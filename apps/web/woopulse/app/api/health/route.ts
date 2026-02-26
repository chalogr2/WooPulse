import { NextResponse } from "next/server";

const API_BASE = process.env.API_BASE_URL!;

export async function GET() {
  const res = await fetch(`${API_BASE}/health`, { cache: "no-store" });
  const data = await res.json();
  return NextResponse.json(data, { status: res.status });
}