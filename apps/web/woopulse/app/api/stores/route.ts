import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

// GET /api/stores -> calls GET http://localhost:5113/stores
export async function GET() {
  const { token } = await auth0.getAccessToken();

  const res = await fetch("http://localhost:5113/stores", {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}

// POST /api/stores -> calls POST http://localhost:5113/stores
export async function POST(req: Request) {
  const { token } = await auth0.getAccessToken();
  const body = await req.text();

  const res = await fetch("http://localhost:5113/stores", {
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