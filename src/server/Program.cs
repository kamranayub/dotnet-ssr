// /src/Server/Program.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Choose where Node will look for your bundled SSR files
var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web"));
var assetsRoot = Path.Combine(projectDir, "build", "client", "assets");
var clientRoot = Path.Combine(projectDir, "build", "client");

builder.Services.AddSingleton(_ => new NodeSsrHost(projectDir, Path.Combine(baseDir, "runtimes/osx-arm64/native/libnode.dylib")));

var app = builder.Build();

app.MapStaticAssets().ShortCircuit();

if (Directory.Exists(assetsRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assetsRoot),
        RequestPath = "/assets",
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    });
}

if (Directory.Exists(clientRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(clientRoot),
        RequestPath  = "",
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=300";
        }
    });
}

app.UseRouting();
app.MapGet("/{**path}", async (HttpContext ctx, NodeSsrHost host) =>
{
    var result = await host.RenderAsync(ctx.Request);
    ctx.Response.StatusCode = result.Status;
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(result.Html);
});

app.Run();