import { isDev, notFound, proxyToApi } from "@/lib/api-proxy";

// GET /api/invites/mine/debug -> GET {API_BASE}/invites/mine/debug
// Dev only: returns my pending invites INCLUDING their tokens, which is how the
// "Accept this invite" button works locally. The API also 404s this outside Development.
export async function GET() {
  if (!isDev) return notFound();
  return proxyToApi("/invites/mine/debug");
}
