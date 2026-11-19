import { proxyToApi } from "@/lib/api-proxy";

// GET /api/stores/:id/analytics?from=YYYY-MM-DD&to=YYYY-MM-DD -> same on the .NET API (any member)
export async function GET(req: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;

  // Forward ONLY the parameters we expect (an allow-list), rebuilt with URLSearchParams so
  // they're encoded properly. Never paste the raw incoming query string into the API URL.
  const incoming = new URL(req.url).searchParams;
  const qs = new URLSearchParams();
  for (const key of ["from", "to"]) {
    const value = incoming.get(key);
    if (value) qs.set(key, value);
  }

  const query = qs.toString();
  return proxyToApi(`/stores/${encodeURIComponent(id)}/analytics${query ? `?${query}` : ""}`);
}
