"use client";

import { useCallback, useEffect, useState } from "react";
import { fetchJson, toApiError, type ApiError } from "@/lib/client-api";
import type { AuthState, Store } from "@/lib/types";
import { ErrorText } from "@/components/ui";
import UserCard from "@/components/UserCard";
import StoresPanel from "@/components/StoresPanel";
import ActiveStoreSelect from "@/components/ActiveStoreSelect";
import ConnectionPanel from "@/components/ConnectionPanel";
import AnalyticsDashboard from "@/components/AnalyticsDashboard";
import StoreList from "@/components/StoreList";
import TrashPanel from "@/components/TrashPanel";

/**
 * What: the WooPulse page. Since DEVLOG #015 it only does two things:
 *   1. holds the state several blocks SHARE (who I am, my stores, the active store), and
 *   2. lays the blocks out in the owner's order.
 * Each block is its own component in components/ and handles its own fetching, buttons and errors.
 *
 * Data flow ("lifting state up"): state that several components need lives in their
 * closest common parent (here). Children get it as props and report changes back through
 * callbacks like onStoresChanged / onSelect.
 */
export default function Home() {
  const [auth, setAuth] = useState<AuthState | null>(null);   // null = still checking
  const [stores, setStores] = useState<Store[]>([]);
  const [activeStoreId, setActiveStoreId] = useState<number | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  // Bumped after every successful load(); the blocks watch it and reload themselves.
  const [dataVersion, setDataVersion] = useState(0);

  // My stores. useCallback: a stable function, safe to pass to children as a callback.
  const load = useCallback(async () => {
    try {
      const list = await fetchJson<Store[]>("/api/stores");
      setStores(list);
      // Keep the active store if it still exists; otherwise (first load, or I left/deleted it)
      // pick the first store, or null. Functional form = reads the LATEST state.
      setActiveStoreId((current) => (list.some((s) => s.id === current) ? current : list[0]?.id ?? null));
      setDataVersion((v) => v + 1);
      setError(null);
    } catch (e) {
      setError(toApiError(e));
      setStores([]);
    }
  }, []);

  // On page open: who am I? (reads the Auth0 session cookie on the Next.js server)
  // Then load stores ONLY if logged in (avoids a pointless 401 banner for visitors).
  // State is set inside the .then() CALLBACK (when the response arrives), not directly in the
  // effect body. React's lint rule "set-state-in-effect" flags the latter: it causes an extra
  // render pass right after the effect runs.
  useEffect(() => {
    fetchJson<AuthState>("/api/profile")
      .then((me) => {
        setAuth(me);
        if (me.loggedIn) void load();
      })
      .catch(() => setAuth({ loggedIn: false }));
  }, [load]);

  // After creating a store: reload, then make the new store the active one.
  const handleCreated = useCallback(
    async (storeId: number) => {
      await load();
      setActiveStoreId(storeId);
    },
    [load]
  );

  // Derived value: computed from state on every render, so it never gets out of sync.
  const activeStore = stores.find((s) => s.id === activeStoreId);

  return (
    <main className="mx-auto max-w-6xl space-y-4 p-4 sm:p-6">
      <header className="flex items-center gap-2">
        <span aria-hidden="true" className="text-xl">📈</span>
        <h1 className="text-xl font-semibold">WooPulse</h1>
      </header>

      <ErrorText error={error} />

      {/* Order (owner's spec, DEVLOG #016): one full-width column, top to bottom. */}
      <UserCard auth={auth} />

      {/* Everything below needs a login */}
      {auth?.loggedIn && (
        <>
          <ActiveStoreSelect stores={stores} activeStoreId={activeStoreId} onSelect={setActiveStoreId} />

          {activeStore && (
            <>
              <ConnectionPanel
                storeId={activeStore.id}
                myRole={activeStore.myRole}
                refreshKey={dataVersion}
                onSynced={load}
              />
              <AnalyticsDashboard storeId={activeStore.id} refreshKey={dataVersion} />
              <StoresPanel activeStore={activeStore} refreshKey={dataVersion} onStoresChanged={load} />
            </>
          )}

          {/* My stores also contains "create store" and "Invites for you" (DEVLOG #017, #018) */}
          <StoreList
            stores={stores}
            activeStoreId={activeStoreId}
            refreshKey={dataVersion}
            onSelect={setActiveStoreId}
            onStoresChanged={load}
            onCreated={handleCreated}
          />
          <TrashPanel mySub={auth.sub} refreshKey={dataVersion} onStoresChanged={load} />
        </>
      )}
    </main>
  );
}
