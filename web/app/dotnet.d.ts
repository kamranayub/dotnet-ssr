import type SharedLib from "../../lib/dist/SharedLib.d.ts";

declare global {
    var dotnet: {
        SharedLib: typeof SharedLib;
    };
}
