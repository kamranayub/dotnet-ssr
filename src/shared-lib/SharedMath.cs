using Microsoft.JavaScript.NodeApi;

namespace SharedLib;

[JSExport]
public static class SharedMath
{
    public static int Add(int a, int b) => a + b;
}