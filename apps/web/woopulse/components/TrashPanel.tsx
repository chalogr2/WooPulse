"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import { Button, Card, ErrorText, formatDate, muted } from "@/components/ui";

type DeletedStore = {
  id: number;
  name: string;
  ownerSub: string;
  deletedAtUtc: string | null;
};

type Props = {
  /** My Auth0 user id. Restore is Owner-only, so the button shows only on my own stores. */
  mySub?: string | null;
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

/** What: "Deleted stores" (soft-deleted, restorable for 30 days). */
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

  return (
    <Card title="Deleted stores">
      <ErrorText error={error} />
      {stores.length === 0 ? (
        <p className={`text-sm ${muted}`}>Nothing here. Deleted stores stay restorable for 30 days.</p>
      ) : (
        <ul className="divide-y divide-[var(--viz-grid)]">
          {stores.map((s) => (
            <li key={s.id} className="flex flex-wrap items-center gap-2 py-2">
              <div className="min-w-0 flex-1">
                <span className="font-medium">{s.name}</span> <span className={`text-xs ${muted}`}>#{s.id}</span>
                <div className={`text-xs ${muted}`}>
                  Deleted {formatDate(s.deletedAtUtc)} · permanently removed in {daysLeft(s.deletedAtUtc) ?? "?"} days
                </div>
              </div>
              {s.ownerSub === mySub && (
                <Button disabled={busy} onClick={() => restore(s.id)}>Restore</Button>
              )}
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
