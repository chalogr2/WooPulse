"use client";

import { useEffect, useState } from "react";

type Health = { status: string; utc: string };
type Store = { id: number; name: string; createdAtUtc: string };

export default function Home() {
  const [health, setHealth] = useState<Health | null>(null);
  const [stores, setStores] = useState<Store[]>([]);
  const [name, setName] = useState("StoreFromUI");
  const [busy, setBusy] = useState(false);

  async function load() {
    const [h, s] = await Promise.all([
      fetch("http://localhost:5113/health", { cache: "no-store" }).then(r => r.json()),
      fetch("http://localhost:5113/stores", { cache: "no-store" }).then(r => r.json()),
    ]);
    setHealth(h);
    setStores(s);
  }

  useEffect(() => {
    load();
  }, []);

  async function createStore() {
    setBusy(true);
    try {
      const res = await fetch("http://localhost:5113/stores", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name }),
      });

      if (!res.ok) {
        const text = await res.text();
        throw new Error(`POST /stores failed (${res.status}): ${text}`);
      }

      await load();
      setName("StoreFromUI");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main style={{ padding: 24, fontFamily: "system-ui" }}>
      <h1>WooPulse</h1>

      <h2>API health</h2>
      <pre>{health ? JSON.stringify(health, null, 2) : "Loading..."}</pre>

      <h2>Stores</h2>
      <div style={{ display: "flex", gap: 8, marginBottom: 12 }}>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Store name"
          style={{ padding: 8, width: 260 }}
        />
        <button onClick={createStore} disabled={busy || !name.trim()} style={{ padding: "8px 12px" }}>
          {busy ? "Creating..." : "Create store"}
        </button>
        <button onClick={load} disabled={busy} style={{ padding: "8px 12px" }}>
          Refresh
        </button>
      </div>

      <pre>{JSON.stringify(stores, null, 2)}</pre>
    </main>
  );
}
