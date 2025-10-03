using System;
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

public record SsrResult(int Status, string ContentType, IAsyncEnumerable<ReadOnlyMemory<byte>> HtmlChunks);

public sealed class NodeSsrHost : IAsyncDisposable
{
    private readonly NodeEmbeddingThreadRuntime _rt;
    private JSReference? _serverHandlerRef;

    public NodeSsrHost(string projectDir, string? libNodePath = null)
    {
        NodeEmbeddingPlatform platform = new(new NodeEmbeddingPlatformSettings()
        {
            LibNodePath = libNodePath
        });

        _rt = platform.CreateThreadRuntime(projectDir);

        // Warm up Node & cache handler
        _rt.RunAsync(async () =>
        {
            var mod = await _rt.ImportAsync("./build/server/index.js", esModule: true);
            var handler = mod.GetProperty("default");
            _serverHandlerRef = new JSReference(handler); // keep alive across requests
            return 0;
        }).GetAwaiter().GetResult();

        if (Debugger.IsAttached)
        {
            int pid = Process.GetCurrentProcess().Id;
            Uri inspectionUri = _rt.StartInspector();
            Debug.WriteLine($"Node.js ({pid}) inspector listening at {inspectionUri.AbsoluteUri}");
        }
    }

    public async Task RenderAsync(HttpRequest request, HttpResponse response)
    {
        await _rt.RunAsync(async () =>
        {
            if (_serverHandlerRef == null)
            {
                throw new InvalidOperationException("No reference to SSR request handler");
            }
            var handlerJs = _serverHandlerRef.GetValue();
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
            var handler = handlerJs.Call(handlerJs, req).As<JSPromise>();

            if (handler == null)
            {
                throw new InvalidOperationException("React Router handler is not returning an expected JSPromise");
            }

            /* type: https://developer.mozilla.org/en-US/docs/Web/API/Response */
            var res = await handler.Value.AsTask(request.HttpContext.RequestAborted);
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
            await response.BodyWriter.FlushAsync(request.HttpContext.RequestAborted);
        });

    }

    private async Task WriteReadableStreamToResponseBody(JSValue readableStream, PipeWriter writer, CancellationToken cancellationToken)
    {
        using var asyncScope = new JSAsyncScope();
        var readableStreamDefaultReader = readableStream.CallMethod("getReader");
        using JSReference nodeStreamReference = new(readableStreamDefaultReader);

        while (true)
        {
            readableStreamDefaultReader = nodeStreamReference.GetValue();
            var readPromise = readableStreamDefaultReader.CallMethod("read").As<JSPromise>()!;
            var readResult = await readPromise.Value.AsTask(cancellationToken);
            var done = (bool)readResult["done"];
            if (done)
            {
                break;
            }

            var chunk = (JSTypedArray<byte>)readResult["value"];
            var len = chunk.Length;

            // rent once per chunk
            var owner = System.Buffers.MemoryPool<byte>.Shared.Rent(len);

            // copy from V8 buffer to managed memory you control
            chunk.Memory.Span[..len].CopyTo(owner.Memory.Span);

            await writer.WriteAsync(owner.Memory[..len], cancellationToken);
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