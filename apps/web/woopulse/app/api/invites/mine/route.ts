import { proxyToApi } from "@/lib/api-proxy";

// GET /api/invites/mine -> GET {API_BASE}/invites/mine   (pending invites for my email)
export async function GET() {
  return proxyToApi("/invites/mine");
}
