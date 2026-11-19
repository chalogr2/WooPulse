import type { AuthState } from "@/lib/types";
import { buttonClass, Card, muted } from "@/components/ui";

/**
 * What: who is logged in (top-left). Logged out → a Login button.
 * /auth/login and /auth/logout are handled by the Auth0 SDK (middleware.ts), so they are
 * plain <a> links (full page navigations), not fetch calls.
 * No "use client" needed: no state or effects here. It still runs in the browser because
 * its parent (page.tsx) is a client component.
 */
export default function UserCard({ auth }: { auth: AuthState | null }) {
  if (auth === null) {
    return <Card><div className={`text-sm ${muted}`}>Checking login…</div></Card>;
  }

  if (!auth.loggedIn) {
    return (
      <Card title="Welcome to WooPulse">
        <p className={`mb-3 text-sm ${muted}`}>Log in to see your stores and their analytics.</p>
        <a href="/auth/login" className={buttonClass("primary")}>Log in</a>
      </Card>
    );
  }

  // First letter of the name for a simple avatar circle.
  const initial = (auth.name ?? auth.email ?? "?").trim().charAt(0).toUpperCase();

  // Full width, ONE row (DEVLOG #016): avatar · name · email · sub, Log out pushed to the right.
  // flex-wrap: on a narrow phone screen the items wrap onto a second line instead of overflowing.
  return (
    <Card>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div
          aria-hidden="true"
          className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-[var(--viz-series-1)] font-semibold text-white"
        >
          {initial}
        </div>
        {/* min-w-0 lets long emails/ids shrink and wrap instead of overflowing the card */}
        <div className="flex min-w-0 flex-1 flex-wrap items-baseline gap-x-4 gap-y-1">
          <span className="font-semibold">{auth.name ?? "(no name)"}</span>
          <span className={`text-sm ${muted} break-all`}>{auth.email ?? "(no email)"}</span>
          <span className={`text-xs ${muted} break-all`} title="Your Auth0 user id">{auth.sub}</span>
        </div>
        <a href="/auth/logout" className={buttonClass("secondary")}>Log out</a>
      </div>
    </Card>
  );
}
