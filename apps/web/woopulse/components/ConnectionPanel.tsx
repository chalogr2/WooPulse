"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";

// Matches Dtos/ConnectionDtos.cs → StoreConnectionDto
type Connection = {
  shopUrl: string;
  keyHint: string;
  authMode: "Header" | "QueryString";
  createdAtUtc: string;
  lastTestedAtUtc: string | null;
  lastTestOk: boolean | null;
  lastTestMessage: string | null;
};

type Role = "Owner" | "Admin" | "Member";

type Props = {
  storeId: number;
  myRole: Role;
  refreshKey: number;
};

/**
 * What: the active store's WooCommerce connection: status, and (for the Owner) the form to
 *       connect / change keys / disconnect.
 * Security notes:
 *  - The secret field is type="password" and is cleared from state right after saving.
 *  - The API never returns keys, only a hint like "ck_…a1b2".
 */
export default function ConnectionPanel({ storeId, myRole, refreshKey }: Props) {
  // undefined = still loading, null = not connected, object = connected
  const [conn, setConn] = useState<Connection | null | undefined>(undefined);
  const [editing, setEditing] = useState(false);
  const [shopUrl, setShopUrl] = useState("");
  const [consumerKey, setConsumerKey] = useState("");
  const [consumerSecret, setConsumerSecret] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const isOwner = myRole === "Owner";
  const canTest = myRole === "Owner" || myRole === "Admin";

  const load = useCallback(async () => {
    try {
      // 204 No Content → fetchJson returns undefined → "?? null" turns that into null.
      const c = await fetchJson<Connection | undefined>(`/api/stores/${storeId}/connection`);
      setConn(c ?? null);
      setError(null);
    } catch (e) {
      setError(toApiError(e));
    }
  }, [storeId]);

  useEffect(() => {
    setEditing(false);
    void load();
  }, [load, refreshKey]);

  async function run(action: () => Promise<Connection | undefined>) {
    setBusy(true);
    setError(null);
    try {
      const result = await action();
      setConn(result ?? null);
      setEditing(false);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const save = () =>
    run(async () => {
      const result = await fetchJson<Connection>(`/api/stores/${storeId}/connection`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ shopUrl, consumerKey, consumerSecret }),
      });
      // Don't keep secrets in browser memory longer than needed.
      setConsumerKey("");
      setConsumerSecret("");
      return result;
    });

  const test = () =>
    run(() => fetchJson<Connection>(`/api/stores/${storeId}/connection/test`, { method: "POST" }));

  const disconnect = () => {
    if (!confirm("Disconnect this store from WooCommerce? The saved API key will be deleted.")) return;
    void run(async () => {
      await fetchJson(`/api/stores/${storeId}/connection`, { method: "DELETE" });
      return undefined; // → conn becomes null (not connected)
    });
  };

  const startEditing = () => {
    setShopUrl(conn?.shopUrl ?? "");
    setEditing(true);
  };

  const box = { border: "1px solid #333", padding: 12, borderRadius: 8, marginBottom: 12 } as const;
  //  ^ "as const" tells TypeScript these values are fixed literals (fine for a style object).

  if (conn === undefined && !error) return <div style={box}>Loading WooCommerce connection…</div>;

  return (
    <div style={box}>
      <div style={{ fontWeight: 700, marginBottom: 8 }}>WooCommerce connection</div>

      {error && (
        <div style={{ color: "#ff6b6b", marginBottom: 8, whiteSpace: "pre-wrap" }}>
          {error.title ?? "Error"}: {error.detail ?? error.raw ?? `status ${error.status}`}
        </div>
      )}

      {/* Connected: status */}
      {conn && !editing && (
        <div>
          <div>
            {conn.lastTestOk ? "🟢" : "🔴"} <b>{conn.shopUrl}</b>{" "}
            <span style={{ opacity: 0.7 }}>key {conn.keyHint}</span>
          </div>
          <div style={{ fontSize: 12, opacity: 0.8, marginTop: 4 }}>
            {conn.lastTestMessage}
            {conn.lastTestedAtUtc && ` (checked ${new Date(conn.lastTestedAtUtc).toLocaleString()})`}
          </div>
          <div style={{ display: "flex", gap: 8, marginTop: 8 }}>
            {canTest && <button disabled={busy} onClick={test}>{busy ? "Testing…" : "Test connection"}</button>}
            {isOwner && <button disabled={busy} onClick={startEditing}>Change keys</button>}
            {isOwner && <button disabled={busy} onClick={disconnect}>Disconnect</button>}
          </div>
        </div>
      )}

      {/* Not connected, and not the owner */}
      {conn === null && !isOwner && (
        <div style={{ opacity: 0.8 }}>Not connected yet. Ask the store owner to connect WooCommerce.</div>
      )}

      {/* Owner: connect form (first time, or "Change keys") */}
      {isOwner && (conn === null || editing) && (
        <div style={{ display: "flex", flexDirection: "column", gap: 8, maxWidth: 460 }}>
          <div style={{ fontSize: 12, opacity: 0.8 }}>
            In WordPress: <b>WooCommerce → Settings → Advanced → REST API → Add key</b>, with
            Permissions set to <b>Read</b>. Paste the key and secret here. They are encrypted before being saved.
          </div>
          <input
            value={shopUrl}
            onChange={(e) => setShopUrl(e.target.value)}
            placeholder="https://yourshop.com"
            style={{ padding: 8 }}
            disabled={busy}
          />
          <input
            value={consumerKey}
            onChange={(e) => setConsumerKey(e.target.value)}
            placeholder="Consumer key (ck_...)"
            style={{ padding: 8 }}
            autoComplete="off"
            disabled={busy}
          />
          <input
            type="password"
            value={consumerSecret}
            onChange={(e) => setConsumerSecret(e.target.value)}
            placeholder="Consumer secret (cs_...)"
            style={{ padding: 8 }}
            autoComplete="new-password"
            disabled={busy}
          />
          <div style={{ display: "flex", gap: 8 }}>
            <button
              disabled={busy || !shopUrl.trim() || !consumerKey.trim() || !consumerSecret.trim()}
              onClick={save}
            >
              {busy ? "Testing & saving…" : "Save & test"}
            </button>
            {editing && <button disabled={busy} onClick={() => setEditing(false)}>Cancel</button>}
          </div>
        </div>
      )}
    </div>
  );
}
