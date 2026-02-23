import { NextResponse } from "next/server";
import { auth0 } from "@/lib/auth0";

export async function GET() {
  const session = await auth0.getSession();

  if (!session?.user) {
    return NextResponse.json({ loggedIn: false });
  }

  const u: any = session.user;

  return NextResponse.json({
    loggedIn: true,
    name: u.name ?? u.nickname ?? null,
    email: u.email ?? null,
    sub: u.sub ?? null,
  });
}