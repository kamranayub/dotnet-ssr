import type { Route } from "./+types/suspense";
import React from "react";

export async function loader() {
  // note this is NOT awaited
  let nonCriticalData = new Promise<string>((res) =>
    setTimeout(() => res("non-critical"), 2000)
  );

  let criticalData = await new Promise<string>((res) =>
    setTimeout(() => res("critical"), 300)
  );

  return {
    nonCriticalData,
    criticalData,
  };
}

export function meta({}: Route.MetaArgs) {
  return [
    { title: "New React Router App" },
    { name: "description", content: "Welcome to React Router!" },
  ];
}

export function ServerComponent({ loaderData }: Route.ComponentProps) {
  const { criticalData, nonCriticalData } = loaderData;

  return (
    <>
      <div className="flex justify-center items-center">
        <div className="p-8 rounded-lg border border-gray-300 shadow-md">

          <h1 className="text-2xl font-bold mt-5">Streaming example</h1>
          <h2 className="text-xl">Critical data value: {criticalData}</h2>

          <React.Suspense fallback={<div>Loading...</div>}>
            <NonCriticalUI p={nonCriticalData} />
          </React.Suspense>
        </div>
      </div>
    </>
  );
}

function NonCriticalUI({ p }: { p: Promise<string> }) {
  let value = React.use(p);
  return <h3>Non critical value: {value}</h3>;
}
