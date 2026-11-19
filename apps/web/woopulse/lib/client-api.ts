// Browser-side helpers for calling our own Next.js /api/* routes (the BFF).
// Moved here from app/page.tsx (DEVLOG #010) so every component can share them.
// (Compare lib/api-proxy.ts, which runs on the SERVER and calls the .NET API.)

/** Error shape shown in the UI. Mirrors ASP.NET ProblemDetails { title, detail }. */
export type ApiError = {
  status: number;
  title?: string;
  detail?: string;
  raw?: string;
};

// What a ProblemDetails JSON body may contain. Using a real type instead of "any"
// keeps TypeScript checking our property names.
type ProblemBody = { title?: string; detail?: string };

/**
 * fetch() + JSON parsing + consistent errors.
 * Generic <T>: the caller says what type the response body is, e.g.
 *   const stores = await fetchJson<Store[]>("/api/stores");
 * On non-2xx it THROWS an Error with a structured `.api: ApiError` attached.
 */
export async function fetchJson<T>(input: RequestInfo, init?: RequestInit): Promise<T> {
  const res = await fetch(input, { cache: "no-store", ...init });

  const contentType = res.headers.get("content-type") ?? "";
  const text = await res.text();

  if (res.ok) {
    if (!text) return undefined as unknown as T; // e.g. 204 No Content
    return contentType.includes("application/json") ? (JSON.parse(text) as T) : (text as unknown as T);
  }

  let problem: ProblemBody | null = null;
  if (contentType.includes("application/json") && text) {
    try { problem = JSON.parse(text) as ProblemBody; } catch { /* not JSON, keep raw */ }
  }

  const e = new Error(problem?.detail || problem?.title || `Request failed (${res.status})`) as Error & { api?: ApiError };
  e.api = { status: res.status, title: problem?.title, detail: problem?.detail, raw: text };
  throw e;
}

/**
 * Turn anything caught in a `catch (e)` into an ApiError.
 * In TypeScript a caught value is `unknown` (it could be anything), so we check its
 * shape before using it. That's a "type guard" (`"api" in e`).
 */
export function toApiError(e: unknown): ApiError {
  if (e && typeof e === "object" && "api" in e && e.api) return e.api as ApiError;
  return { status: 0, title: "Unknown error", detail: e instanceof Error ? e.message : String(e) };
}
