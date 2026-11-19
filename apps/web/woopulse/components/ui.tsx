// Small shared UI building blocks (DEVLOG #015). Every panel uses these, so the whole app
// looks consistent and a style change happens in ONE place.
// Colors come from the CSS tokens in app/globals.css (light + dark), same as the analytics block.

import type { ButtonHTMLAttributes, ReactNode } from "react";
import type { ApiError } from "@/lib/client-api";
import type { Role } from "@/lib/types";

/** Secondary text color class, used for labels and hints. */
export const muted = "text-[var(--viz-text-secondary)]";

/** Text inputs and selects. */
export const inputClass =
  "rounded-lg border border-[var(--viz-border)] bg-[var(--background)] px-3 py-1.5 text-sm " +
  "outline-none focus:border-[var(--viz-series-1)] disabled:opacity-50";

/** A titled box. `actions` renders on the right of the title (e.g. buttons). */
export function Card(props: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  const { title, actions, children, className = "" } = props;
  return (
    <section
      className={`rounded-2xl border border-[var(--viz-border)] bg-[var(--viz-surface)] p-4 text-[var(--viz-text-primary)] ${className}`}
    >
      {(title || actions) && (
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          {title && <h2 className="text-base font-semibold">{title}</h2>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

/** A sub-heading inside a Card (e.g. "Team" inside "Stores"). */
export function SectionTitle({ children }: { children: ReactNode }) {
  return <h3 className="mb-2 text-sm font-semibold">{children}</h3>;
}

type Variant = "primary" | "secondary" | "danger";

const variants: Record<Variant, string> = {
  primary: "bg-[var(--viz-series-1)] text-white hover:opacity-90",
  secondary: "border border-[var(--viz-border)] hover:bg-[var(--viz-grid)]",
  danger:
    "border border-[var(--viz-critical)] text-[var(--viz-critical)] hover:bg-[var(--viz-critical)] hover:text-white",
};

/** Button classes as a string, so <a> links can look like buttons too (e.g. Logout). */
export function buttonClass(variant: Variant = "secondary", extra = "") {
  return (
    "inline-flex items-center justify-center gap-1 rounded-lg px-3 py-1.5 text-sm font-medium transition " +
    `disabled:cursor-not-allowed disabled:opacity-50 ${variants[variant]} ${extra}`
  );
}

/**
 * A <button> with our styles.
 * ButtonHTMLAttributes<HTMLButtonElement> = every normal <button> prop (onClick, disabled, type...).
 * "...rest" collects them and passes them all through, so <Button> works like a <button>.
 */
export function Button({
  variant = "secondary",
  className = "",
  ...rest
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant }) {
  return <button className={buttonClass(variant, className)} {...rest} />;
}

/** Error box. role="alert" makes screen readers announce it. Renders nothing when no error. */
export function ErrorText({ error }: { error: ApiError | null }) {
  if (!error) return null;
  return (
    <div role="alert" className="mb-3 rounded-lg border border-[var(--viz-critical)] px-3 py-2 text-sm">
      <b>{error.title ?? "Error"}</b>
      {(error.detail ?? error.raw) && <span>: {error.detail ?? error.raw}</span>}
      {error.status === 401 && (
        <span>
          {" "}
          <a className="underline" href="/auth/login">Log in</a>
        </span>
      )}
    </div>
  );
}

/** Small pill showing a role. Text + shape carry the meaning (not color alone). */
export function RoleBadge({ role }: { role: Role }) {
  const style =
    role === "Owner"
      ? "border-[var(--viz-series-1)] text-[var(--viz-series-1)]"
      : "border-[var(--viz-border)] " + muted;
  return <span className={`rounded-full border px-2 py-0.5 text-xs font-medium ${style}`}>{role}</span>;
}

/** Dates from the API are ISO strings; show them in the viewer's local format. */
export const formatDateTime = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleString() : "");
export const formatDate = (iso: string | null | undefined) => (iso ? new Date(iso).toLocaleDateString() : "");
