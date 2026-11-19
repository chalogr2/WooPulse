import { proxyToApi } from "@/lib/api-proxy";

// GET /api/stores -> GET {API_BASE}/stores
export async function GET() {
  return proxyToApi("/stores");
}

// POST /api/stores -> POST {API_BASE}/stores   (body: { name })
export async function POST(req: Request) {
  return proxyToApi("/stores", { method: "POST", body: await req.text() });
}
