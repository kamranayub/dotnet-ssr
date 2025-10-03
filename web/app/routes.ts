import { type RouteConfig, index, route } from "@react-router/dev/routes";

export default [
  index("routes/home.tsx"),
  route("/about", "routes/about.mdx"),
  route("/suspense", "routes/suspense.tsx")
] satisfies RouteConfig;
