import { proxyToApi } from "@/lib/api-proxy";

// POST /api/stores/:id/sync -> POST {API_BASE}/stores/:id/sync   ("Sync now", any member)
// The API answers 202 Accepted: the sync is QUEUED and runs in the background (Hangfire).
export async function POST(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/sync`, { method: "POST" });
}
