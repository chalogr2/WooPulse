"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import type { Role } from "@/lib/types";
import { Button, Card, ErrorText, formatDateTime, inputClass, muted } from "@/components/ui";

// Matches Dtos/ConnectionDtos.cs → StoreConnectionDto
type Connection = {
  shopUrl: string;
  keyHint: string;
  authMode: "Header" | "QueryString";
  createdAtUtc: string;
  lastTestedAtUtc: string | null;
  lastTestOk: boolean | null;
  lastTestMessage: string | null;
  // sync status (DEVLOG #012)
  syncInProgress: boolean;
  lastSyncStartedAtUtc: string | null;
  lastSyncCompletedAtUtc: string | null;
  lastSyncOk: boolean | null;
  lastSyncMessage: string | null;
  initialSyncDone: boolean;
  orderCount: number;
};

type Props = {
  storeId: number;
  myRole: Role;
  refreshKey: number;
  /** Called once when a sync finishes, so the page can refresh the analytics. */
  onSynced?: () => void;
};

/**
 * What: the active store's WooCommerce connection: status, and (for the Owner) the form to
 *       connect / change keys / disconnect.
 * Security notes:
 *  - The secret field is type="password" and is cleared from state right after saving.
 *  - The API never returns keys, only a hint like "ck_…a1b2".
 */
export default function ConnectionPanel({ storeId, myRole, refreshKey, onSynced }: Props) {
  // undefined = still loading, null = not connected, object = connected
  const [conn, setConn] = useState<Connection | null | undefined>(undefined);
  const [editing, setEditing] = useState(false);
  const [shopUrl, setShopUrl] = useState("");
  const [consumerKey, setConsumerKey] = useState("");
  const [consumerSecret, setConsumerSecret] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  // When we asked for a sync (ms timestamp). Used to keep polling until that sync finishes,
  // even in the second before the background job has actually started.
  const [syncRequestedAt, setSyncRequestedAt] = useState<number | null>(null);

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

  // Is a sync running or about to run? (derived from state; no extra useState needed)
  const waitingForRequestedSync =
    syncRequestedAt != null &&
    (!conn?.lastSyncCompletedAtUtc || Date.parse(conn.lastSyncCompletedAtUtc) < syncRequestedAt);
  const syncing = !!conn?.syncInProgress || waitingForRequestedSync;
  //              ^ "!!" turns any value into a plain true/false

  // POLLING: while syncing, reload the status every 3 seconds so the order count climbs live.
  // The cleanup function (returned) stops the timer when syncing ends or the panel unmounts;
  // without it, timers would pile up and keep running.
  useEffect(() => {
    if (!syncing) return;
    const timer = setInterval(() => void load(), 3000);
    return () => clearInterval(timer);
  }, [syncing, load]);

  // Detect the moment syncing goes true → false. useRef holds a value across renders
  // WITHOUT causing a re-render when it changes (unlike useState), which is ideal for
  // remembering "what was it last time?".
  const wasSyncing = useRef(false);
  useEffect(() => {
    if (wasSyncing.current && !syncing) onSynced?.();
    wasSyncing.current = syncing;
  }, [syncing, onSynced]);

  const syncNow = async () => {
    setError(null);
    try {
      await fetchJson(`/api/stores/${storeId}/sync`, { method: "POST" });
      setSyncRequestedAt(Date.now());
    } catch (e) {
      setError(toApiError(e));
    }
  };

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
      // Saving queues the first sync automatically (ConnectionService), so start watching it.
      setSyncRequestedAt(Date.now());
      return result;
    });

  const test = () =>
    run(() => fetchJson<Connection>(`/api/stores/${storeId}/connection/test`, { method: "POST" }));

  const disconnect = () => {
    if (!confirm("Disconnect this store from WooCommerce? The saved API key AND all synced orders will be deleted from WooPulse (your shop is not affected).")) return;
    void run(async () => {
      await fetchJson(`/api/stores/${storeId}/connection`, { method: "DELETE" });
      return undefined; // → conn becomes null (not connected)
    });
  };

  const startEditing = () => {
    setShopUrl(conn?.shopUrl ?? "");
    setEditing(true);
  };

  if (conn === undefined && !error) {
    return <Card title="WooCommerce connection"><div className={`text-sm ${muted}`}>Loading…</div></Card>;
  }

  return (
    <Card title="WooCommerce connection">
      <ErrorText error={error} />

      {/* Connected: status */}
      {conn && !editing && (
        <div className="grid gap-4 md:grid-cols-2">
          {/* Left: the connection itself */}
          <div>
            <div className="flex flex-wrap items-center gap-2">
              {/* Status = icon + text, never color alone */}
              <span aria-hidden="true">{conn.lastTestOk ? "🟢" : "🔴"}</span>
              <span className="font-semibold break-all">{conn.shopUrl}</span>
              <span className={`text-xs ${muted}`}>key {conn.keyHint}</span>
            </div>
            <p className={`mt-1 text-xs ${muted}`}>
              {conn.lastTestMessage}
              {conn.lastTestedAtUtc && ` (checked ${formatDateTime(conn.lastTestedAtUtc)})`}
            </p>
          </div>

          {/* Right: sync status */}
          <div>
            <div className="text-sm">
              <b>{conn.orderCount.toLocaleString()}</b> orders synced
              {syncing && (
                <span className={muted}>
                  {" "}· ⏳ syncing…{!conn.initialSyncDone && " (first full sync, this can take a few minutes)"}
                </span>
              )}
            </div>
            {!syncing && (
              <p className={`mt-1 text-xs ${muted}`}>
                {conn.lastSyncMessage
                  ? `${conn.lastSyncOk ? "✅" : "⚠️"} ${conn.lastSyncMessage}${conn.lastSyncCompletedAtUtc ? ` (${formatDateTime(conn.lastSyncCompletedAtUtc)})` : ""}`
                  : "Not synced yet."}
              </p>
            )}
          </div>

          <div className="flex flex-wrap gap-2 md:col-span-2">
            {/* Any member can sync (owner decision #011) */}
            <Button variant="primary" disabled={busy || syncing} onClick={syncNow}>
              {syncing ? "Syncing…" : "Sync now"}
            </Button>
            {canTest && <Button disabled={busy} onClick={test}>{busy ? "Testing…" : "Test connection"}</Button>}
            {isOwner && <Button disabled={busy} onClick={startEditing}>Change keys</Button>}
            {isOwner && <Button variant="danger" disabled={busy} onClick={disconnect}>Disconnect</Button>}
          </div>
        </div>
      )}

      {/* Not connected, and not the owner */}
      {conn === null && !isOwner && (
        <p className={`text-sm ${muted}`}>Not connected yet. Ask the store owner to connect WooCommerce.</p>
      )}

      {/* Owner: connect form (first time, or "Change keys") */}
      {isOwner && (conn === null || editing) && (
        <form
          className="flex max-w-xl flex-col gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            void save();
          }}
        >
          <p className={`text-xs ${muted}`}>
            In WordPress: <b>WooCommerce → Settings → Advanced → REST API → Add key</b>, with Permissions set
            to <b>Read</b>. Paste the key and secret here. They are encrypted before being saved.
          </p>
          <input
            aria-label="Shop URL"
            className={inputClass}
            value={shopUrl}
            onChange={(e) => setShopUrl(e.target.value)}
            placeholder="https://yourshop.com"
            disabled={busy}
          />
          <input
            aria-label="Consumer key"
            className={inputClass}
            value={consumerKey}
            onChange={(e) => setConsumerKey(e.target.value)}
            placeholder="Consumer key (ck_...)"
            autoComplete="off"
            disabled={busy}
          />
          <input
            aria-label="Consumer secret"
            type="password"
            className={inputClass}
            value={consumerSecret}
            onChange={(e) => setConsumerSecret(e.target.value)}
            placeholder="Consumer secret (cs_...)"
            autoComplete="new-password"
            disabled={busy}
          />
          <div className="flex gap-2">
            <Button
              type="submit"
              variant="primary"
              disabled={busy || !shopUrl.trim() || !consumerKey.trim() || !consumerSecret.trim()}
            >
              {busy ? "Testing & saving…" : "Save & test"}
            </Button>
            {editing && <Button type="button" disabled={busy} onClick={() => setEditing(false)}>Cancel</Button>}
          </div>
        </form>
      )}
    </Card>
  );
}
