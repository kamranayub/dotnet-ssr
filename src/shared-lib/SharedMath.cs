// /src/SharedLib/SharedMath.cs
using Microsoft.JavaScript.NodeApi;

namespace MyCompany.Shared;

[JSExport] // exported into the dynamic .NET surface in JS
public static class SharedMath
{
    public static int Add(int a, int b) => a + b;
}