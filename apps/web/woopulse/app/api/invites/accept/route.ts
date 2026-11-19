import { proxyToApi } from "@/lib/api-proxy";

// POST /api/invites/accept -> POST {API_BASE}/invites/accept   (body: { token })
export async function POST(req: Request) {
  return proxyToApi("/invites/accept", { method: "POST", body: await req.text() });
}
