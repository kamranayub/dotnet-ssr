# dotnet-ssr

> [!CAUTION]
> There be dragons here. :dragon:

What if you could use your favorite frontend framework like React Router, Svelte, Vue, Astro, etc. and take advantage of a .NET backend that supports SSR natively?

This is the question this repo seeks to answer.

## The Problem

There are many SSR-enabled frameworks like React Router, Next.js, Astro, Vue, and SvelteKit. They let you build full-stack JavaScript/TypeScript applications and they can be served on different JavaScript runtimes like Node.js, Deno, or bun.

However, backend servers (aka APIs) might depend on different tech stacks, like Java or .NET.

Even though these stacks _can_ render HTML (very well), they cannot run JavaScript code natively.

What they _can_ do is spawn a Node child process and manage communication that way, but this is sub-optimal and doesn't scale for production use cases.

So to workaround this, you can implement a "Backend-for-Frontend" (BFF) architecture where you essentially support SSR with JavaScript servers but then call into your own backend APIs through traditional web APIs (like `fetch`, or SSE, or WebSockets, etc.).

However, this has an obvious problem: you are still stuck scaling _both_ frontend (JavaScript) and backend (other tech) servers for production. This is complex by its nature.

## The Dream

In an ideal world, your SSR code should be able to "just call" your backend code without a web API layer in-between.

However, in order to do that, the frontend framework JavaScript code needs to be able to "interop" with the backend language.

How do you get Node.js code, written in C++, to run in languages that _don't_ interop with C++, like WASM, C#, Rust, and Java?

[By implementing a C API for embedding!](https://github.com/nodejs/node/pull/54660)

Once you have that, suddenly you can now embed and interop with the Node C API (`libnode`) from higher-level languages like C#, Rust, and Java.

_And_ once you can do that, all that's left is ensuring compatibility with different frameworks.

## The Reality

Right now the C API is _highly experimental_ and in draft -- it's not even merged into Node yet!

That said, Microsoft has still built [node-api-dotnet](https://microsoft.github.io/node-api-dotnet/) for use in Semantic Kernel and that's what I'm using.

I'm choosing [React Router](https://reactrouter.com) to experiment with first because it can be used as a full framework, supports React Server Components (RSC), and it's what I'm using on my frontend -- so it's personal :smile: