import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

export async function GET() {
  try {
    const tokenResult = await auth0.getAccessToken();

    const accessToken = tokenResult.token; // <-- THIS is correct for your SDK

    if (!accessToken) {
      return NextResponse.json(
        { error: "No access token returned from Auth0." },
        { status: 401 }
      );
    }

    const upstream = await fetch("http://localhost:5113/me", {
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
    });

    const raw = await upstream.text();

    return NextResponse.json({
      tokenPrefix: accessToken.slice(0, 30),
      upstreamStatus: upstream.status,
      upstreamBody: raw,
    });
  } catch (e: any) {
    return NextResponse.json(
      { error: e?.message ?? "Unknown error" },
      { status: 500 }
    );
  }
}