"use client";
// "use client": this component uses state and click handlers, so it runs in the browser.

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";

// Shapes returned by the API (see Dtos/MemberDtos.cs and Dtos/InvitationDtos.cs)
type Member = {
  id: number;
  userSub: string;
  email: string | null;
  role: "Owner" | "Admin" | "Member";
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
  /** Changes whenever the page reloads its data, telling this panel to reload too. */
  refreshKey: number;
  /** Called after "Leave store", so the page refreshes its store list. */
  onStoresChanged: () => void;
};

/**
 * What: the team of ONE store: members (change role / remove / leave) + pending invites (revoke).
 * Self-contained: it fetches its own data, so page.tsx only renders <TeamPanel storeId={...} />.
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

  // Load on mount, when the active store changes, or when the page signals a refresh.
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
    <div style={{ border: "1px solid #333", padding: 12, borderRadius: 8, marginBottom: 12 }}>
      <div style={{ fontWeight: 700, marginBottom: 8 }}>Team of active store</div>

      {error && (
        <div style={{ color: "#ff6b6b", marginBottom: 8 }}>
          {error.title ?? "Error"}: {error.detail ?? error.raw ?? `status ${error.status}`}
        </div>
      )}

      {members.map((m) => (
        <div key={m.id} style={{ display: "flex", gap: 8, alignItems: "center", padding: "4px 0", flexWrap: "wrap" }}>
          <span style={{ minWidth: 260 }}>
            {m.email ?? <span style={{ opacity: 0.6 }}>{m.userSub}</span>}
            {m.isMe && <b> (you)</b>}
          </span>

          {/* Only the Owner can change roles, and never the Owner's own role */}
          {myRole === "Owner" && m.role !== "Owner" ? (
            <select
              value={m.role === "Admin" ? 2 : 3}
              disabled={busy}
              onChange={(e) => changeRole(m, Number(e.target.value) as 2 | 3)}
            >
              <option value={2}>Admin</option>
              <option value={3}>Member</option>
            </select>
          ) : (
            <span style={{ opacity: 0.8 }}>{m.role}</span>
          )}

          {canRemove(m) && <button disabled={busy} onClick={() => remove(m)}>Remove</button>}
          {m.isMe && m.role !== "Owner" && <button disabled={busy} onClick={leave}>Leave store</button>}
        </div>
      ))}

      {canManage && (
        <div style={{ marginTop: 12 }}>
          <div style={{ fontWeight: 600, marginBottom: 4 }}>Pending invites</div>
          {invites.length === 0 ? (
            <div style={{ opacity: 0.75 }}>None.</div>
          ) : (
            invites.map((inv) => (
              <div key={inv.id} style={{ display: "flex", gap: 8, alignItems: "center", padding: "4px 0" }}>
                <span style={{ minWidth: 260 }}>{inv.email}</span>
                <span style={{ opacity: 0.8 }}>{inv.role}</span>
                <span style={{ fontSize: 12, opacity: 0.7 }}>expires {inv.expiresAtUtc ?? "never"}</span>
                <button disabled={busy} onClick={() => revoke(inv)}>Revoke</button>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  );
}
