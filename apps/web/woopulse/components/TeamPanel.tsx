"use client";
// "use client": this component uses state and click handlers, so it runs in the browser.

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import type { Role } from "@/lib/types";
import { Button, ErrorText, inputClass, muted, RoleBadge, SectionTitle, formatDate } from "@/components/ui";

// Shapes returned by the API (see Dtos/MemberDtos.cs and Dtos/InvitationDtos.cs)
type Member = {
  id: number;
  userSub: string;
  email: string | null;
  role: Role;
  joinedAtUtc: string;
  isMe: boolean;
};

type PendingInvite = {
  id: number;
  email: string;
  role: string;
  expiresAtUtc: string | null;
};

type Props = {
  storeId: number;
  /** Changes whenever something elsewhere changed, telling this panel to reload. */
  refreshKey: number | string;
  /** Called after "Leave store", so the page refreshes its store list. */
  onStoresChanged: () => void;
};

/**
 * What: the team of ONE store: members (change role / remove / leave) + pending invites (revoke).
 * Rendered INSIDE the Stores card (components/StoresPanel.tsx), so it has no card of its own.
 * Buttons are only SHOWN when the rules allow them. The API enforces the same rules
 * (MemberService), so hiding a button is only for convenience, never for security.
 */
export default function TeamPanel({ storeId, refreshKey, onStoresChanged }: Props) {
  const [members, setMembers] = useState<Member[]>([]);
  const [invites, setInvites] = useState<PendingInvite[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  // My role in this store, found from the members list (isMe = true on my row).
  // "?." = optional chaining: undefined instead of a crash if no row matches.
  const myRole = members.find((m) => m.isMe)?.role;
  const canManage = myRole === "Owner" || myRole === "Admin";

  // useCallback keeps the same function object between renders unless storeId changes.
  // That lets the useEffect below list it as a dependency without looping forever.
  const load = useCallback(async () => {
    try {
      const list = await fetchJson<Member[]>(`/api/stores/${storeId}/members`);
      setMembers(list);

      const me = list.find((m) => m.isMe);
      // Members get 403 on the invites endpoint, so only Owners/Admins ask for it.
      setInvites(me && me.role !== "Member"
        ? await fetchJson<PendingInvite[]>(`/api/stores/${storeId}/invites`)
        : []);
      setError(null);
    } catch (e) {
      setError(toApiError(e));
    }
  }, [storeId]);

  // Load on mount, when the active store changes, or when the parent signals a refresh.
  useEffect(() => {
    void load(); // "void" = we intentionally don't await the promise here
  }, [load, refreshKey]);

  // One wrapper for every button: busy flag, error handling, reload afterwards.
  async function run(action: () => Promise<unknown>, afterwards?: () => void) {
    setBusy(true);
    setError(null);
    try {
      await action();
      await load();
      afterwards?.();
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  const changeRole = (m: Member, role: 2 | 3) =>
    run(() => fetchJson(`/api/stores/${storeId}/members/${m.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ role }),
    }));

  const remove = (m: Member) => {
    if (!confirm(`Remove ${m.email ?? m.userSub} from this store?`)) return;
    void run(() => fetchJson(`/api/stores/${storeId}/members/${m.id}`, { method: "DELETE" }));
  };

  const leave = () => {
    if (!confirm("Leave this store? You'll need a new invite to rejoin.")) return;
    void run(() => fetchJson(`/api/stores/${storeId}/leave`, { method: "POST" }), onStoresChanged);
  };

  const revoke = (inv: PendingInvite) =>
    run(() => fetchJson(`/api/stores/${storeId}/invites/${inv.id}/revoke`, { method: "POST" }));

  // Mirrors MemberService.RemoveAsync: Owner removes anyone but self; Admin removes Members.
  const canRemove = (m: Member) =>
    !m.isMe && (myRole === "Owner" || (myRole === "Admin" && m.role === "Member"));

  return (
    <div>
      <ErrorText error={error} />

      <SectionTitle>Team</SectionTitle>
      {/* divide-y: a hairline between rows (Tailwind) */}
      <ul className="divide-y divide-[var(--viz-grid)]">
        {members.map((m) => (
          <li key={m.id} className="flex flex-wrap items-center gap-2 py-2">
            <span className="min-w-0 flex-1 break-all text-sm">
              {m.email ?? <span className={muted}>{m.userSub}</span>}
              {m.isMe && <span className={muted}> (you)</span>}
            </span>

            {/* Only the Owner can change roles, and never the Owner's own role */}
            {myRole === "Owner" && m.role !== "Owner" ? (
              <select
                aria-label={`Role of ${m.email ?? m.userSub}`}
                className={inputClass}
                value={m.role === "Admin" ? 2 : 3}
                disabled={busy}
                onChange={(e) => changeRole(m, Number(e.target.value) as 2 | 3)}
              >
                <option value={2}>Admin</option>
                <option value={3}>Member</option>
              </select>
            ) : (
              <RoleBadge role={m.role} />
            )}

            {canRemove(m) && <Button variant="danger" disabled={busy} onClick={() => remove(m)}>Remove</Button>}
            {m.isMe && m.role !== "Owner" && <Button disabled={busy} onClick={leave}>Leave store</Button>}
          </li>
        ))}
      </ul>

      {canManage && (
        <div className="mt-4">
          {/* OUTGOING invites: sent FROM this store, not yet accepted (DEVLOG #018) */}
          <SectionTitle>Invites sent from this store</SectionTitle>
          <p className={`-mt-1 mb-2 text-xs ${muted}`}>
            People you invited who haven&apos;t accepted yet. Revoke to cancel an invite.
          </p>
          {invites.length === 0 ? (
            <div className={`text-sm ${muted}`}>No invites waiting for a reply.</div>
          ) : (
            <ul className="divide-y divide-[var(--viz-grid)]">
              {invites.map((inv) => (
                <li key={inv.id} className="flex flex-wrap items-center gap-2 py-2">
                  <span className="min-w-0 flex-1 break-all text-sm">{inv.email}</span>
                  <span className={`text-xs ${muted}`}>{inv.role} · expires {formatDate(inv.expiresAtUtc) || "never"}</span>
                  <Button disabled={busy} onClick={() => revoke(inv)}>Revoke</Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
