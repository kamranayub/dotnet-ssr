using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using System;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

var projectDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "web"));
var assetsRoot = Path.Combine(projectDir, "build", "client", "assets");
var clientRoot = Path.Combine(projectDir, "build", "client");

builder.Services.AddSingleton(_ => new NodeSsrHost(projectDir));

var app = builder.Build();

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
        RequestPath = "",
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