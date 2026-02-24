import { NextResponse } from "next/server";
import { cookies } from "next/headers";

const API_BASE = process.env.API_BASE_URL ?? "http://localhost:5113";

async function getToken() {
  const jar = await cookies(); 
  return jar.get("access_token")?.value;
}

export async function POST(
  req: Request,
  ctx: { params: Promise<{ id: string }> } 
) {
  const { id } = await ctx.params; 
  const body = await req.text();
  const token = await getToken();

  const headers = new Headers();
  headers.set("Content-Type", "application/json");
  if (token) headers.set("Authorization", `Bearer ${token}`);

  const upstream = await fetch(`${API_BASE}/stores/${id}/invites`, {
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