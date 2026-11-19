"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import { Button, ErrorText, formatDate, inputClass, muted, SectionTitle } from "@/components/ui";

// Dtos/InvitationDtos.cs. `token` is only present from the dev-only debug endpoint.
type MyInvite = {
  id: number;
  storeId: number;
  storeName: string;
  role: string;
  expiresAtUtc: string | null;
  token?: string;
};

type Props = {
  refreshKey: number;
  /** After accepting, the page reloads its stores so the new one appears. */
  onStoresChanged: () => void;
};

/**
 * What: invitations sent TO ME (matched by the email in my login), with Accept buttons.
 * Rendered as a SECTION inside the "My stores" card (StoreList), not as its own card (DEVLOG #017).
 *
 * Which endpoint: in development, /api/invites/mine/debug returns the token, which is what
 * makes one-click "Accept" possible locally (no email is sent; see DEVLOG #002). Outside
 * development that route answers 404, so we fall back to /api/invites/mine (no tokens): the
 * list still shows, and the token can be pasted by hand.
 */
export default function MyInvitesPanel({ refreshKey, onStoresChanged }: Props) {
  const [invites, setInvites] = useState<MyInvite[]>([]);
  const [token, setToken] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const load = useCallback(async () => {
    try {
      try {
        setInvites(await fetchJson<MyInvite[]>("/api/invites/mine/debug"));
      } catch (e) {
        // Only fall back on 404 (debug route disabled). Any other error is real.
        if (toApiError(e).status !== 404) throw e;
        setInvites(await fetchJson<MyInvite[]>("/api/invites/mine"));
      }
      setError(null);
    } catch (e) {
      setError(toApiError(e));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  async function accept(inviteToken: string) {
    setBusy(true);
    setError(null);
    try {
      await fetchJson("/api/invites/accept", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token: inviteToken }),
      });
      setToken("");
      await load();
      onStoresChanged(); // the accepted store now appears in my lists
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between gap-2">
        {/* INCOMING invites: sent TO me by other stores (DEVLOG #018) */}
        <SectionTitle>Invites for you</SectionTitle>
        <Button onClick={() => void load()} disabled={busy}>Refresh invites</Button>
      </div>
      <p className={`-mt-1 mb-2 text-xs ${muted}`}>
        Stores that invited you to join their team. Accept to see them under &quot;Member of&quot;.
      </p>
      <ErrorText error={error} />

      {invites.length === 0 ? (
        <p className={`text-sm ${muted}`}>Nobody has invited you to a store right now.</p>
      ) : (
        <ul className="divide-y divide-[var(--viz-grid)]">
          {invites.map((i) => (
            <li key={i.id} className="flex flex-wrap items-center gap-2 py-2">
              <span className="min-w-0 flex-1 text-sm">
                Join <b>{i.storeName}</b> as {i.role}
                <span className={`ml-2 text-xs ${muted}`}>expires {formatDate(i.expiresAtUtc) || "never"}</span>
              </span>
              {i.token && (
                <Button variant="primary" disabled={busy} onClick={() => accept(i.token!)}>Accept</Button>
              )}
            </li>
          ))}
        </ul>
      )}

      {/* Manual fallback: paste a token you received some other way */}
      <form
        className="mt-3 flex flex-wrap gap-2 border-t border-[var(--viz-grid)] pt-3"
        onSubmit={(e) => {
          e.preventDefault();
          void accept(token.trim());
        }}
      >
        <input
          aria-label="Invite token"
          className={`${inputClass} min-w-0 flex-1`}
          value={token}
          onChange={(e) => setToken(e.target.value)}
          placeholder="Have an invite token? Paste it here"
          disabled={busy}
        />
        <Button type="submit" disabled={busy || !token.trim()}>Accept token</Button>
      </form>
    </div>
  );
}
