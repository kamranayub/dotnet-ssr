// /src/Server/NodeSsrHost.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Interop;
using Microsoft.JavaScript.NodeApi.Runtime;

public record SsrResult(int Status, string Html);

public sealed class NodeSsrHost : IAsyncDisposable
{
    private readonly NodeEmbeddingThreadRuntime _rt;

    public NodeSsrHost(string projectDir, string libnodePath)
    {
        // Find the path to the libnode binary for the current platform.
        NodeEmbeddingPlatform platform = new(new NodeEmbeddingPlatformSettings()
        {
            LibNodePath = libnodePath
        });

        _rt = platform.CreateThreadRuntime(projectDir,
        new NodeEmbeddingRuntimeSettings
        {
            MainScript =
                "globalThis.require = require('module').createRequire(process.execPath);\n"
        });

        if (Debugger.IsAttached)
        {
            int pid = Process.GetCurrentProcess().Id;
            Uri inspectionUri = _rt.StartInspector();
            Debug.WriteLine($"Node.js ({pid}) inspector listening at {inspectionUri.AbsoluteUri}");
        }
    }

    public async Task<SsrResult> RenderAsync(HttpRequest request)
    {
        return await _rt.RunAsync(async () =>
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
            var body = res["body"];

            if (body.IsNullOrUndefined())
            {
                return new SsrResult(
                    Status: (int)res["status"],
                    Html: "<div>No body</div>"
                );
            }

            var status = (int)res["status"];
            var bodyPromise = res.CallMethod("text").As<JSPromise>()!.Value;
            var bodyContents = (string)await bodyPromise.AsTask();

            return new SsrResult(
                Status: status,
                Html: bodyContents
            );
        });
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