import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { getBearerToken } from "@/app/api/_lib/auth";

export async function GET() {
  const jar = await cookies();
  const token = await getBearerToken();

  return NextResponse.json({
    cookieNames: jar.getAll().map((c) => c.name),
    hasBearerToken: !!token,
    bearerTokenLength: token?.length ?? 0,
    bearerTokenPreview: token ? `${token.slice(0, 16)}...${token.slice(-8)}` : null,
  });
}