"use client";

import { useCallback, useEffect, useState } from "react";
// Recharts: chart COMPONENTS. You describe the chart declaratively; it does scales, axes,
// SVG drawing, tooltips and resizing (pinned at 3.10.1 in package.json).
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";

// ---- Types: mirror Dtos/AnalyticsDtos.cs (JSON is camelCase) ----
type Kpi = {
  revenue: number;
  netRevenue: number;
  orders: number;
  averageOrderValue: number;
  itemsSold: number;
  cancelledOrders: number;
  cancelledValue: number;
};
type SeriesPoint = { period: string; revenue: number; orders: number };
type TopProduct = { productId: number; name: string; quantity: number; revenue: number };
type StatusCount = { status: string; orders: number; total: number };
type Analytics = {
  from: string;
  to: string;
  granularity: "day" | "month";
  currency: string;
  current: Kpi;
  previous: Kpi;
  series: SeriesPoint[];
  topProducts: TopProduct[];
  statuses: StatusCount[];
  lastSyncCompletedAtUtc: string | null;
};

// ---- Date helpers ----

/**
 * "2026-11-03" → a Date at LOCAL midnight.
 * Why not `new Date("2026-11-03")`? JavaScript parses that format as UTC midnight, which in
 * El Salvador (UTC-6) is Nov 2 at 6pm, so every label would be one day early.
 */
function parseLocalDate(iso: string): Date {
  const [y, m, d] = iso.split("-").map(Number); // array destructuring
  return new Date(y, m - 1, d);                  // months are 0-based in JS
}

/** A Date → "YYYY-MM-DD" in local time (what the API expects). */
function toIsoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

// Range presets: label + number of days (including today).
const RANGES = [
  { key: "7d", label: "7D", days: 7, name: "7 days" },
  { key: "30d", label: "30D", days: 30, name: "30 days" },
  { key: "90d", label: "90D", days: 90, name: "90 days" },
  { key: "12m", label: "12M", days: 365, name: "12 months" },
  { key: "3y", label: "3Y", days: 1095, name: "3 years" },
] as const;
// "as const": TypeScript treats the array as fixed literals, so RangeKey below is the
// exact union "7d" | "30d" | "90d" | "12m" | "3y" instead of just "string".
type RangeKey = (typeof RANGES)[number]["key"];

// ---- Number formatting (Intl = the browser's built-in, locale-aware formatter) ----
function money(value: number, currency: string, compact = false) {
  return new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: currency || "USD",
    notation: compact ? "compact" : "standard",
    maximumFractionDigits: compact ? 1 : 2,
  }).format(value);
}
const count = (n: number) => n.toLocaleString();

/** % change vs previous period, or null if there's nothing to compare against. */
function change(current: number, previous: number): number | null {
  if (previous === 0) return null;
  return (current - previous) / previous;
}

// ---- Small presentational components ----

/**
 * Stat tile: label · value · delta vs the previous period.
 * The delta shows an ARROW + sign + color, never color alone (colorblind-safe).
 * upIsGood=false flips the meaning (more cancellations is bad).
 */
function StatTile(props: {
  label: string;
  value: string;
  current: number;
  previous: number;
  periodName: string;
  upIsGood?: boolean;
  sub?: string;
}) {
  const { label, value, current, previous, periodName, upIsGood = true, sub } = props;
  const delta = change(current, previous);
  const good = delta !== null && (upIsGood ? delta >= 0 : delta <= 0);

  return (
    <div className="rounded-xl border border-[var(--viz-border)] bg-[var(--viz-surface)] p-4">
      <div className="text-sm text-[var(--viz-text-secondary)]">{label}</div>
      <div className="mt-1 text-2xl font-semibold text-[var(--viz-text-primary)]">{value}</div>
      {sub && <div className="text-xs text-[var(--viz-text-secondary)]">{sub}</div>}
      <div className="mt-2 text-xs text-[var(--viz-text-secondary)]">
        {delta === null ? (
          <span>No data for the previous {periodName}</span>
        ) : (
          <>
            {/* <>…</> is a Fragment: groups elements without adding a DOM node */}
            <span style={{ color: good ? "var(--viz-good)" : "var(--viz-critical)" }}>
              {delta >= 0 ? "▲" : "▼"} {delta >= 0 ? "+" : ""}
              {(delta * 100).toFixed(1)}%
            </span>{" "}
            vs previous {periodName}
          </>
        )}
      </div>
    </div>
  );
}

// ---- The dashboard ----

type Props = { storeId: number; refreshKey: number };

/**
 * What: analytics for the active store: KPI tiles, revenue chart, top products, statuses.
 * Data: one request to /api/stores/:id/analytics?from&to (all numbers computed in Postgres).
 */
export default function AnalyticsDashboard({ storeId, refreshKey }: Props) {
  const [range, setRange] = useState<RangeKey>("30d");
  const [data, setData] = useState<Analytics | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<ApiError | null>(null);
  const [showTable, setShowTable] = useState(false);

  const preset = RANGES.find((r) => r.key === range)!; // "!" = we know it exists

  const load = useCallback(async () => {
    const to = new Date();
    const from = new Date(to);
    from.setDate(to.getDate() - (preset.days - 1));

    setLoading(true);
    try {
      const qs = new URLSearchParams({ from: toIsoDate(from), to: toIsoDate(to) });
      setData(await fetchJson<Analytics>(`/api/stores/${storeId}/analytics?${qs}`));
      setError(null);
    } catch (e) {
      setError(toApiError(e));
    } finally {
      setLoading(false);
    }
  }, [storeId, preset.days]);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  const currency = data?.currency ?? "USD";
  const fmtPeriod = (iso: string) =>
    parseLocalDate(iso).toLocaleDateString(undefined,
      data?.granularity === "month" ? { month: "short", year: "numeric" } : { month: "short", day: "numeric" });

  const maxProductRevenue = Math.max(1, ...(data?.topProducts.map((p) => p.revenue) ?? [0]));
  //                                      ^ "..." spread: passes the array's items as separate arguments

  return (
    <section className="mb-4 rounded-2xl border border-[var(--viz-border)] p-4 text-[var(--viz-text-primary)]">
      {/* Header + range filter (filters live in ONE row above the charts) */}
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold">Analytics</h2>
          {data && (
            <div className="text-xs text-[var(--viz-text-secondary)]">
              {parseLocalDate(data.from).toLocaleDateString()} – {parseLocalDate(data.to).toLocaleDateString()}
              {data.lastSyncCompletedAtUtc && ` · data synced ${new Date(data.lastSyncCompletedAtUtc).toLocaleString()}`}
            </div>
          )}
        </div>

        {/* Segmented control. aria-pressed tells screen readers which button is active. */}
        <div className="inline-flex rounded-lg border border-[var(--viz-border)] p-0.5" role="group" aria-label="Date range">
          {RANGES.map((r) => (
            <button
              key={r.key}
              onClick={() => setRange(r.key)}
              aria-pressed={range === r.key}
              className={`rounded-md px-3 py-1 text-sm ${
                range === r.key
                  ? "bg-[var(--viz-series-1)] text-white"
                  : "text-[var(--viz-text-secondary)] hover:bg-[var(--viz-grid)]"
              }`}
            >
              {r.label}
            </button>
          ))}
        </div>
      </div>

      {error && (
        <div className="mb-3 rounded-lg border border-[var(--viz-critical)] p-3 text-sm">
          {error.title ?? "Error"}: {error.detail ?? error.raw ?? `status ${error.status}`}
        </div>
      )}

      {!data && loading && <div className="text-sm text-[var(--viz-text-secondary)]">Loading analytics…</div>}

      {data && data.current.orders === 0 && data.previous.orders === 0 && data.statuses.length === 0 ? (
        <div className="text-sm text-[var(--viz-text-secondary)]">
          No orders in this range. If the store was just connected, wait for the first sync to finish.
        </div>
      ) : data && (
        // "opacity-60" while reloading: keep showing the old numbers instead of flashing empty
        <div className={loading ? "opacity-60 transition-opacity" : "transition-opacity"}>
          {/* KPI tiles */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatTile
              label="Revenue"
              value={money(data.current.revenue, currency, data.current.revenue >= 10000)}
              sub={`Net ${money(data.current.netRevenue, currency, data.current.netRevenue >= 10000)} (excl. tax & shipping)`}
              current={data.current.revenue}
              previous={data.previous.revenue}
              periodName={preset.name}
            />
            <StatTile
              label="Orders"
              value={count(data.current.orders)}
              sub={`${count(data.current.itemsSold)} items sold`}
              current={data.current.orders}
              previous={data.previous.orders}
              periodName={preset.name}
            />
            <StatTile
              label="Average order value"
              value={money(data.current.averageOrderValue, currency)}
              current={data.current.averageOrderValue}
              previous={data.previous.averageOrderValue}
              periodName={preset.name}
            />
            <StatTile
              label="Cancelled orders"
              value={count(data.current.cancelledOrders)}
              sub={`${money(data.current.cancelledValue, currency, data.current.cancelledValue >= 10000)} not collected`}
              current={data.current.cancelledOrders}
              previous={data.previous.cancelledOrders}
              periodName={preset.name}
              upIsGood={false}
            />
          </div>

          {/* Revenue over time: ONE series → no legend, the title names it. */}
          <div className="mt-4 rounded-xl border border-[var(--viz-border)] bg-[var(--viz-surface)] p-4">
            <div className="mb-2 flex items-center justify-between">
              <h3 className="text-sm font-semibold">
                Revenue per {data.granularity}{" "}
                <span className="font-normal text-[var(--viz-text-secondary)]">(processing + completed orders)</span>
              </h3>
              <button className="text-xs text-[var(--viz-text-secondary)] underline" onClick={() => setShowTable((s) => !s)}>
                {showTable ? "Show chart" : "View as table"}
              </button>
            </div>

            {showTable ? (
              // Table view: every value is also reachable without hovering (accessibility).
              <div className="max-h-72 overflow-auto">
                <table className="w-full text-sm tabular-nums">
                  <thead className="text-left text-[var(--viz-text-secondary)]">
                    <tr><th className="py-1">Period</th><th className="py-1 text-right">Revenue</th><th className="py-1 text-right">Orders</th></tr>
                  </thead>
                  <tbody>
                    {data.series.map((p) => (
                      <tr key={p.period} className="border-t border-[var(--viz-grid)]">
                        <td className="py-1">{fmtPeriod(p.period)}</td>
                        <td className="py-1 text-right">{money(p.revenue, currency)}</td>
                        <td className="py-1 text-right">{count(p.orders)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              // ResponsiveContainer: the chart resizes with its parent (needs a fixed height).
              <div className="h-64">
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={data.series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
                    {/* Hairline, solid, horizontal-only gridlines: recessive, not competing with data */}
                    <CartesianGrid vertical={false} stroke="var(--viz-grid)" strokeWidth={1} />
                    <XAxis
                      dataKey="period"
                      tickFormatter={fmtPeriod}
                      tick={{ fill: "var(--viz-text-secondary)", fontSize: 12 }}
                      tickLine={false}
                      axisLine={{ stroke: "var(--viz-grid)" }}
                      minTickGap={24}
                    />
                    <YAxis
                      tickFormatter={(v: number) => money(v, currency, true)}
                      tick={{ fill: "var(--viz-text-secondary)", fontSize: 12 }}
                      tickLine={false}
                      axisLine={false}
                      width={64}
                    />
                    {/* Crosshair (cursor) + tooltip listing revenue AND orders for that period */}
                    <Tooltip
                      cursor={{ stroke: "var(--viz-text-secondary)", strokeWidth: 1 }}
                      contentStyle={{
                        background: "var(--viz-surface)",
                        border: "1px solid var(--viz-border)",
                        borderRadius: 8,
                        color: "var(--viz-text-primary)",
                      }}
                      labelFormatter={(label) => fmtPeriod(String(label))}
                      formatter={(value, _name, item) => {
                        const p = item.payload as SeriesPoint;
                        return [`${money(Number(value), currency)} · ${count(p.orders)} orders`, "Revenue"];
                      }}
                    />
                    {/* 2px line + ~10% wash. No dots on every point; a ringed dot on hover only. */}
                    <Area
                      type="monotone"
                      dataKey="revenue"
                      stroke="var(--viz-series-1)"
                      strokeWidth={2}
                      fill="var(--viz-series-1)"
                      fillOpacity={0.1}
                      dot={false}
                      activeDot={{ r: 5, stroke: "var(--viz-surface)", strokeWidth: 2 }}
                      isAnimationActive={false}
                    />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
            )}
          </div>

          <div className="mt-4 grid grid-cols-1 gap-4 lg:grid-cols-3">
            {/* Top products: thin horizontal bars (magnitude), value at the bar's tip */}
            <div className="rounded-xl border border-[var(--viz-border)] bg-[var(--viz-surface)] p-4 lg:col-span-2">
              <h3 className="mb-3 text-sm font-semibold">Top products by revenue</h3>
              {data.topProducts.length === 0 ? (
                <div className="text-sm text-[var(--viz-text-secondary)]">No product sales in this range.</div>
              ) : (
                <ol className="space-y-2">
                  {data.topProducts.map((p) => (
                    <li key={p.productId} title={`${p.name}: ${money(p.revenue, currency)} · ${count(p.quantity)} sold`}>
                      <div className="flex justify-between gap-3 text-sm">
                        <span className="truncate">{p.name || "(deleted product)"}</span>
                        <span className="shrink-0 tabular-nums text-[var(--viz-text-secondary)]">
                          {money(p.revenue, currency)} · {count(p.quantity)} sold
                        </span>
                      </div>
                      {/* 8px bar, width proportional to revenue (max = 100%), rounded data end */}
                      <div className="mt-1 h-2 rounded-r bg-[var(--viz-series-1)]"
                           style={{ width: `${Math.max(2, (p.revenue / maxProductRevenue) * 100)}%` }} />
                    </li>
                  ))}
                </ol>
              )}
            </div>

            {/* Orders by status: a plain table (labels carry meaning, not color) */}
            <div className="rounded-xl border border-[var(--viz-border)] bg-[var(--viz-surface)] p-4">
              <h3 className="mb-3 text-sm font-semibold">Orders by status</h3>
              <table className="w-full text-sm tabular-nums">
                <tbody>
                  {data.statuses.map((s) => (
                    <tr key={s.status} className="border-t border-[var(--viz-grid)] first:border-t-0">
                      <td className="py-1 capitalize">{s.status.replace("-", " ")}</td>
                      <td className="py-1 text-right">{count(s.orders)}</td>
                      <td className="py-1 text-right text-[var(--viz-text-secondary)]">{money(s.total, currency)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
