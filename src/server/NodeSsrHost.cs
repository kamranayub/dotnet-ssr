using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

    public async Task RenderAsync(HttpRequest request, HttpResponse response)
    {
        await _rt.RunAsync(async () =>
        {
            var mod = await _rt.ImportAsync("./build/server/index.js", esModule: true);
            var req = JSValue.CreateObject();
            req["url"] = request.GetDisplayUrl();
            req["method"] = request.Method;
            req["headers"] = ToJSMap(request.Headers);
            var global = JSValue.Global;
            var abortController = global["AbortController"].CallAsConstructor();
            req["signal"] = abortController["signal"];

            /* TODO: body */
            var handler = mod.CallMethod("default" /* export handler as default */, req).As<JSPromise>();

            if (handler == null)
            {
                throw new InvalidOperationException("React Router handler is not returning an expected JSPromise");
            }

            /* type: https://developer.mozilla.org/en-US/docs/Web/API/Response */
            var res = await handler.Value.AsTask();
            var status = (int)res["status"];
            var body = res["body"];
            var contentType = (string?)res["headers"].CallMethod("get", "content-type") ?? "text/html; charset=utf-8";

            response.StatusCode = status;
            response.ContentType = contentType;

            if (body.IsNullOrUndefined())
            {
                return;
            }

            /* @type ReadableStream */
            var bodyDefaultReadableStream = res["body"].CallMethod("getReader");

            await foreach (var chunk in ReadFromDefaultReadableStreamAsync(bodyDefaultReadableStream))
            {
                await response.BodyWriter.WriteAsync(chunk);
                await response.BodyWriter.FlushAsync();
            }
        });

    }

    private async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadFromDefaultReadableStreamAsync(JSValue readableStreamDefaultReader)
    {
        using var asyncScope = new JSAsyncScope();
        using JSReference nodeStreamReference = new(readableStreamDefaultReader);
        
        var charsReceived = 0;
        while (true)
        {
            readableStreamDefaultReader = nodeStreamReference.GetValue();
            var readPromise = readableStreamDefaultReader.CallMethod("read").As<JSPromise>();
            var readResult = await readPromise.Value.AsTask();
            var done = (bool)readResult["done"];
            if (done)
            {
                break;
            }

            var chunk = (JSTypedArray<byte>)readResult["value"];
            var chunkSize = chunk.Length;
            charsReceived += chunkSize;

            yield return chunk.Memory;
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