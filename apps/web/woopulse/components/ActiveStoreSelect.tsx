import type { Store } from "@/lib/types";
import { Card, inputClass, muted, RoleBadge } from "@/components/ui";

type Props = {
  stores: Store[];
  activeStoreId: number | null;
  onSelect: (storeId: number) => void;
};

/** What: the "Active store" dropdown. Every panel below shows data for this store. */
export default function ActiveStoreSelect({ stores, activeStoreId, onSelect }: Props) {
  const active = stores.find((s) => s.id === activeStoreId);

  return (
    <Card>
      <div className="flex flex-wrap items-center gap-3">
        {/* htmlFor links the label to the select (clicking the label focuses it; screen readers read it) */}
        <label htmlFor="active-store" className="text-sm font-semibold">Active store</label>
        {stores.length === 0 ? (
          <span className={`text-sm ${muted}`}>You don&apos;t have any stores yet. Create one above.</span>
        ) : (
          <>
            <select
              id="active-store"
              className={`${inputClass} min-w-56`}
              value={activeStoreId ?? ""}
              onChange={(e) => onSelect(Number(e.target.value))}
            >
              {stores.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name} (#{s.id})
                </option>
              ))}
            </select>
            {active && <RoleBadge role={active.myRole} />}
          </>
        )}
      </div>
    </Card>
  );
}
