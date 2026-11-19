import { proxyToApi } from "@/lib/api-proxy";

type Ctx = { params: Promise<{ id: string }> };
const path = (id: string) => `/stores/${encodeURIComponent(id)}/connection`;

// GET /api/stores/:id/connection -> status (any member). 204 = not connected.
export async function GET(_: Request, ctx: Ctx) {
  const { id } = await ctx.params;
  return proxyToApi(path(id));
}

// PUT /api/stores/:id/connection -> save + test (Owner). Body: { shopUrl, consumerKey, consumerSecret }
// The keys pass through this server route on their way to the API; they are never logged here.
export async function PUT(req: Request, ctx: Ctx) {
  const { id } = await ctx.params;
  return proxyToApi(path(id), { method: "PUT", body: await req.text() });
}

// DELETE /api/stores/:id/connection -> disconnect (Owner)
export async function DELETE(_: Request, ctx: Ctx) {
  const { id } = await ctx.params;
  return proxyToApi(path(id), { method: "DELETE" });
}
