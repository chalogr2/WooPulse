import { NextRequest, NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

const API_BASE = process.env.API_BASE_URL!;

function forwardSetCookies(from: NextResponse, to: NextResponse) {
  // NextResponse may contain one or multiple Set-Cookie headers
  const anyHeaders = from.headers as any;
  const setCookies: string[] =
    typeof anyHeaders.getSetCookie === "function"
      ? anyHeaders.getSetCookie()
      : (from.headers.get("set-cookie") ? [from.headers.get("set-cookie")!] : []);

  for (const c of setCookies) to.headers.append("set-cookie", c);
}

async function getAccessTokenFromAuth0(request: NextRequest) {
  // Auth0 v4 needs BOTH request + response in route handlers
  const auth0Response = new NextResponse();
  const tokenResult = await auth0.getAccessToken(request, auth0Response);

  // v4 returns { token, expiresAt, ... }
  const accessToken = (tokenResult as any)?.token as string | undefined;

  return { accessToken, auth0Response };
}

export async function GET(request: NextRequest) {
  const { accessToken, auth0Response } = await getAccessTokenFromAuth0(request);

  const upstream = await fetch(`${API_BASE}/stores`, {
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : {},
  });

  const raw = await upstream.text();

  const out = NextResponse.json({
    upstreamStatus: upstream.status,
    upstreamBody: raw,
  });

  // IMPORTANT: forward auth cookies if Auth0 set/rotated them
  forwardSetCookies(auth0Response, out);

  return out;
}

export async function POST(request: NextRequest) {
  const { accessToken, auth0Response } = await getAccessTokenFromAuth0(request);

  const body = await request.text();

  const upstream = await fetch(`${API_BASE}/stores`, {
    method: "POST",
    headers: {
      "content-type": "application/json",
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body,
  });

  const raw = await upstream.text();

  const out = NextResponse.json({
    upstreamStatus: upstream.status,
    upstreamBody: raw,
  });

  forwardSetCookies(auth0Response, out);
  return out;
}