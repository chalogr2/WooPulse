import { NextResponse } from "next/server";
import { cookies } from "next/headers";

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:5113";
const TOKEN_COOKIE = "access_token";

async function buildAuthHeaders(): Promise<Headers> {
  const cookieStore = await cookies();
  const token = cookieStore.get(TOKEN_COOKIE)?.value;

  const headers = new Headers();
  if (token) headers.set("Authorization", `Bearer ${token}`);
  return headers;
}

export async function POST(req: Request) {
  const body = await req.text();

  const headers = await buildAuthHeaders();
  headers.set("Content-Type", "application/json");

  const upstream = await fetch(`${API_BASE}/invites/accept`, {
    method: "POST",
    headers,
    body,
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