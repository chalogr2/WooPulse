import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

const API_BASE = process.env.API_BASE_URL!;

// GET /api/stores -> calls GET {API_BASE}/stores
export async function GET() {
  const { token } = await auth0.getAccessToken();

  const res = await fetch(`${API_BASE}/stores`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}

// POST /api/stores -> calls POST {API_BASE}/stores
export async function POST(req: Request) {
  const { token } = await auth0.getAccessToken();
  const body = await req.text();

  const res = await fetch(`${API_BASE}/stores`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body,
  });

  const raw = await res.text();
  const contentType = res.headers.get("content-type") ?? "";
  const payload = contentType.includes("application/json") && raw ? JSON.parse(raw) : raw;

  return NextResponse.json(payload, { status: res.status });
}