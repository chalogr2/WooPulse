import { proxyToApi } from "@/lib/api-proxy";

// GET /api/stores/:id/members -> GET {API_BASE}/stores/:id/members   (any member)
export async function GET(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/members`);
}
