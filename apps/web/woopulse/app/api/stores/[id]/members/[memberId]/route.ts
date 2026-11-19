import { proxyToApi } from "@/lib/api-proxy";

// Two dynamic segments in the folder path ([id] and [memberId]) → two params.
type Ctx = { params: Promise<{ id: string; memberId: string }> };

function memberPath(id: string, memberId: string) {
  return `/stores/${encodeURIComponent(id)}/members/${encodeURIComponent(memberId)}`;
}

// PUT /api/stores/:id/members/:memberId -> change role   (body: { role: 2 | 3 }, Owner only)
export async function PUT(req: Request, ctx: Ctx) {
  const { id, memberId } = await ctx.params;
  return proxyToApi(memberPath(id, memberId), { method: "PUT", body: await req.text() });
}

// DELETE /api/stores/:id/members/:memberId -> remove member
export async function DELETE(_: Request, ctx: Ctx) {
  const { id, memberId } = await ctx.params;
  return proxyToApi(memberPath(id, memberId), { method: "DELETE" });
}
