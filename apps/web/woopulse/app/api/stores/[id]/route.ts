import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

export async function PUT(req: Request, ctx: { params: Promise<{ id: string }> }) {
  const { token } = await auth0.getAccessToken();
  const { id } = await ctx.params;
  const body = await req.text();

  const res = await fetch(`http://localhost:5113/stores/${id}`, {
    method: "PUT",
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

export async function DELETE(_: Request, ctx: { params: Promise<{ id: string }> }) {
  const { token } = await auth0.getAccessToken();
  const { id } = await ctx.params;

  const res = await fetch(`http://localhost:5113/stores/${id}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });

  return new NextResponse(null, { status: res.status });
}