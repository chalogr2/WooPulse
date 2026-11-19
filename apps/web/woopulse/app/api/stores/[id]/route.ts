import { proxyToApi } from "@/lib/api-proxy";

// In Next.js 15+, route params arrive as a Promise, so we "await ctx.params".
type Ctx = { params: Promise<{ id: string }> };

// PUT /api/stores/:id -> PUT {API_BASE}/stores/:id   (body: { name })
export async function PUT(req: Request, ctx: Ctx) {
  const { id } = await ctx.params;
  // encodeURIComponent: the id comes from the URL, so a crafted value like "1/../debug"
  // could otherwise change which API path we call. Encoding keeps it a single segment.
  return proxyToApi(`/stores/${encodeURIComponent(id)}`, { method: "PUT", body: await req.text() });
}

// DELETE /api/stores/:id -> DELETE {API_BASE}/stores/:id   (soft delete)
// "_" is a naming convention for "parameter required by the signature but unused".
export async function DELETE(_: Request, ctx: Ctx) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}`, { method: "DELETE" });
}
