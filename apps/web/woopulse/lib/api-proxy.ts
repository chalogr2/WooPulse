import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

// The .NET API's base URL, e.g. http://localhost:5113 (from .env.local).
// The trailing "!" is TypeScript's non-null assertion: "trust me, this is defined".
const API_BASE = process.env.API_BASE_URL!;

// Options a route can pass. The "?" makes each property optional.
type ProxyOptions = {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: string; // raw JSON text, forwarded as-is
};

/**
 * What: forwards one request from the Next.js BFF to the .NET API, attaching the
 *       user's Auth0 access token, and passes the API's answer back unchanged.
 *
 * Why:  every /app/api/** route used to repeat this logic in a slightly different way,
 *       and some versions crashed with a 500 when the user was logged out. Having ONE
 *       function means every route behaves the same way, and a fix lands everywhere.
 *
 * Returns:
 *   401  if the user has no session (not logged in)
 *   502  if the .NET API can't be reached (e.g. it isn't running)
 *   otherwise: the API's exact status code, body and content-type
 */
export async function proxyToApi(path: string, options: ProxyOptions = {}) {
  // 1) Get the access token from the encrypted session cookie.
  //    auth0.getAccessToken() THROWS when there is no session, so we catch it and
  //    turn it into a proper 401 shaped like ASP.NET's ProblemDetails
  //    ({ title, detail, status }), which the frontend already knows how to display.
  let token: string;
  try {
    // Destructuring assignment: pull the "token" property out of the returned object.
    // The surrounding parentheses are required when destructuring into an existing variable.
    ({ token } = await auth0.getAccessToken());
  } catch {
    return NextResponse.json(
      { title: "Not authenticated", detail: "Please log in.", status: 401 },
      { status: 401 }
    );
  }

  // 2) Build headers. Only send Content-Type when there is a body.
  //    Record<string, string> = "an object whose keys and values are strings".
  const headers: Record<string, string> = { Authorization: `Bearer ${token}` };
  if (options.body !== undefined) headers["Content-Type"] = "application/json";

  // 3) Call the API. If it's down, fetch() throws (network error), so we catch it too.
  let upstream: Response;
  try {
    upstream = await fetch(`${API_BASE}${path}`, {
      method: options.method ?? "GET", // "??" = use the right side only if left is null/undefined
      headers,
      body: options.body,
      cache: "no-store", // never cache per-user data
    });
  } catch {
    return NextResponse.json(
      { title: "API unreachable", detail: "The WooPulse API is not responding.", status: 502 },
      { status: 502 }
    );
  }

  // 4) Pass the API's response through untouched.
  //    We read it as text (not .json()) because some responses have an empty body,
  //    e.g. 204 No Content after a delete, or a bare 401/403/404 and .json() would throw.
  const text = await upstream.text();
  if (!text) return new NextResponse(null, { status: upstream.status });

  return new NextResponse(text, {
    status: upstream.status,
    headers: {
      "content-type": upstream.headers.get("content-type") ?? "application/json",
    },
  });
}

/**
 * What: true only when running `pnpm dev`.
 * Why:  debug routes must not exist in a production build (`pnpm build && pnpm start`).
 */
export const isDev = process.env.NODE_ENV === "development";

/** Standard 404 used by debug routes outside development. */
export function notFound() {
  return new NextResponse(null, { status: 404 });
}
