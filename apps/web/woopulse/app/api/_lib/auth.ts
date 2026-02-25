import { cookies } from "next/headers";

/**
 * Returns a bearer token string if present, otherwise null.
 * Supports:
 * - access_token (if you ever add it)
 * - __session__ (single)
 * - __session__0/__session__1/... (split token across multiple cookies)
 * - sb-*-auth-token (Supabase cookie with JSON containing access_token)
 */
export async function getBearerToken(): Promise<string | null> {
  const jar = await cookies();

  // 1) common case
  const direct =
    jar.get("access_token")?.value ??
    jar.get("__session__")?.value ??
    null;

  if (direct) return direct;

  // 2) split cookie chunks like __session__0, __session__1 ...
  const sessionParts = jar
    .getAll()
    .filter((c) => c.name.startsWith("__session__"))
    .sort((a, b) => a.name.localeCompare(b.name)); // __session__0 then __session__1

  if (sessionParts.length) {
    const joined = sessionParts.map((p) => p.value).join("");
    if (joined.trim().length) return joined;
  }

  // 3) Supabase cookie format sb-<project>-auth-token
  const sb = jar
    .getAll()
    .find((c) => c.name.startsWith("sb-") && c.name.endsWith("-auth-token"));

  if (sb?.value) {
    try {
      const parsed = JSON.parse(sb.value);
      const token = parsed?.access_token ?? parsed?.currentSession?.access_token;
      if (typeof token === "string" && token.length) return token;
    } catch {
      // ignore
    }
  }

  return null;
}