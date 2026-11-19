"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";

type DeletedStore = {
  id: number;
  name: string;
  ownerSub: string;
  deletedAtUtc: string | null;
};

type Props = {
  /** My Auth0 user id. Restore is Owner-only, so the button shows only on my own stores. */
  mySub?: string;
  refreshKey: number;
  /** Called after a restore, so the page's store list shows the store again. */
  onStoresChanged: () => void;
};

/** Days left before the daily purge job hard-deletes it (MaintenanceService, 30 days). */
function daysLeft(deletedAtUtc: string | null): number | null {
  if (!deletedAtUtc) return null;
  const purgeAt = new Date(deletedAtUtc).getTime() + 30 * 24 * 60 * 60 * 1000;
  return Math.max(0, Math.ceil((purgeAt - Date.now()) / (24 * 60 * 60 * 1000)));
}

/**
 * What: "Trash": stores I'm a member of that were soft-deleted, with a Restore button.
 */
export default function TrashPanel({ mySub, refreshKey, onStoresChanged }: Props) {
  const [stores, setStores] = useState<DeletedStore[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const load = useCallback(async () => {
    try {
      setStores(await fetchJson<DeletedStore[]>("/api/stores/deleted"));
      setError(null);
    } catch (e) {
      setError(toApiError(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  async function restore(id: number) {
    setBusy(true);
    setError(null);
    try {
      await fetchJson(`/api/stores/${id}/restore`, { method: "POST" });
      await load();
      onStoresChanged();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  // Nothing in the trash and no error → render nothing ("null" = no output).
  if (stores.length === 0 && !error) return null;

  return (
    <div style={{ border: "1px solid #333", padding: 12, borderRadius: 8, marginTop: 16 }}>
      <div style={{ fontWeight: 700, marginBottom: 8 }}>Trash (deleted stores)</div>

      {error && (
        <div style={{ color: "#ff6b6b", marginBottom: 8 }}>
          {error.title ?? "Error"}: {error.detail ?? error.raw ?? `status ${error.status}`}
        </div>
      )}

      {stores.map((s) => (
        <div key={s.id} style={{ display: "flex", gap: 8, alignItems: "center", padding: "4px 0" }}>
          <span style={{ minWidth: 260 }}>
            <b>{s.name}</b> <span style={{ opacity: 0.7 }}>#{s.id}</span>
          </span>
          <span style={{ fontSize: 12, opacity: 0.7 }}>
            permanently deleted in {daysLeft(s.deletedAtUtc) ?? "?"} days
          </span>
          {s.ownerSub === mySub && (
            <button disabled={busy} onClick={() => restore(s.id)}>Restore</button>
          )}
        </div>
      ))}
    </div>
  );
}
