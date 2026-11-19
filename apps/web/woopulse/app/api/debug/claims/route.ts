import { isDev, notFound, proxyToApi } from "@/lib/api-proxy";

// GET /api/debug/claims -> GET {API_BASE}/debug/claims
// Dev only: shows every claim inside my access token (useful for Auth0 setup).
export async function GET() {
  if (!isDev) return notFound();
  return proxyToApi("/debug/claims");
}
