import { proxyToApi } from "@/lib/api-proxy";

// POST /api/stores/:id/restore -> POST {API_BASE}/stores/:id/restore   (Owner only)
export async function POST(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/restore`, { method: "POST" });
}
