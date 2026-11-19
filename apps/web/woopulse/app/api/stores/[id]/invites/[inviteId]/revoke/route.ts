import { proxyToApi } from "@/lib/api-proxy";

type Ctx = { params: Promise<{ id: string; inviteId: string }> };

// POST /api/stores/:id/invites/:inviteId/revoke -> POST {API_BASE}/stores/:id/invites/:inviteId/revoke
export async function POST(_: Request, ctx: Ctx) {
  const { id, inviteId } = await ctx.params;
  return proxyToApi(
    `/stores/${encodeURIComponent(id)}/invites/${encodeURIComponent(inviteId)}/revoke`,
    { method: "POST" }
  );
}
