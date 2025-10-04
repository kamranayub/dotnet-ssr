using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Interop;
using Microsoft.JavaScript.NodeApi.Runtime;
using SharedLib;

public record SsrResult(int Status, string ContentType, IAsyncEnumerable<ReadOnlyMemory<byte>> HtmlChunks);

public sealed class NodeSsrHost : IAsyncDisposable
{
    private readonly NodeEmbeddingThreadRuntime _rt;

    public NodeSsrHost(string projectDir, string? libNodePath = null)
    {
        NodeEmbeddingPlatform platform = new(new NodeEmbeddingPlatformSettings()
        {
            LibNodePath = libNodePath
        });

        _rt = platform.CreateThreadRuntime(projectDir);

        if (Debugger.IsAttached)
        {
            int pid = Process.GetCurrentProcess().Id;
            Uri inspectionUri = _rt.StartInspector();
            Debug.WriteLine($"Node.js ({pid}) inspector listening at {inspectionUri.AbsoluteUri}");
        }
    }

    private static int AsInt32(JSCallbackArgs args, int argIndex)
    {
        if (!args[argIndex].IsNumber())
        {
            throw new JSException(new JSError(
                $"Wrong type of args[{argIndex}]. Expects a number.",
                JSErrorType.TypeError));
        }

        return (int)args[argIndex];
    }

    public async Task RenderAsync(HttpRequest request, HttpResponse response)
    {
        await _rt.RunAsync(async () =>
        {
            /* Try to attach dotnet functions globally? */
            var sharedMathAdd = JSValue.CreateFunction("add", args =>
            {
                var a = AsInt32(args, 0);
                var b = AsInt32(args, 1);
                return SharedMath.Add(a, b);
            }, IntPtr.Zero);

            var dotnetMethods = JSValue.CreateObject();
            var sharedMathModule = JSValue.CreateObject();
            dotnetMethods["SharedMath"] = sharedMathModule;
            sharedMathModule["add"] = sharedMathAdd;

            JSValue.Global.SetProperty("dotnet", dotnetMethods);

            var mod = await _rt.ImportAsync("./build/server/index.js", esModule: true);
            var handlerJs = mod.GetProperty("default");
            var abortController = JSValue.Global["AbortController"].CallAsConstructor();
            var webRequest = JSValue.Global["Request"];
            var jsHeaders = JSValue.Global["Headers"].CallAsConstructor();

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

            var req = webRequest.CallAsConstructor(request.GetDisplayUrl(), requestInit);
            var handler = handlerJs.Call(handlerJs, req).As<JSPromise>() ?? throw new InvalidOperationException("React Router handler is not returning an expected JSPromise");

            /* type: https://developer.mozilla.org/en-US/docs/Web/API/Response */
            var res = await handler.AsTask(request.HttpContext.RequestAborted);
            var status = (int)res["status"];
            var body = res["body"];
            var contentType = (string?)res["headers"].CallMethod("get", "content-type") ?? "text/html; charset=utf-8";

            response.StatusCode = status;
            response.ContentType = contentType;

            if (body.IsNullOrUndefined())
            {
                return;
            }

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

    private static JSMap ToJSMap(IHeaderDictionary h)
    {
        var map = new JSMap();

        foreach (var kv in h)
        {
            var value = new JSArray();
            foreach (var val in kv.Value)
            {
                if (val != null)
                {
                    value.Add(val);
                }
            }
            map.Add(kv.Key, value);
        }

        return map;
    }
}