using Microsoft.JavaScript.NodeApi;

namespace SharedLib;

/// <summary>
/// Shared .NET math utilities
/// </summary>
[JSExport]
public static class SharedMath
{
    /// <summary>
    /// Adds two numbers together
    /// </summary>
    /// <param name="a">The first number</param>
    /// <param name="b">The second number</param>
    /// <returns>The added number</returns>
    public static int Add(int a, int b) => a + b;
}