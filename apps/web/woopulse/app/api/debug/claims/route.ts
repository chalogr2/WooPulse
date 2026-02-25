import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:5113";

export async function GET() {
  let token: string;
  try {
    ({ token } = await auth0.getAccessToken());
  } catch {
    return NextResponse.json({ error: "Not authenticated" }, { status: 401 });
  }

  const upstream = await fetch(`${API_BASE}/debug/claims`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  const text = await upstream.text();

  return new NextResponse(text, {
    status: upstream.status,
    headers: {
      "content-type": upstream.headers.get("content-type") ?? "application/json",
    },
  });
}