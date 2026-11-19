import { proxyToApi } from "@/lib/api-proxy";

// GET /api/stores/deleted -> GET {API_BASE}/stores/deleted   (my soft-deleted stores)
// Next.js routing: a STATIC folder ("deleted") wins over a dynamic one ("[id]"), so
// /api/stores/deleted comes here, not to [id]/route.ts with id = "deleted".
export async function GET() {
  return proxyToApi("/stores/deleted");
}
