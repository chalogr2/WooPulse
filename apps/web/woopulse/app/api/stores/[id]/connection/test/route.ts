import { proxyToApi } from "@/lib/api-proxy";

// POST /api/stores/:id/connection/test -> re-test saved keys (Owner/Admin)
export async function POST(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/connection/test`, { method: "POST" });
}
