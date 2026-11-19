"use client";

import { useEffect, useState } from "react";
import { fetchJson, type ApiError } from "@/lib/client-api";
import TeamPanel from "@/components/TeamPanel";
import TrashPanel from "@/components/TrashPanel";

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

type InvitationDebug = {
  id: number;
  storeId: number;
  storeName: string;
  email: string;
  role: string;   // API sends enum names: "Admin" | "Member"
  status: string; // "Pending" | "Accepted" | ...
  token: string;
  createdAtUtc: string;
  expiresAtUtc?: string | null;
};

// fetchJson + ApiError now live in lib/client-api.ts (shared with components/).

export default function Home() {
  const [health, setHealth] = useState<Health | null>(null);
  const [stores, setStores] = useState<Store[]>([]);
  const [name, setName] = useState("StoreFromUI");
  const [busy, setBusy] = useState(false);
  const [auth, setAuth] = useState<{ loggedIn: boolean; name?: string; email?: string; sub?: string } | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [activeStoreId, setActiveStoreId] = useState<number | null>(null);
  const [invites, setInvites] = useState<InvitationDebug[]>([]);
  // Bumped after every successful load(); the panels watch it and reload themselves.
  const [dataVersion, setDataVersion] = useState(0);

  type Invitation = {
  id: number;
  storeId: number;
  storeName: string;
  email: string;
  role: string;   // API sends enum names: "Admin" | "Member"
  status: string; // "Pending" | "Accepted" | ...
  createdAtUtc: string;
  expiresAtUtc?: string | null;
  token?: string; // optional (only if your API returns it)
  };

  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<2 | 3>(3);
  const [inviteResult, setInviteResult] = useState<any | null>(null);
  const [myInvites, setMyInvites] = useState<Invitation[]>([]);
  const [acceptToken, setAcceptToken] = useState("");
  
  async function load() {
    setBusy(true);
    setError(null);

    try {
      // Use fetchJson so error handling is consistent across the whole app
      const [h, storesData] = await Promise.all([
        fetchJson<Health>("/api/health"),
        fetchJson<Store[]>("/api/stores"),
      ]);

      setHealth(h);
      setStores(storesData);

      // Keep the active store if it's still in the list; otherwise (first load, or I just
      // left/deleted it) pick the first store, or null if I have none.
      // Functional form setX(current => next) reads the LATEST state, not the value
      // captured when this function was created.
      setActiveStoreId((current) =>
        storesData.some((s) => s.id === current) ? current : storesData[0]?.id ?? null
      );
      setDataVersion((v) => v + 1);
    } catch (e: any) {
      console.error("LOAD FAILED:", e?.message ?? e);

      const api: ApiError =
        e?.api ?? { status: 0, title: "Unknown error", detail: e?.message };

      setError(api);

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

  async function inviteToActiveStore() {
    if (!activeStoreId) {
      setError({ status: 0, title: "No active store", detail: "Select an active store first." });
      return;
    }

    setBusy(true);
    setError(null);
    setInviteResult(null);

    try {
      const dto = await fetchJson(`/api/stores/${activeStoreId}/invites`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: inviteEmail, role: inviteRole }),
      });

      setInviteResult(dto);
      setInviteEmail("");
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  async function loadMyInvites() {
    setBusy(true);
    setError(null);

    try {
      const list = await fetchJson<Invitation[]>("/api/invites/mine");
      setMyInvites(list);
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  async function loadMyInvitesDebug() {
    setBusy(true);
    setError(null);

    try {
      const list = await fetchJson<InvitationDebug[]>("/api/invites/mine/debug");
      setInvites(list);
    } catch (e: any) {
      setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
    } finally {
      setBusy(false);
    }
  }

  async function acceptInvite(token: string) {
    setBusy(true);
    setError(null);

    try {
      await fetchJson(`/api/invites/accept`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token }),
      });

      setAcceptToken("");
      await loadMyInvites(); // refresh invite list
      await load();          // refresh stores so invited store appears
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

      <div style={{ marginBottom: 12 }}>
        <label style={{ marginRight: 8 }}>Active store:</label>
        <select
          value={activeStoreId ?? ""}
          onChange={(e) => setActiveStoreId(Number(e.target.value))}
          disabled={!stores.length}
        >
          {stores.map((s) => (
            <option key={s.id} value={s.id}>
              {s.name} (#{s.id})
            </option>
          ))}
        </select>
      </div>

      {/* Invite to active store */}
      <div style={{ border: "1px solid #333", padding: 12, borderRadius: 8, marginBottom: 12 }}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>Invite member to active store</div>

        <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
          <input
            value={inviteEmail}
            onChange={(e) => setInviteEmail(e.target.value)}
            placeholder="email@example.com"
            style={{ padding: 8, width: 260 }}
            disabled={busy}
          />

          <select
            value={inviteRole}
            onChange={(e) => setInviteRole(Number(e.target.value) as 2 | 3)}
            disabled={busy}
          >
            <option value={3}>Member</option>
            <option value={2}>Admin</option>
          </select>

          <button
            onClick={inviteToActiveStore}
            disabled={busy || !activeStoreId || !inviteEmail.trim()}
            style={{ padding: "8px 12px" }}
          >
            Invite
          </button>
        </div>

        {inviteResult && (
          <pre style={{ marginTop: 10, fontSize: 12, opacity: 0.9 }}>
            {JSON.stringify(inviteResult, null, 2)}
          </pre>
        )}
      </div>

      {/* Team of the active store (components/TeamPanel.tsx). "cond && <X/>" renders X only if cond is truthy. */}
      {activeStoreId != null && (
        <TeamPanel storeId={activeStoreId} refreshKey={dataVersion} onStoresChanged={load} />
      )}

      {/* My invites */}
      <div style={{ border: "1px solid #333", padding: 12, borderRadius: 8, marginBottom: 12 }}>
        <div style={{ fontWeight: 700, marginBottom: 8 }}>My pending invites</div>

        <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap", marginBottom: 8 }}>
          <button
            onClick={async () => {
              setBusy(true);
              setError(null);
              try {
                const list = await fetchJson<any[]>("/api/invites/mine/debug");
                setMyInvites(list);
              } catch (e: any) {
                setError(e?.api ?? { status: 0, title: "Unknown error", detail: e?.message });
              } finally {
                setBusy(false);
              }
            }}
            disabled={busy}
            style={{ padding: "8px 12px" }}
          >
            Refresh invites (debug)
          </button>

          <input
            value={acceptToken}
            onChange={(e) => setAcceptToken(e.target.value)}
            placeholder="Paste invite token to accept"
            style={{ padding: 8, width: 320 }}
            disabled={busy}
          />

          <button
            onClick={() => acceptInvite(acceptToken.trim())}
            disabled={busy || !acceptToken.trim()}
            style={{ padding: "8px 12px" }}
          >
            Accept token
          </button>
        </div>

        {myInvites.length === 0 ? (
          <div style={{ opacity: 0.75 }}>No pending invites loaded (or none exist).</div>
        ) : (
          myInvites.map(i => (
            <div key={i.id} style={{ border: "1px solid #444", padding: 10, borderRadius: 8, marginBottom: 8 }}>
              <div><b>Invite #{i.id}</b> → {i.storeName} <span style={{ opacity: 0.7 }}>#{i.storeId}</span></div>
              <div style={{ fontSize: 12, opacity: 0.85 }}>
                Email: {i.email} | Role: {i.role} | Expires: {i.expiresAtUtc ?? "(none)"}
              </div>

              {/* if token comes back in dev, accept with one click */}
              {i.token && (
                <button
                  onClick={() => acceptInvite((i as any)["token"])}
                  disabled={busy}
                  style={{ marginTop: 8 }}
                >
                  Accept this invite
                </button>
              )}
            </div>
          ))
        )}
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

      {/* Soft-deleted stores with Restore (components/TrashPanel.tsx) */}
      <TrashPanel mySub={auth?.sub} refreshKey={dataVersion} onStoresChanged={load} />
    </main>
  );
}
