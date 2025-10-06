using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.DotNetHost;
using Microsoft.JavaScript.NodeApi.Interop;
using Microsoft.JavaScript.NodeApi.Runtime;
using SharedLib;

public record SsrResult(int Status, string ContentType, IAsyncEnumerable<ReadOnlyMemory<byte>> HtmlChunks);

public sealed class NodeSsrHost : IAsyncDisposable
{
    private readonly NodeEmbeddingThreadRuntime _rt;
    private readonly ILogger<NodeSsrHost> _logger;
    private readonly IEnumerable<Assembly>? _sharedAssemblies;

    public NodeSsrHost(ILogger<NodeSsrHost> logger, string projectDir, IEnumerable<Assembly>? sharedAssemblies = null, string? libNodePath = null)
    {
        NodeEmbeddingPlatform platform = new(new NodeEmbeddingPlatformSettings()
        {
            LibNodePath = libNodePath
        });
        
        _logger = logger;
        _sharedAssemblies = sharedAssemblies;
        _logger.LogDebug("Creating JS runtime");
        _rt = platform.CreateThreadRuntime(projectDir);
        LoadDotNetManagedHost();

        if (Debugger.IsAttached)
        {
            int pid = Process.GetCurrentProcess().Id;
            Uri inspectionUri = _rt.StartInspector();
            Debug.WriteLine($"Node.js ({pid}) inspector listening at {inspectionUri.AbsoluteUri}");
        }
    }

    /// <summary>
    /// Creates a ManagedHost that will allow interop with generated modules
    /// when running under SSR (which already has a CLR host initialized)
    /// </summary>
    /// <remarks>
    /// See: https://github.com/microsoft/node-api-dotnet/issues/330
    /// </remarks>
    private void LoadDotNetManagedHost()
    {
        if (_sharedAssemblies == null) return;

        _rt.Run(() =>
        {
            JSObject managedTypes = (JSObject)JSValue.CreateObject();
            JSValue.Global.SetProperty("dotnetHost", managedTypes);
            ManagedHost managedHost = new(managedTypes);

            _logger.LogInformation("Loaded ManagedHost");

            return 0;
        });
    }

    public async Task RenderAsync(HttpRequest request, HttpResponse response)
    {
        await _rt.RunAsync(async () =>
        {
            _logger.LogDebug("Importing SSR server");
            var mod = await _rt.ImportAsync("./build/server/index.js", esModule: true);
            var handlerJs = mod.GetProperty("default");
            var abortController = JSValue.Global["AbortController"].CallAsConstructor();
            var webRequest = JSValue.Global["Request"];
            var jsHeaders = JSValue.Global["Headers"].CallAsConstructor();

            var dotnetHost = JSValue.Global["dotnetHost"];
            _logger.LogTrace("dotNetHost properties: {0}", string.Join(", ", dotnetHost.GetPropertyNames().As<JSArray>()!.Value.Select(val => (string)val)));
            _logger.LogTrace("dotNetHost.require defined? {0}", !dotnetHost["require"].IsNullOrUndefined());
            _logger.LogTrace("dotNetHost.require is fn? {0}", dotnetHost["require"].IsFunction());

            foreach (var (k, v) in request.Headers)
            {
                foreach (var val in v)
                {
                    if (val != null)
                    {
                        jsHeaders.CallMethod("append", k, val);
                    }
                }
            }

            var requestInit = JSValue.CreateObject();
            requestInit["method"] = request.Method;
            requestInit["headers"] = jsHeaders;
            requestInit["signal"] = abortController["signal"];
            /* TODO: body if needed */

            using var reg = request.HttpContext.RequestAborted.Register(() =>
            {
                abortController.CallMethod("abort");
            });

            _logger.LogDebug("Invoking SSR handler with request");

            var req = webRequest.CallAsConstructor(request.GetDisplayUrl(), requestInit);
            var handler = handlerJs.Call(handlerJs, req).As<JSPromise>() ?? throw new InvalidOperationException("React Router handler is not returning an expected JSPromise");

            /* type: https://developer.mozilla.org/en-US/docs/Web/API/Response */
            var res = await handler.AsTask(request.HttpContext.RequestAborted);
            var status = (int)res["status"];
            var body = res["body"];
            var contentType = (string?)res["headers"].CallMethod("get", "content-type") ?? "text/html; charset=utf-8";

            _logger.LogDebug("Received SSR response");

            response.StatusCode = status;
            response.ContentType = contentType;

            if (body.IsNullOrUndefined())
            {
                return;
            }

            _logger.LogDebug("Reading SSR body stream");

            await WriteReadableStreamToResponseBody(res["body"], response.BodyWriter, request.HttpContext.RequestAborted);
        });

    }

    private async Task WriteReadableStreamToResponseBody(JSValue readableStream, PipeWriter writer, CancellationToken cancellationToken)
    {
        var readable = _rt.Import("node:stream", "Readable");
        var nodeReadable = readable.CallMethod("fromWeb", readableStream);

        using var nodeStream = (NodeStream)nodeReadable;
        const int ReadChunkSize = 4096;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadChunkSize);
        try
        {
            int n;
            while ((n = await nodeStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await writer.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
            }
        }
        catch (Exception ex)
        {
            try
            {
                nodeReadable.CallMethod("destroy", new JSError(ex).Value);
            }
            catch (Exception)
            {
                // Ignore errors from destroy().
                _logger.LogWarning(ex, "Error trying to destroy Readable during body stream");
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await writer.FlushAsync(cancellationToken);
        }
    }

    public ValueTask DisposeAsync()
    {
        _rt.Dispose();
        return ValueTask.CompletedTask;
    }
}