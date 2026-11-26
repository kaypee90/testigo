import type { MetadataRoute } from "next";

/** The staff dashboard is not for search engines: every page is behind sign-in. */
export default function robots(): MetadataRoute.Robots {
  return { rules: { userAgent: "*", disallow: "/" } };
}
