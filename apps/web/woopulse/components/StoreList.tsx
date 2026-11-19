"use client";

import { useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import type { Store } from "@/lib/types";
import MyInvitesPanel from "@/components/MyInvitesPanel";
import { Button, Card, ErrorText, formatDate, inputClass, muted, RoleBadge, SectionTitle } from "@/components/ui";

type Props = {
  stores: Store[];
  activeStoreId: number | null;
  refreshKey: number;
  onSelect: (storeId: number) => void;
  onStoresChanged: () => void;
  /** A store was created: the page reloads and makes it the active store. */
  onCreated: (storeId: number) => void;
};

/**
 * What: "My stores": create a store, all my stores split into "Owned by me" / "Member of",
 *       and invites sent to me (DEVLOG #017).
 * Actions follow the API rules: rename = Owner/Admin, delete = Owner only (DEVLOG #008).
 * Rename is INLINE (an input in the row) instead of the browser's prompt() dialog.
 */
export default function StoreList({ stores, activeStoreId, refreshKey, onSelect, onStoresChanged, onCreated }: Props) {
  const [editingId, setEditingId] = useState<number | null>(null);
  const [draftName, setDraftName] = useState("");
  const [newName, setNewName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function createStore() {
    setBusy(true);
    setError(null);
    try {
      const created = await fetchJson<Store>("/api/stores", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: newName }),
      });
      setNewName("");
      onCreated(created.id);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const owned = stores.filter((s) => s.myRole === "Owner");
  const memberOf = stores.filter((s) => s.myRole !== "Owner");

  async function run(action: () => Promise<unknown>) {
    setBusy(true);
    setError(null);
    try {
      await action();
      setEditingId(null);
      onStoresChanged();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const rename = (s: Store) =>
    run(() => fetchJson(`/api/stores/${s.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: draftName }),
    }));

  const remove = (s: Store) => {
    if (!confirm(`Delete "${s.name}"? It goes to "Deleted stores" and can be restored for 30 days.`)) return;
    void run(() => fetchJson(`/api/stores/${s.id}`, { method: "DELETE" }));
  };

  // A small render function (not a separate component) because it only exists for this list.
  const renderRow = (s: Store) => {
    const isActive = s.id === activeStoreId;
    const canRename = s.myRole === "Owner" || s.myRole === "Admin";

    return (
      <li key={s.id} className="flex flex-wrap items-center gap-2 py-2">
        {editingId === s.id ? (
          <form
            className="flex min-w-0 flex-1 flex-wrap gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              void rename(s);
            }}
          >
            <input
              aria-label={`New name for ${s.name}`}
              className={`${inputClass} min-w-0 flex-1`}
              value={draftName}
              onChange={(e) => setDraftName(e.target.value)}
              maxLength={60}
              autoFocus
              disabled={busy}
            />
            <Button type="submit" variant="primary" disabled={busy || draftName.trim().length < 2}>Save</Button>
            <Button type="button" disabled={busy} onClick={() => setEditingId(null)}>Cancel</Button>
          </form>
        ) : (
          <>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-medium">{s.name}</span>
                <span className={`text-xs ${muted}`}>#{s.id}</span>
                <RoleBadge role={s.myRole} />
                {isActive && <span className="text-xs text-[var(--viz-series-1)]">● active</span>}
              </div>
              <div className={`text-xs ${muted}`}>
                Created {formatDate(s.createdAtUtc)}
                {s.updatedAtUtc && ` · updated ${formatDate(s.updatedAtUtc)}`}
              </div>
            </div>
            {!isActive && <Button disabled={busy} onClick={() => onSelect(s.id)}>Open</Button>}
            {canRename && (
              <Button
                disabled={busy}
                onClick={() => {
                  setDraftName(s.name);
                  setEditingId(s.id);
                }}
              >
                Rename
              </Button>
            )}
            {s.myRole === "Owner" && <Button variant="danger" disabled={busy} onClick={() => remove(s)}>Delete</Button>}
          </>
        )}
      </li>
    );
  };

  return (
    <Card title="My stores" actions={<Button onClick={onStoresChanged} disabled={busy}>Refresh</Button>}>
      <ErrorText error={error} />

      {/* Create a store. <form onSubmit> lets Enter submit; preventDefault stops a page reload. */}
      <form
        className="mb-5 flex flex-wrap gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void createStore();
        }}
      >
        <input
          aria-label="New store name"
          className={`${inputClass} min-w-0 flex-1`}
          value={newName}
          onChange={(e) => setNewName(e.target.value)}
          placeholder="New store name (2–60 characters)"
          maxLength={60}
          disabled={busy}
        />
        <Button type="submit" variant="primary" disabled={busy || newName.trim().length < 2}>
          {busy ? "Working…" : "Create store"}
        </Button>
      </form>

      {stores.length === 0 ? (
        <p className={`text-sm ${muted}`}>No stores yet.</p>
      ) : (
        <div className="grid gap-6 md:grid-cols-2">
          <div>
            <SectionTitle>Owned by me ({owned.length})</SectionTitle>
            {owned.length === 0
              ? <p className={`text-sm ${muted}`}>None.</p>
              : <ul className="divide-y divide-[var(--viz-grid)]">{owned.map(renderRow)}</ul>}
          </div>
          <div>
            <SectionTitle>Member of ({memberOf.length})</SectionTitle>
            {memberOf.length === 0
              ? <p className={`text-sm ${muted}`}>None.</p>
              : <ul className="divide-y divide-[var(--viz-grid)]">{memberOf.map(renderRow)}</ul>}
          </div>
        </div>
      )}

      {/* Invites sent to me: accepting one adds a store to "Member of" above */}
      <div className="mt-5 border-t border-[var(--viz-grid)] pt-4">
        <MyInvitesPanel refreshKey={refreshKey} onStoresChanged={onStoresChanged} />
      </div>
    </Card>
  );
}
