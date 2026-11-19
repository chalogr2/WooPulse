import { proxyToApi } from "@/lib/api-proxy";

// POST /api/stores/:id/leave -> POST {API_BASE}/stores/:id/leave   (anyone except the Owner)
export async function POST(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/leave`, { method: "POST" });
}
