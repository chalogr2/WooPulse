"use client";

import { useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import type { Store } from "@/lib/types";
import TeamPanel from "@/components/TeamPanel";
import { Button, Card, ErrorText, formatDate, inputClass, muted, SectionTitle } from "@/components/ui";

type Props = {
  /** The store selected in the "Active store" dropdown. */
  activeStore: Store;
  refreshKey: number;
  /** Reload the page's store list (after leaving the store, etc.). */
  onStoresChanged: () => void;
};

// What InviteAsync returns (Dtos/InvitationDtos.cs → InvitationDto)
type InviteResult = { email: string; role: string; storeName: string; expiresAtUtc: string | null };

/**
 * What: the "Current store: <name>" card: invite someone to the active store, and its team
 *       (members + the invites this store has sent).
 * Creating stores and my own incoming invites live in "My stores" (StoreList), DEVLOG #017.
 */
export default function StoresPanel({ activeStore, refreshKey, onStoresChanged }: Props) {
  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState<2 | 3>(3);
  const [inviting, setInviting] = useState(false);
  const [inviteResult, setInviteResult] = useState<InviteResult | null>(null);
  // Bumped after an invite, so TeamPanel reloads its "Pending invites" list.
  const [teamVersion, setTeamVersion] = useState(0);
  const [error, setError] = useState<ApiError | null>(null);

  const canInvite = activeStore.myRole === "Owner" || activeStore.myRole === "Admin";

  async function invite() {
    setInviting(true);
    setError(null);
    setInviteResult(null);
    try {
      const result = await fetchJson<InviteResult>(`/api/stores/${activeStore.id}/invites`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: inviteEmail, role: inviteRole }),
      });
      setInviteResult(result);
      setInviteEmail("");
      setTeamVersion((v) => v + 1);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setInviting(false);
    }
  }

  return (
    <Card
      title={
        <>
          Current store: <span className="text-[var(--viz-series-1)]">{activeStore.name}</span>
        </>
      }
    >
      <ErrorText error={error} />

      <SectionTitle>Invite someone</SectionTitle>
      {canInvite ? (
        <>
          {/* <form onSubmit> lets Enter submit. e.preventDefault() stops the browser's full-page reload. */}
          <form
            className="flex flex-wrap gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              void invite();
            }}
          >
            <input
              type="email"
              aria-label="Email to invite"
              className={`${inputClass} min-w-0 flex-1`}
              value={inviteEmail}
              onChange={(e) => setInviteEmail(e.target.value)}
              placeholder="email@example.com"
              disabled={inviting}
            />
            <select
              aria-label="Role"
              className={inputClass}
              value={inviteRole}
              onChange={(e) => setInviteRole(Number(e.target.value) as 2 | 3)}
              disabled={inviting}
            >
              <option value={3}>Member</option>
              <option value={2}>Admin</option>
            </select>
            <Button type="submit" variant="primary" disabled={inviting || !inviteEmail.trim()}>
              {inviting ? "Inviting…" : "Invite"}
            </Button>
          </form>
          {inviteResult && (
            <p className={`mt-2 text-sm ${muted}`}>
              ✅ Invited <b>{inviteResult.email}</b> as {inviteResult.role}
              {inviteResult.expiresAtUtc && ` (expires ${formatDate(inviteResult.expiresAtUtc)})`}. They&apos;ll
              see it under &quot;My stores → Invites for you&quot; when they log in.
            </p>
          )}
        </>
      ) : (
        <p className={`text-sm ${muted}`}>Only owners and admins can invite people.</p>
      )}

      <div className="mt-5 border-t border-[var(--viz-grid)] pt-4">
        {/* Template-string key: changes when either value changes, so TeamPanel reloads */}
        <TeamPanel storeId={activeStore.id} refreshKey={`${refreshKey}-${teamVersion}`} onStoresChanged={onStoresChanged} />
      </div>
    </Card>
  );
}
