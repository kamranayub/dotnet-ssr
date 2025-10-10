# .NET SSR Host

> [!CAUTION]
> There be dragons here. :dragon: 
> 
> This is still in experiment/prototyping mode and has yet to be tested with a full production .NET app.
>
> **There is no Nuget package available yet as I'm still playing with the API.**

What if you could use your favorite JavaScript metaframework like Astro, TanStack, React Router, SvelteKit, Qwik, Nuxt, Next.js, etc. and take advantage of a .NET backend that supports SSR natively?

To pass the sniff test:

- No Backend-for-Frontend (BFF) needed
- No sacrifices in local DX (ESM imports, no custom tooling, etc.)
- Invoke your .NET code from SSR (with TypeScript declarations!)
- Bring your own SSR-based metaframework
- Support for React Suspense and RSC (w/ streaming)
- It has to be fast (enough)

This repo proves this can all be done, at least on a small scale:

<img width="3822" height="1584" alt="Screenshot 2025-10-01 at 00 17 01" src="https://github.com/user-attachments/assets/80defd95-0b02-4583-87d9-e153f902adc0" />

<img width="1224" height="780" alt="SSR Performance Comparison: Node js vs  NET" src="https://github.com/user-attachments/assets/743242ac-e472-492b-9c38-2725c4d2f032" />

## Video

A quick look at what's going on ([link](http://www.youtube.com/watch?v=_u2ia16_dEw))

[![Explainer video for .NET SSR hosting](http://img.youtube.com/vi/_u2ia16_dEw/0.jpg)](http://www.youtube.com/watch?v=_u2ia16_dEw "Rendering React SSR in .NET")

## Features

### Tested and Working

- :white_check_mark: React Router 7 Framework Mode support
- :white_check_mark: React 19 and React Server Components (RSC)
- :white_check_mark: Calling .NET code from React Router's `loader`
- :white_check_mark: Works in `npm run dev` and `dotnet run` (SSR)
- :white_check_mark: .NET ESM with generated TypeScript typedefs (`.d.ts`)

### TODO

- [ ] Call `use server` functions (RPC endpoints)
- [ ] Invoke POST/other method types from frontend to React Router SSR
- [ ] Async server components
- [ ] Test other metaframeworks

# Quick Look

The prototype consists of four parts that make it work:

- [node-api-dotnet](https://github.com/microsoft/node-api-dotnet), which marshals .NET <-> Node.js calls _in-process_
- An ASP.NET request endpoint that instantiates the Node.js runtime
- Glue code that marshals ASP.NET and Web-native req and res objects to and from the SSR entrypoint (`HttpRequest` -> `Request`, `Response` -> `HttpResponse`), with streaming support
- A Vite plugin to rewrite .NET lib ESM import paths to DLLs

**app/routes/home.tsx**

The React Router home route:

```ts
import type { Route } from "./+types/home";

/**
 *  This ESM module is auto-generated on dotnet build:
 * - lib/SharedLib.mjs
 * - lib/SharedLib.d.ts
*/
import { SharedMath } from "lib/SharedLib";

export async function loader() {
  return {
    message: "Calling .NET from SSR loader!",
    element: <p>2 + 2 = {SharedMath.add(2, 2)}</p>,
  };
}

export function ServerComponent({ loaderData }: Route.ComponentProps) {
  const { element, message } = loaderData;

  return (
    <>
      <div className="flex justify-center items-center">
        <div className="p-8 rounded-lg border border-gray-300 shadow-md">
          <h1 className="text-2xl font-bold">{message}</h1>
          <div>{element}</div>
        </div>
      </div>
    </>
  );
}
```

**shared-lib/SharedLib/SharedMath.cs**

The shared .NET math utility:

```c#
using Microsoft.JavaScript.NodeApi;

namespace SharedLib;

[JSExport]
public static class SharedMath
{
    public static int Add(int a, int b) => a + b;
}
```

Running the app:

```sh
# In dev mode
cd web
npm run dev

# In prod mode
cd web
npm run build
cd ../src/server
dotnet run
```

# Motivation

## The Problem

There are many SSR-enabled frameworks like React Router, Next.js, Astro, Vue, and SvelteKit. They let you build full-stack JavaScript/TypeScript applications and they can be served on different JavaScript runtimes like Node.js, Deno, or bun.

However, backend servers (aka APIs) might depend on different tech stacks, like Java or .NET.

Even though these stacks _can_ render HTML (very well), they cannot run JavaScript code natively.

What they _can_ do is spawn a Node child process and manage communication that way, but this is sub-optimal and doesn't scale for production use cases.

So to workaround this, you can implement a "Backend-for-Frontend" (BFF) architecture where you essentially support SSR with JavaScript servers but then call into your own backend APIs through traditional web APIs (like `fetch`, or SSE, or WebSockets, etc.).

However, this has an obvious problem: you are still stuck scaling _both_ frontend (JavaScript) and backend (other tech) servers for production. This is complex.

## The Dream

In an ideal world, you'd have **one [type of] server.** Just the backend server. But you'd still write all your SSR code as TypeScript/JavaScript, and then "just call" any other backend language services _without_ a web API layer in-between.

However, in order to do that, the frontend framework JavaScript code needs to be able to "interop" with the backend language. This is complicated because of data types, memory allocation, and all sorts of other nonsense.

How do you get Node.js code, written in C++, to run in languages that _don't_ interop with C++, like WASM, C#, Rust, and Java?

[By implementing a C API for embedding!](https://github.com/nodejs/node/pull/54660)

Once you have that, suddenly you can now embed and interop with the Node C API (`libnode`) from higher-level languages like C#, Rust, and Java.

_And_ once you can do that, all that's left is ensuring compatibility with different frameworks.

## The Reality

Right now the C API is _highly experimental_ and in draft -- it's not even merged into Node yet!

That said, Microsoft has still built [node-api-dotnet](https://microsoft.github.io/node-api-dotnet/) for use in Semantic Kernel and that's what I'm using.

I'm choosing [React Router](https://reactrouter.com) to experiment with first because it can be used as a full framework, supports React Server Components (RSC), and it's what I'm using on my frontend -- so it's personal :smile:

# Building

## Prerequisites

- Node.js 20
- .NET 8

## Notes

- In .NET 9, Windows doesn't work (crashes when creating runtime) :cry: ([issue](https://github.com/microsoft/node-api-dotnet/issues/440))
- In .NET 8, debugging the server project doesn't work :cry: ([issue](https://github.com/microsoft/node-api-dotnet/issues/440))
