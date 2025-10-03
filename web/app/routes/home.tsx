import type { Route } from "./+types/home";
import { Welcome } from "../welcome/welcome";

export async function loader() {
  return {
    message: "Message from the server!",
    element: <p>Element from the server!</p>,
  };
}

export function meta({}: Route.MetaArgs) {
  return [
    { title: "New React Router App" },
    { name: "description", content: "Welcome to React Router!" },
  ];
}

export function ServerComponent({ loaderData }: Route.ComponentProps) {
  const { element, message } = loaderData;

  return (
    <>
      <Welcome />
      <div className="flex justify-center items-center">
        <div className="p-8 rounded-lg border border-gray-300 shadow-md">
          <h1 className="text-2xl font-bold">{message}</h1>
          <div>{element}</div>
        </div>
      </div>
    </>
  );
}