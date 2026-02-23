"use client";

import { useEffect, useState } from "react";

type Health = { status: string; utc: string };
type Store = {
  id: number;
  name: string;
  ownerSub: string;
  createdAtUtc: string;
  createdBySub: string;
  updatedAtUtc?: string | null;
  updatedBySub?: string | null;
  isDeleted: boolean;
  deletedAtUtc?: string | null;
  deletedBySub?: string | null;
};

type ApiError = {
  status: number;
  title?: string;
  detail?: string;
  raw?: string;
};

async function fetchJson<T>(input: RequestInfo, init?: RequestInit): Promise<T> {
  const res = await fetch(input, { cache: "no-store", ...init });

  const contentType = res.headers.get("content-type") ?? "";
  const text = await res.text();

  // success
  if (res.ok) {
    if (!text) return undefined as unknown as T;
    return contentType.includes("application/json")
      ? (JSON.parse(text) as T)
      : ((text as unknown) as T);
  }

  // error
  let problem: any = null;
  if (contentType.includes("application/json") && text) {
    try { problem = JSON.parse(text); } catch {}
  }

  const err: ApiError = {
    status: res.status,
    title: problem?.title,
    detail: problem?.detail,
    raw: text,
  };

  // throw a real Error but attach structured info
  const e = new Error(
    problem?.detail ||
      problem?.title ||
      `Request failed (${res.status})`
  ) as Error & { api?: ApiError };
  e.api = err;
  throw e;
}

export default function Home() {
  const [health, setHealth] = useState<Health | null>(null);
  const [stores, setStores] = useState<Store[]>([]);
  const [name, setName] = useState("StoreFromUI");
  const [busy, setBusy] = useState(false);
  const [auth, setAuth] = useState<{ loggedIn: boolean; name?: string; email?: string; sub?: string } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  
  async function load() {
    setBusy(true);
    setError(null);

    try {
      // If you still want the same “see what came back” debugging,
      // this reproduces it while still using your helper’s parsing/errors:
      const healthPromise = fetch("/api/health", { cache: "no-store" });
      const storesPromise = fetch("/api/stores", { cache: "no-store" });

      const [healthRes, storesRes] = await Promise.all([healthPromise, storesPromise]);

      const healthText = await healthRes.text();
      const storesText = await storesRes.text();

      console.log("HEALTH status:", healthRes.status, "body:", healthText);
      console.log("STORES status:", storesRes.status, "body:", storesText);

      // Now enforce the SAME error semantics as fetchJson (ProblemDetails-aware)
      // but using your already-read bodies.
      if (!healthRes.ok) {
        throw Object.assign(new Error(`health failed (${healthRes.status})`), {
          api: {
            status: healthRes.status,
            title: tryGetProblemTitle(healthRes.headers.get("content-type") ?? "", healthText),
            detail: tryGetProblemDetail(healthRes.headers.get("content-type") ?? "", healthText),
            raw: healthText,
          } satisfies ApiError,
        });
      }

      if (!storesRes.ok) {
        throw Object.assign(new Error(`stores failed (${storesRes.status})`), {
          api: {
            status: storesRes.status,
            title: tryGetProblemTitle(storesRes.headers.get("content-type") ?? "", storesText),
            detail: tryGetProblemDetail(storesRes.headers.get("content-type") ?? "", storesText),
            raw: storesText,
          } satisfies ApiError,
        });
      }

      // success: parse like you did before
      setHealth(JSON.parse(healthText));
      setStores(JSON.parse(storesText));
    } catch (e: any) {
      console.error("LOAD FAILED:", e?.message ?? e);

      const api: ApiError =
        e?.api ?? { status: 0, title: "Unknown error", detail: e?.message };

      setError(api);

      // keep UI sane
      setHealth({ status: "error", utc: new Date().toISOString() } as any);
      setStores([]);
    } finally {
      setBusy(false);
    }
  }

// tiny helpers to parse ProblemDetails when present
  function tryGetProblemTitle(contentType: string, text: string): string | undefined {
    if (contentType.includes("application/json") && text) {
      try { return JSON.parse(text)?.title; } catch {}
    }
    return undefined;
  }

  function tryGetProblemDetail(contentType: string, text: string): string | undefined {
    if (contentType.includes("application/json") && text) {
      try { return JSON.parse(text)?.detail; } catch {}
    }
    return undefined;
  }

  useEffect(() => {
    load();
  }, []);

  async function createStore() {
    setBusy(true);
    setError(null);

    try {
      await fetchJson(`/api/stores`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name }),
      });

      await load();
      setName("StoreFromUI");
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  async function renameStore(id: number, newName: string) {
    setBusy(true);
    setError(null);

    try {
      await fetchJson(`/api/stores/${id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: newName }),
      });

      await load();
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  async function deleteStore(id: number) {
    setBusy(true);
    setError(null);

    try {
      await fetchJson(`/api/stores/${id}`, { method: "DELETE" });
      await load();
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  useEffect(() => {
    fetch("/api/profile")
      .then(r => r.json())
      .then(setAuth)
      .catch(() => setAuth({ loggedIn: false }));
  }, []);

  return (
    <main style={{ padding: 24, fontFamily: "system-ui" }}>
      
      <section style={{ marginBottom: 16 }}>
        <h2>Auth</h2>

        {auth?.loggedIn ? (
          <div>
            <div>Logged in ✅</div>
            <div><b>Name:</b> {auth.name ?? "(no name)"}</div>
            <div><b>Email:</b> {auth.email ?? "(no email)"}</div>
            <div style={{ fontSize: 12, opacity: 0.8 }}><b>sub:</b> {auth.sub}</div>
            <div style={{ marginTop: 8, display: "flex", gap: 8 }}>
              <a href="/auth/logout">Logout</a>
            </div>
          </div>
        ) : (
          <div style={{ display: "flex", gap: 8 }}>
            <span>Logged out ❌</span>
            <a href="/auth/login">Login</a>
          </div>
        )}
      </section>

      {error && (
        <div style={{
          border: "1px solid #ff6b6b",
          background: "rgba(255, 107, 107, 0.12)",
          padding: 12,
          borderRadius: 8,
          marginBottom: 16
        }}>
          <div style={{ fontWeight: 700 }}>
            Error {error.status || ""}: {error.title ?? "Request failed"}
          </div>

          <div style={{ marginTop: 6, whiteSpace: "pre-wrap" }}>
            {error.detail ?? error.raw ?? "No details"}
          </div>

          {error.status === 401 && (
            <div style={{ marginTop: 8 }}>
              You’re not logged in. <a href="/auth/login">Login</a>
            </div>
          )}

          {error.status === 403 && (
            <div style={{ marginTop: 8 }}>
              You’re logged in, but missing permission for this action.
            </div>
          )}
        </div>
      )}

      <h1>WooPulse</h1>

      <h2>API health</h2>
      <pre>{health ? JSON.stringify(health, null, 2) : "Loading..."}</pre>

      <h2>Stores</h2>
      <div style={{ display: "flex", gap: 8, marginBottom: 12 }}>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Store name"
          style={{ padding: 8, width: 260 }}
        />
        <button onClick={createStore} disabled={busy || !name.trim()} style={{ padding: "8px 12px" }}>
          {busy ? "Creating..." : "Create store"}
        </button>
        <button onClick={load} disabled={busy} style={{ padding: "8px 12px" }}>
          Refresh
        </button>
      </div>

      {stores.map((s) => (
        <div key={s.id} style={{ border: "1px solid #333", padding: 12, marginBottom: 8, borderRadius: 8 }}>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}>
            <div>
              <div><b>{s.name}</b> <span style={{ opacity: 0.7 }}>#{s.id}</span></div>
              <div style={{ fontSize: 12, opacity: 0.8 }}>
                Created: {s.createdAtUtc} by {s.createdBySub}
              </div>
              <div style={{ fontSize: 12, opacity: 0.8 }}>
                Updated: {s.updatedAtUtc ?? "(never)"} {s.updatedBySub ? `by ${s.updatedBySub}` : ""}
              </div>
            </div>

            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              <button
                disabled={busy}
                onClick={() => {
                  const newName = prompt("New store name:", s.name);
                  if (newName && newName.trim()) renameStore(s.id, newName.trim());
                }}
              >
                Rename
              </button>

              <button
                disabled={busy}
                onClick={() => {
                  if (confirm(`Soft delete store "${s.name}"?`)) deleteStore(s.id);
                }}
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      ))}
    </main>
  );
}
