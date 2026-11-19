import { proxyToApi } from "@/lib/api-proxy";

// POST /api/stores/:id/invites -> POST {API_BASE}/stores/:id/invites   (body: { email, role })
export async function POST(req: Request, ctx: { params: Promise<{ id: string }> }) {
  const { id } = await ctx.params;
  return proxyToApi(`/stores/${encodeURIComponent(id)}/invites`, {
    method: "POST",
    body: await req.text(),
  });
}
