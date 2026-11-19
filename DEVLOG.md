# WooPulse — Development Log

This file is the running history of WooPulse: **every change, addition, and bug fix**, plus
**why** it was done and **how** it works. It is written so that any engineer — including a
junior dev joining the project for the first time — can read it top to bottom and understand
the system and the reasoning behind it.

## How to use this log

- **Newest entries go at the bottom** of the "Entries" section, numbered (`#001`, `#002`, ...).
- Every entry has the same shape:
  - **What** — the change, in one or two sentences.
  - **Why** — the problem it solves (bug, security, missing feature).
  - **How** — the approach and the reasoning behind it, written to teach.
  - **Files** — every file touched, so the diff can be reviewed line by line.
  - **How to verify** — the steps to confirm it works (or what was actually tested).
- If you think an entry is wrong, write a correction as a new entry that points to the old one.
  Don't silently rewrite history.

---

## Project overview (read this first)

### What WooPulse is

A multi-tenant SaaS app: a **WooCommerce store monitoring / analytics dashboard**. A user
signs in, creates **Stores** (each one represents a WooCommerce shop), and invites other
people to those stores with a role (Owner / Admin / Member). Each user only sees stores they
belong to. That rule, "you only see your own tenant's data", is what *multi-tenant* means here.

**MVP scope (confirmed by the project owner, 2026-11-17):**
1. The team and store features: stores, members, invites (mostly built).
2. Displaying analytics for each store (not built yet).
3. A decent-looking frontend (current UI is a single debug-style page).

**Deployment:** runs locally only (`localhost:3000` + `localhost:5113`), in Development mode.
There are no plans to host it online for now.

### Architecture

```
 Browser (React page)
     │  fetch("/api/...")      ← same-origin, carries an encrypted session cookie
     ▼
 Next.js 16 app  (apps/web/woopulse)          "BFF" = Backend-For-Frontend
     │  - Auth0 SDK runs the login flow and keeps the session in a cookie
     │  - /app/api/**/route.ts files read the Auth0 access token from the session
     │    and forward the request to the .NET API with  Authorization: Bearer <token>
     ▼
 ASP.NET Core 8 minimal API  (apps/api/WooPulse.Api)
     │  - Validates the JWT against Auth0 (issuer + audience + signature)
     │  - Checks a permission claim (stores:read / stores:write / stores:delete)
     │  - Services enforce per-store role rules (StoreService)
     ▼
 PostgreSQL 16 (Docker)  — two databases:
     - woopulse  → app tables (EF Core migrations)
     - hangfire  → background job tables (Hangfire creates them itself)
```

**Why a BFF?** The browser never sees the access token. It only holds an encrypted, httpOnly
session cookie. The Next.js server swaps that cookie for a token and calls the API. This
protects the token from XSS (malicious JavaScript can't read it).

**Two layers of authorization** — keep these apart in your head:

1. **Global permission** (from Auth0 RBAC, inside the token's `permissions` claim):
   "Is this user allowed to use the stores feature at all?" → checked by
   `.RequireAuthorization("stores:read")` in `Program.cs`.
2. **Per-store role** (from the `StoreMembers` table): "Is this user an Owner/Admin of
   *this specific* store?" → checked inside `StoreService` (`CanWrite`).

### Domain model (`apps/api/WooPulse.Api/Data`)

| Entity | Purpose | Notes |
|---|---|---|
| `Store` | A tenant. | Soft-deleted (`IsDeleted` + audit fields). A global EF *query filter* hides deleted stores from every query unless `IgnoreQueryFilters()` is used. |
| `StoreMember` | Links a user (`UserSub` = Auth0 user id) to a store with a `Role`. | Unique on `(StoreId, UserSub)`. |
| `StoreInvitation` | A pending invite by email, with a random 32-byte token. | Status: Pending → Accepted / Revoked / Expired. Expires after 7 days. |

### Background jobs (Hangfire, registered in `Program.cs`)

| Job | Schedule | What it does |
|---|---|---|
| `heartbeat` | every minute | Prints a line. Smoke test only. |
| `expire-invites` | hourly | Pending invites past `ExpiresAtUtc` → `Expired`. |
| `purge-soft-deleted-stores` | daily | Hard-deletes stores soft-deleted > 30 days ago, plus their members/invites, in one transaction. |

### Running locally

1. `cd apps/api && docker compose up -d` (Postgres; the init script creates the `hangfire` DB).
2. API secrets live in **.NET user-secrets** (not in git): `ConnectionStrings:AppDb`,
   `ConnectionStrings:HangfireDb`, `Auth0:Domain`.
   Set with `dotnet user-secrets set "Auth0:Domain" "<tenant>.us.auth0.com"` from `apps/api/WooPulse.Api`.
3. `dotnet ef database update` then `dotnet run` (API on http://localhost:5113, Swagger at `/swagger`).
4. Web app needs `apps/web/woopulse/.env.local` with: `AUTH0_SECRET`, `AUTH0_DOMAIN`,
   `AUTH0_CLIENT_ID`, `AUTH0_CLIENT_SECRET`, `AUTH0_AUDIENCE`, `AUTH0_SCOPE`, `APP_BASE_URL`,
   `API_BASE_URL`. Then `pnpm install && pnpm dev` (http://localhost:3000).

---

## Methodology (how changes are made in this project)

1. **Small, reviewable diffs.** One concern per change. No drive-by refactors. Structural
   changes (splitting files, new libraries, new abstractions) are proposed first and only
   done after approval.
2. **Fix order: security → correctness → MVP features → cleanup.** A bug that leaks data
   comes before a missing button.
3. **Every change gets a devlog entry** in the format above, written to teach: what the code
   does, why it's shaped that way, and the language syntax or framework idiom involved.
4. **Verify before claiming done.** At minimum: `dotnet build` passes, `tsc --noEmit` passes,
   and the affected flow is exercised. Anything not tested is listed under "How to verify"
   as not yet tested.
5. **Comments in code explain *why*, not just *what*.** Follows the existing
   `// What: ... // Why: ...` style in the services.

---

## Entries

### #001 — 2026-11-17 — Full project audit (no code changed)

**What:** Read every tracked source file in the API and the web app, built the API, ran the
TypeScript compiler and ESLint on the web app, and checked git history for leaked secrets.
This entry records the results and the proposed plan. **No source code was modified.**

**Build status at time of audit**

| Check | Result |
|---|---|
| `dotnet build` (API) | ✅ 0 warnings, 0 errors |
| `tsc --noEmit` (web) | ✅ passes |
| `pnpm lint` (web) | ❌ 18 errors, 5 warnings (mostly `any` types, unused debug code in `page.tsx`) |
| Automated tests | ⚠️ none exist |

**Secrets check:** The DB connection string (password `woopulse`, same as `docker-compose.yml`)
and the Auth0 tenant domain were committed in earlier history and removed in `d5a6b53`. The
DB password is the local Docker one, and an Auth0 domain is public information anyway, so
neither is a real leak. **Any production database must use a different password.** No client
secrets were found in git. `.env.local` is correctly git-ignored.

#### Findings

Severity: 🔴 fix before real users · 🟠 bug / MVP blocker · 🟡 quality / cleanup

**Security**

| # | Sev | Where | Finding |
|---|---|---|---|
| S1 | 🔴 | `Program.cs:89` | The Hangfire dashboard uses `LocalRequestsOnlyAuthorizationFilter`. Behind a reverse proxy on the same machine (nginx, IIS, a container sidecar), **every request looks local**, so the dashboard, which can trigger and delete jobs, is open to the internet. The custom `HangfireLocalOnlyAuthFilter` has the same weakness and isn't used anyway. Fix: disable the dashboard outside Development, or protect it with a real auth check. |
| S2 | 🔴 | `Program.cs:323` | `/debug/claims` is **not** gated by `IsDevelopment()` (the other debug routes are). In production any logged-in user can dump their token claims. |
| S3 | 🔴 | `app/api/debug/token/route.ts` | Unauthenticated BFF route that returns cookie names and a preview of the session cookie. Also `app/api/api-me/route.ts` returns the first 30 chars of the access token. Both are debugging leftovers and should be deleted or gated to development. |
| S4 | 🟠 | `app/api/debug/claims`, `app/api/invites/mine/debug` | BFF debug proxies exist in production builds. The API 404s the second one outside dev, but the BFF shouldn't expose them at all. |
| S5 | 🟡 | `Program.cs:95` | `UseExceptionHandler()` is registered *after* auth middleware. It should be first in the pipeline so it catches exceptions thrown anywhere. |

**Correctness bugs**

| # | Sev | Where | Finding |
|---|---|---|---|
| B1 | 🟠 | `app/api/stores/route.ts`, `stores/[id]/route.ts` | When logged out, `auth0.getAccessToken()` **throws**, and the error isn't caught → the route returns a 500 instead of 401. Also `GET` calls `res.json()` on an API 401 with an empty body, which throws → 500. Effect: a logged-out visitor sees "Request failed (500)" on the home page. (The invites routes already handle this correctly with `try/catch`, so the pattern exists, it just isn't applied everywhere.) |
| B2 | 🟠 | Invite flow (whole feature) | **Invitees can't accept invites outside dev.** The token is only returned by `/invites/mine/debug` (dev-only). The normal `/invites/mine` hides it, `InviteAsync` doesn't return it, and no email is sent. So in production the invite flow dead-ends. |
| B3 | 🟠 | `StoreService.cs:274` | `AcceptInviteAsync` comment says the transaction prevents TOCTOU races, but at Postgres's default *Read Committed* isolation, two simultaneous accepts both read `Pending`. The unique index stops a duplicate member, but the loser gets an unhandled 500. Needs a row lock (`SELECT ... FOR UPDATE`) or a conditional update. Low impact, but the comment is misleading for learners. |
| B4 | 🟠 | Auth0 config | `/invites/mine` and `/invites/accept` require an email claim **in the access token** (`https://woopulse.local/email`). Auth0 only adds that through a custom Action. Also, invited users need the `stores:read` permission in Auth0 RBAC or they get 403 on `/stores` even after joining. Both are Auth0 dashboard settings, not code, and must be written down for deployment. |
| B5 | 🟡 | `Program.cs:100` | Heartbeat job: the `$"...{DateTimeOffset.Now}"` string is built **once, when the job is registered**, because Hangfire serializes the method *arguments*. Every run prints the same timestamp. (Good learning example of how Hangfire stores jobs.) |
| B6 | 🟡 | `StoreService.cs:248` | `ListMyInvitesAsync` can return invites that are past expiry but not yet flipped by the hourly job. It should also filter `ExpiresAtUtc > now`. It also returns no store name, so the UI can only show "Store #12". |
| B7 | 🟡 | `page.tsx:84-93` | Frontend `Invitation.role/status` are typed `number`, but the API sends strings (`"Member"`, `"Pending"`). |

**MVP feature gaps (backend has no endpoint for these)**

- G1: List members of a store / remove a member / change a member's role / leave a store.
- G2: Revoke a pending invite (the `Revoked` status exists but nothing sets it).
- G3: Restore a soft-deleted store (`/stores/deleted` exists, but no restore). BFF doesn't proxy `/stores/deleted`.
- G4: A real way to deliver invites (email via a provider, or at least a copyable invite link shown to the inviter).
- G5: Permission rules to confirm: right now an **Admin can delete a store** (`CanWrite` is used for delete). Most SaaS products reserve delete for the Owner.
- G6: The actual product feature on top of stores (see open questions).

**Cleanup / maintainability**

| # | Where | Finding |
|---|---|---|
| C1 | `Program.cs:152`, `WooPulse.Api.http` | `weatherforecast` template leftover. |
| C2 | `Program.cs:167` | `/me` duplicates the `GetSub` helper. |
| C3 | `appsettings.Development.json:8` | `"Auth"` section is unused (the code reads `"Auth0"`). |
| C4 | `app/api/api-stores/`, `app/api/_lib/auth.ts` | Duplicate stores proxy plus a cookie parser with Supabase leftovers. Unused by the UI. |
| C5 | all `app/api/**/route.ts` | ~10 near-identical proxy functions with three different error-handling styles. One shared `proxyToApi()` helper would fix B1 everywhere at once. *(Structural change: needs approval.)* |
| C6 | `app/page.tsx` | 520-line single component, debug UI mixed with real UI, `prompt()`/`confirm()` dialogs, lint errors. Needs splitting into components. *(Structural change: needs approval.)* |
| C7 | `apps/web/pnpm-lock.yaml`, `apps/web/node_modules` | Stray lockfile/deps one level above the real app (the last commit removed the stray `package.json` but not these). |
| C8 | `app/layout.tsx` | Title is still "Create Next App". |
| C9 | `middleware.ts` | Next.js 16 renamed `middleware.ts` to `proxy.ts`. The old name still works but is deprecated. |
| C10 | `WooPulse.Api.csproj` | `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.0` while EF Core is `8.0.24`. Should be bumped to the latest 8.0.x patch to match. |
| C11 | repo | No root README, no `.env.example`, no tests, no CI. `Store.OwnerSub` duplicates the Owner row in `StoreMembers` (two sources of truth; fine for now, but document which one wins). |

#### Proposed plan (awaiting approval; nothing below has been implemented)

**Phase 1: Security hardening (small, isolated diffs)**
1. Gate Hangfire dashboard to Development only (S1); delete unused `HangfireLocalOnlyAuthFilter`.
2. Gate `/debug/claims` to Development (S2).
3. Delete BFF `debug/token`, `api-me`, `api-stores`, `_lib/auth.ts`; gate remaining BFF debug routes to dev (S3, S4, C4).
4. Move `UseExceptionHandler()` to the top of the pipeline (S5).

**Phase 2: Correctness**
5. Shared BFF proxy helper that returns a proper 401 when logged out and passes through API status + body unchanged (B1, C5). *Needs approval because it restructures the route files.*
6. Fix invite listing (expiry filter + store name) and accept race (B3, B6).
7. Fix heartbeat (B5) or remove it.

**Phase 3: MVP features (backend endpoint + BFF route + UI each)**
8. Invite delivery: return an invite link to the inviter, plus an `/invite/[token]` accept page (G4, B2). Email sending can come later.
9. Revoke invite, list members, remove member, change role, leave store (G1, G2).
10. Restore deleted store + Trash view (G3).
11. *(Depends on your answers)* the core product feature (G6).

**Phase 4: Frontend cleanup**
12. Split `page.tsx` into components, remove debug UI, fix lint, fix types (C6, B7, C8).

**Phase 5: Production readiness**
13. Root README + `.env.example` + Auth0 setup checklist (Action for the email claim, RBAC permissions) (B4, C11).
14. Housekeeping: C1–C3, C7, C9, C10.
15. Minimal tests for `StoreService` authorization rules (the riskiest logic).

**Files:** `DEVLOG.md` (new).

**How to verify:** Nothing to verify: no code was changed. Every finding above lists a file
and line so it can be checked by hand.

---

### #002 — 2026-11-17 — Owner decisions + re-audit of the invite flow (corrects #001)

**What:** Recorded the project owner's answers (product, MVP scope, local-only deployment;
see "Project overview" above) and re-checked the invite flow end to end.

**Correction to #001, finding B2 ("invitees can't accept invites"):** that finding is
**only true for a production build**. Since WooPulse runs locally in Development mode,
**invites work today.** Here is the exact flow, traced through the code:

1. Owner/Admin picks the active store, types an email, clicks **Invite**.
   `page.tsx` → `POST /api/stores/{id}/invites` → API `InviteAsync` creates a `Pending`
   invitation with a random token, valid 7 days.
2. The invited person logs into the same app (`localhost:3000`) with **that email**.
3. They click **Refresh invites (debug)** → `/api/invites/mine/debug` → API
   `/invites/mine/debug`. This returns their pending invites **including the token**. It is
   Development-only, but that's exactly how the app runs.
4. They click **Accept this invite** → `/api/invites/accept` → API `AcceptInviteAsync` adds
   a `StoreMember` row and marks the invite `Accepted`. The store appears in their list.

Things to know (not bugs for local use, but important):
- **No email is sent.** "Reaching people" means: they see the invite when they log in.
- The flow depends on the *debug* endpoint. If you ever run `pnpm build && pnpm start`
  (production mode) or run the API outside Development, step 3 stops working. Phase 3
  will add a proper non-debug accept path.
- Step 3 needs the Auth0 Action that puts the email into the access token
  (`https://woopulse.local/email`). Since the flow worked for you before, it is configured.
- Showing your *own* invite tokens to *you* is not a security problem: the API filters by
  the email inside your token.

**Effect on the plan:** because the app is local-only, the security items in Phase 1 are
lower risk than #001 said (nobody on the internet can reach it). They were still done,
since they're small and the app may go online later.

**Files:** `DEVLOG.md`.

---

### #003 — 2026-11-17 — Phase 1: security hardening of the API and BFF

**What:** Closed the debug and admin surfaces that would be dangerous if the app were ever
exposed, and fixed the order of the exception-handling middleware.

**Why:** Findings S1–S5 in #001.

**How (each change, and the idea behind it):**

1. **Hangfire dashboard only in Development** (`Program.cs`).
   Wrapped `app.UseHangfireDashboard(...)` in `if (app.Environment.IsDevelopment())`.
   *Idea:* `LocalRequestsOnlyAuthorizationFilter` decides "is this request from this machine?"
   by looking at the connecting IP. A reverse proxy *is* on this machine, so through a proxy,
   every visitor would pass. Not registering the dashboard at all outside dev is the simplest
   safe rule. Locally nothing changes: `http://localhost:5113/hangfire` still works.
   *Note:* `Security/HangfireLocalOnlyAuthFilter.cs` is still in the repo but is not used by
   anything. It was left in place on purpose (not deleted without asking).

2. **`/debug/claims` gated to Development** (`Program.cs`). Added the same
   `if (!app.Environment.IsDevelopment()) return Results.NotFound();` guard the other debug
   endpoints already use. *Idea:* returning 404 (not 403) outside dev hides that the
   endpoint exists at all.

3. **`UseExceptionHandler()` moved to the top of the pipeline** (`Program.cs`).
   *Idea:* ASP.NET Core middleware is a chain; each piece wraps the ones registered after it.
   An error handler can only catch exceptions from things *inside* it, so it must be first.

4. **Deleted leftover BFF debug routes** (web app):
   - `app/api/debug/token/route.ts`: no login required; returned cookie names and a
     preview of the session cookie.
   - `app/api/api-me/route.ts`: returned the first 30 characters of the access token.
   - `app/api/api-stores/route.ts`: a duplicate of `/api/stores`, unused by the UI.
   - `app/api/_lib/auth.ts`: a cookie parser with Supabase leftovers, only used by `debug/token`.
   Before deleting, the code was searched: nothing in the app imported or called them.

5. **Remaining BFF debug routes gated to development**: `app/api/debug/claims` and
   `app/api/invites/mine/debug` now return 404 unless `NODE_ENV === "development"`
   (which is what `pnpm dev` sets). **The invite flow from #002 still works locally.**

**Files:** `apps/api/WooPulse.Api/Program.cs`; deleted the four web files listed above;
`app/api/debug/claims/route.ts`, `app/api/invites/mine/debug/route.ts` (rewritten in #004).

**How to verify (done 2026-11-17, with the API and `pnpm dev` running):**
- `dotnet build`: 0 warnings, 0 errors. `tsc --noEmit`: passes.
- `GET /api/debug/token` and `GET /api/api-me` → **404** (deleted).
- `GET http://localhost:5113/hangfire` from this machine in Development → **200** (still works).
- `GET http://localhost:5113/debug/claims` without a token → **401** (auth still required in dev).
- **Not tested:** the logged-in flows (requires your Auth0 login in the browser). Please click
  through: create store, rename, delete, invite, accept as the invited user.

---

### #004 — 2026-11-17 — Shared BFF proxy helper (fixes logged-out 500 errors)

**What:** Added `apps/web/woopulse/lib/api-proxy.ts` with one function, `proxyToApi()`, and
rewrote every Next.js API route that talks to the .NET API to use it. Each route is now
2–5 lines.

**Why:** Finding B1 + C5 in #001. There were ~10 copies of "get token → call API → return
response", written three different ways. Two of those versions crashed when you were logged
out: `auth0.getAccessToken()` throws without a session, and `res.json()` throws on an empty
body. So the home page showed **"Request failed (500)"** instead of "please log in".

**Is this frontend or backend?** It is **frontend-repo code that runs on the server**. Files
under `app/api/**/route.ts` and `lib/` run inside the Next.js server process, never in the
browser. This layer is the **BFF** (Backend-For-Frontend). The .NET API, the React page,
the database and the URLs are unchanged.

**Effect on architecture:** none at the system level. The request path is still
Browser → Next.js route → .NET API. Inside the BFF, it changes from "each route has its own
copy of the logic" to "each route *declares* which API path it maps to, and one shared
function does the work". This is the **DRY** principle (Don't Repeat Yourself) and gives
a **single point of change**: to add a header, logging or a timeout to every API call, you
edit one file.

**Behavior after the change** (identical for every route):

| Situation | Before | After |
|---|---|---|
| Not logged in | 500 (crash) on stores routes, 401 on invites routes | **401** `{ title: "Not authenticated", ... }` everywhere |
| .NET API not running | 500 (crash) | **502** `{ title: "API unreachable", ... }` |
| API returns empty body (204, bare 404, ...) | some routes crashed parsing it | status passed through, empty body |
| API returns JSON / ProblemDetails | passed through (most routes) | passed through, same status + content-type |

The page's existing error box already reads `title`/`detail` and shows a **Login** link on
401, so no React changes were needed.

**Small security improvement included:** route `id`s are passed through
`encodeURIComponent()`. Without it, a crafted URL like `/api/stores/1%2F..%2Fdebug%2Fclaims`
gives `id = "1/../debug/claims"`, and the BFF would call a *different* API endpoint than
intended (a path-traversal bug).

**How you could do this yourself, step by step:**

1. **Spot the repetition.** Open two route files side by side (e.g. old
   `invites/mine/route.ts` and `stores/route.ts`). Write down what's the same: get token,
   build `Authorization` header, `fetch(API_BASE + path)`, return the response. And what
   differs: only the **path**, the **HTTP method**, and whether there is a **body**.
   The parts that differ become the function's **parameters**.
2. **Pick the "best" existing copy.** The invites routes already had the right
   `try/catch` around `getAccessToken()` and read the body with `.text()`. Start from that.
3. **Create the helper** in `lib/api-proxy.ts` (`lib/` = shared server code; the `@/` import
   alias points to the project root, see `tsconfig.json` → `paths`). Signature:
   `proxyToApi(path: string, options: { method?, body? } = {})`. The `= {}` default lets
   callers omit options for simple GETs.
4. **Handle each failure explicitly**, in order: no session → 401; network error → 502;
   empty body → pass status only; otherwise pass status + body + content-type.
5. **Convert one route, test it, then the rest.** E.g. `stores/route.ts` becomes
   `return proxyToApi("/stores");`. Hit it with the browser or
   `curl -i http://localhost:3000/api/stores` while logged out and check for 401.
6. **Gate debug routes** with `if (!isDev) return notFound();` before calling the helper.
7. **Run `npx tsc --noEmit`** (type check) and click through the UI while logged in.

**Files:** `lib/api-proxy.ts` (new); rewritten: `app/api/stores/route.ts`,
`app/api/stores/[id]/route.ts`, `app/api/stores/[id]/invites/route.ts`,
`app/api/invites/mine/route.ts`, `app/api/invites/mine/debug/route.ts`,
`app/api/invites/accept/route.ts`, `app/api/debug/claims/route.ts`.
Not changed: `app/api/health/route.ts` (no auth needed), `app/api/profile/route.ts`
(reads the session, doesn't call the API).

**How to verify (done 2026-11-17, logged out, with `curl`):** all nine proxied routes
(GET/POST/PUT/DELETE stores, invites create/mine/mine-debug/accept, debug/claims) return
**401** with the JSON body above. `/api/profile` → `{"loggedIn":false}`.
`tsc` passes; lint dropped from 23 to 20 problems (the rest are older ones in `page.tsx`,
handled in Phase 4). **Not yet tested:** logged-in flows; please click through the app.

---

### Planned (not done): splitting `page.tsx` into components (Phase 4)

Recorded here so it can be reviewed before it happens.

**What it would do:** `app/page.tsx` (520 lines) holds all state, all API calls and all UI in
one component. It would become:
- `lib/client-api.ts`: `fetchJson` + one typed function per endpoint
  (`listStores()`, `createStore(name)`, `inviteMember(...)`, ...).
- `lib/types.ts`: `Store`, `Invitation`, `ApiError` types, matched to what the API really
  returns (fixes B7).
- `components/`: `AuthStatus`, `ErrorBanner`, `StoreList`, `StoreCard`, `InviteForm`,
  `MyInvites`, each receiving data and callbacks via **props**.
- `page.tsx` keeps the shared state (stores, active store, busy, error) and composes the pieces.

**Frontend or backend?** Purely frontend (browser-side React). The API and BFF are untouched.

**Effect on architecture:** introduces the standard React layering:
*page (state + orchestration) → presentational components (render props) → API client
(network)*. Each file has one job, so the analytics screens and the visual redesign
(MVP items 2 and 3) can be added as new components instead of growing one file.

**How you could do it yourself:** cut one visually separate block of JSX (e.g. the red error
box) into `components/ErrorBanner.tsx`; list every variable it reads (`error`) and make those
its props; import it in `page.tsx`; check the page looks the same. Repeat block by block,
moving the fetch functions into `lib/client-api.ts` last.

---

### #005 — 2026-11-17 — Owner decisions for Steps 1–3 (no code changed)

**Revised plan order (local-only MVP):** Step 1 finish team/store features → Step 2 WooCommerce
connection + order sync + analytics endpoints → Step 3 frontend split + Tailwind redesign +
charts → Step 4 README / `.env.example` / tests.

**Decisions:**
1. **Analytics data source:** a real (older) WooCommerce store will be used. Its WooCommerce
   version and whether it uses HTTPS still need to be checked, because both decide which REST
   API version and which authentication method WooPulse must use.
2. **Deleting a store is Owner-only.** Admins can still rename and invite. Implemented in Step 1.
3. **Chart library: Recharts** approved (explained to the owner before installing). Added in
   Step 3 with an exact pinned version.

**Files:** `DEVLOG.md`.

---

### #006 — 2026-11-19 — Target WooCommerce store details (no code changed)

**What:** Recorded the technical facts about the real store used to build analytics.

| Fact | Value | What it means for WooPulse |
|---|---|---|
| WooCommerce version | 4.1.1 (May 2020) | Includes the REST API package (1.0.7), which serves `/wp-json/wc/v1`, `v2` and **`v3`**. WooPulse will use **`wc/v3`**, the same version current WooCommerce uses, so the code won't need changing if the shop is upgraded. |
| Protocol | HTTPS | API keys can be sent with **HTTP Basic auth** (`Authorization: Basic base64(key:secret)`). The OAuth 1.0a signing that plain HTTP requires is **not needed**. |
| Hosting | Plesk-style server (`/var/www/vhosts/...`) | Some PHP setups (CGI/FastCGI) drop the `Authorization` header. If that happens, the fallback is sending `consumer_key` / `consumer_secret` as query-string parameters, which WooCommerce accepts **over HTTPS only**. The sync code will try the header first. |

**Endpoints Step 2 will read (all read-only):**
- `GET /wp-json/wc/v3/orders?after=...&per_page=100&page=N`: orders, fetched in pages of 100.
- `GET /wp-json/wc/v3/products`: product names, for "top products".
- `GET /wp-json/wc/v3/system_status`: connection test ("is the URL + key valid?").

**Rules:**
- The shop URL and keys are **never committed to git**. They are entered in the app per store
  and stored encrypted in the database (ASP.NET Data Protection).
- The WooCommerce key is created with **Read** permission only, so WooPulse can never
  change the shop even if the key leaks.

**Files:** `DEVLOG.md`.

---

### #007 — 2026-11-19 — New users got 403: removed the Auth0 permission requirement + hydration warning fix

**Bug report (owner's manual test of Phase 1):** admin flows worked (log in, create, invite,
rename, delete). A **newly signed-up** user (email + password) got `Request failed (403)`
on page load. After accepting an invite, the store didn't appear.

**Investigation:**
- Database check: invite #9 for store 27 was `Accepted` and a `Member` row for store 27 was
  created. **Accepting worked; the user was in the store.**
- `GET /stores` was protected by `.RequireAuthorization("stores:read")`, which requires the
  Auth0 access token to contain `permissions: ["stores:read"]`. Auth0 RBAC gives new users
  **no permissions**, so the API returned 403 before the store-membership check ran.
  `/invites/accept` only required a login, which is why accepting succeeded.
- The sign-in method was not the cause: a new Google user would get the same 403.

**What changed:** the six store endpoints (`GET /stores`, `GET /stores/{id}`,
`POST /stores`, `PUT /stores/{id}`, `DELETE /stores/{id}`, `GET /stores/deleted`) now use `.RequireAuthorization()`, which means **"any user with a valid Auth0
token"**. The policy definitions in `AddAuthorization` are kept, with a comment explaining
how to turn them back on.

**Why this is safe:** authorization still happens, in one place instead of two:
- **Authentication (unchanged):** Auth0 handles login; the API validates every JWT's
  signature, issuer (your Auth0 tenant) and audience (`https://api.woopulse.local`).
  Without a valid token, every endpoint still returns 401.
- **Authorization (unchanged code):** `StoreService` only returns stores where
  `Members.Any(m => m.UserSub == sub)`, and only Owners/Admins can rename/invite (`CanWrite`).
  A non-member gets 404 (hiding that the store exists).
- **What was removed:** only the "is this user allowed to use stores at all?" switch from
  the Auth0 dashboard. Every signed-up user should be able to create a store anyway.

**Key concept: authentication vs authorization.**
*Authentication* = "who are you?" (Auth0 login → JWT). *Authorization* = "what may you do?".
This app used to answer the second question twice (an Auth0 permission + a store role). Now
it answers it once, with the store role, which is the rule that actually matters in a
multi-tenant app.

**Hydration warning (separate, harmless):** the console showed a mismatch on `<body>`,
caused by the attribute `cz-shortcut-listen="true"`. That attribute is injected by the
**ColorZilla** browser extension, not by WooPulse. Next.js renders HTML on the server;
React then "hydrates" it in the browser and compares the two. The extension changed `<body>`
in between. Fix: `suppressHydrationWarning` on `<body>` in `app/layout.tsx`. It only ignores
attribute differences on that single element, so real mismatches inside the page are still
reported.

**Files:** `apps/api/WooPulse.Api/Program.cs`, `apps/web/woopulse/app/layout.tsx`.

**How to verify:**
- Done: the API compiles (0 warnings, 0 errors; built to a temp folder because the running
  API locks `bin/`). `tsc --noEmit` passes.
- **Needs you:** restart the API (Ctrl+C, then `dotnet run --launch-profile http`), because
  the running one still has the old rules. Reload the page as the new user: the 403 should
  be gone and store 27 should appear. No Auth0 changes or re-login needed. The console
  warning should also be gone (Next.js reloads `layout.tsx` itself).

**Result:** confirmed working by the owner. Committed as `ebf5210` with entries #001–#007.

---

### #008 — 2026-11-19 — Step 1A: fixes to existing store/invite features

**What:** four fixes to features that already existed. No new endpoints (that's Step 1B).

**1. Deleting a store is Owner-only** (`StoreService.SoftDeleteAsync`)
- *Before:* `CanWrite(role)` → Owners **and Admins** could delete.
- *After:* `member.Role != StoreRole.Owner` → 403 with a ProblemDetails body
  `{ title: "Owner only", detail: "Only the store owner can delete a store." }`.
  The UI's error box shows the `detail` text automatically.
- Admins can still rename and invite (those still use `CanWrite`).

**2. Invite lists show the store name and hide expired invites**
(`ListMyInvitesAsync`, `ListMyInvitesDebugAsync`, both DTOs, `page.tsx`)
- *Before:* expired invites stayed visible until the hourly job ran, and the UI showed
  "Store #12".
- *After:* the query also requires `ExpiresAtUtc == null || ExpiresAtUtc > now`, and each
  DTO has a new `StoreName` field.
- *How:* the queries now end in `.Select(i => new Dto(..., i.Store.Name, ...))` **before**
  `.ToListAsync()`. That makes EF generate one SQL query with a JOIN to `Stores` and fetch only
  the needed columns. The old code loaded full rows, then mapped them in memory.
- `InvitationDto` is a **positional record**: its constructor parameters are its properties,
  in order. Adding `StoreName` as the 3rd parameter meant updating every `new InvitationDto(...)`
  call, and the compiler points out any you miss.
- Frontend types fixed too: `role`/`status` are `string` (the API sends `"Member"`, not `3`).

**3. Accepting an invite is race-safe** (`AcceptInviteAsync`)
- *Problem:* two accept requests at the same moment (double-click, two tabs) could both read
  `Status == Pending`. The transaction didn't prevent that, because Postgres's default
  isolation level (*Read Committed*) lets both see the old value. Result: an unhandled 500
  from the unique index, or two "successful" accepts.
- *Fix:* **atomic conditional update** (a common pattern, sometimes called "claiming" a row):
  ```csharp
  var claimed = await _db.StoreInvitations
      .IgnoreQueryFilters()
      .Where(i => i.Id == inv.Id && i.Status == InvitationStatus.Pending)
      .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, InvitationStatus.Accepted) ...);
  if (claimed == 0) return 409;
  ```
  `ExecuteUpdateAsync` (EF Core 7+) runs **one** `UPDATE ... WHERE Status = Pending` directly in
  the database, without loading the entity. The database guarantees only one request can
  change that row from Pending; the second waits for the lock, re-checks the `WHERE`, and
  updates 0 rows → **409 "This invite was already used."**
- If we return early, `CommitAsync` never runs; `await using var tx` disposes the transaction
  at the end of the method, which **rolls back** automatically.
- The early checks (not found / not pending / expired / wrong email) are unchanged. They
  give friendly error messages; the conditional update is the real guarantee.

**4. Heartbeat job prints the real time** (`Program.cs`, `MaintenanceService.Heartbeat`)
- *Problem:* Hangfire saves a job as *which method to call + its arguments, serialized to
  JSON*. In `() => Console.WriteLine($"...{DateTimeOffset.Now}")` the argument is the finished
  string, built **once** at registration, so every run printed the same timestamp.
- *Fix:* the job calls `svc.Heartbeat()`, a method with **no arguments** that reads the clock
  when it runs. **Rule of thumb:** with Hangfire, compute "now" *inside* the job, never in
  the expression that registers it.

**Files:** `Services/StoreService.cs`, `Services/MaintenanceService.cs`, `Program.cs`,
`Dtos/InvitationDtos.cs`, `Dtos/InvitationDebugDtos.cs`, `apps/web/woopulse/app/page.tsx`.

**How to verify:**
- Done: API compiles (0 warnings, 0 errors), `tsc --noEmit` passes.
- **Needs you** (restart the API first):
  1. As an **Admin** member (not Owner) of a store, click Delete → red box "Only the store
     owner can delete a store." As the Owner → deletes as before.
  2. Invite someone → as that user, "Refresh invites (debug)" shows the **store name**.
  3. Accept → store appears. Click Accept again on a stale button → 409 "This invite is no
     longer available." (that's the early check; the "already used" message only appears
     when two requests arrive at the *same instant*, which is hard to trigger by hand).
  4. API console: the heartbeat line shows a **different time each minute**.

**Result:** confirmed working by the owner. Committed as `38fc42d`.

---

### #009 — 2026-11-19 — `Program.cs` split into Setup/ and Endpoints/ + `StoreMember.Email`

**What:** `Program.cs` went from ~390 lines to ~60. All service configuration moved to
`Setup/`, all endpoints moved to `Endpoints/` (one file per feature), and the identity
helpers moved to `Security/`. **The HTTP routes and their behavior are unchanged.** Also
added an `Email` column to `StoreMember` so the upcoming members list is readable.

**Why:** the owner's direction: a minimal `Program.cs` and one responsibility per file
(DDD-inspired, applied pragmatically). It also makes Step 1B (7 new endpoints) and Step 2
(analytics) land in their own files instead of growing one big file.

**New layout of `apps/api/WooPulse.Api`:**
```
Program.cs                   ← table of contents: services → middleware → jobs → endpoints
Setup/
  DatabaseSetup.cs           AddWooPulseDatabase()   EF Core + Npgsql
  AuthSetup.cs               AddWooPulseAuth()       Auth0 JWT validation + (unused) policies
  HangfireSetup.cs           AddWooPulseHangfire(), UseWooPulseHangfireDashboard(),
                             RegisterWooPulseRecurringJobs()
  AppServicesSetup.cs        AddWooPulseServices()   StoreService, MaintenanceService, CORS
Endpoints/
  SystemEndpoints.cs         /, /health, /me
  StoreEndpoints.cs          /stores, /stores/{id}, /stores/deleted
  InviteEndpoints.cs         /stores/{id}/invites, /invites/mine, /invites/accept
  DevEndpoints.cs            /debug/claims, /invites/mine/debug, /jobs/*  (Development only)
Security/
  ClaimsPrincipalExtensions.cs   user.GetSub(), user.GetEmail()
Data/ Dtos/ Services/ Validation/ Migrations/   (unchanged structure)
```

**The C# techniques used (each one is standard ASP.NET Core practice):**

1. **Extension methods.** `public static IServiceCollection AddWooPulseAuth(this IServiceCollection services, ...)`.
   The `this` keyword on the first parameter lets you call a static method as if it were
   an instance method: `builder.Services.AddWooPulseAuth(...)`. That's exactly how
   Microsoft's own `AddAuthentication()` and `AddHangfire()` are written.
2. **Route groups.** `var group = app.MapGroup("/stores").RequireAuthorization();`. Every route
   on `group` gets the `/stores` prefix and the auth requirement, declared once.
3. **Binding `ClaimsPrincipal` directly.** Handlers now take `ClaimsPrincipal user` instead of
   `HttpContext ctx`. ASP.NET fills it with the logged-in user. Asking only for what you need
   makes a handler's dependencies obvious.
4. **Dev endpoints mapped conditionally.** `if (app.Environment.IsDevelopment()) app.MapDevEndpoints();`
   Before, every debug handler checked `IsDevelopment()` itself, and a new route could forget
   to. Now, outside dev, those routes are never registered at all.
5. **`return error ?? Results.Ok(dto);`**. `??` (null-coalescing) returns the left side if it
   isn't null, otherwise the right. It replaces the two-line `if (error is not null) return error;`.

**Bug fixed (introduced by #003, never triggered locally):** recurring jobs were registered
with Hangfire's *static* `RecurringJob.AddOrUpdate`. That needs a global `JobStorage.Current`,
which in this app was only set as a side effect of `UseHangfireDashboard`. #003 made the
dashboard Development-only, so **outside Development the API would have crashed at
startup.** Now `RegisterWooPulseRecurringJobs()` asks DI for `IRecurringJobManager`, which
is always wired to the Postgres storage. Lesson: avoid static/global state; take
dependencies from DI so they're explicit.

**Removed:** the `/weatherforecast` template endpoint and its `WeatherForecast` record
(finding C1); `WooPulse.Api.http` now calls `/health`. Also dropped one duplicate claim
lookup in `GetEmail` (`ClaimTypes.Email` *is* the `http://schemas.xmlsoap.org/...` URI).

**Small ordering change:** `UseHttpsRedirection()` now runs right after the exception
handler (the conventional spot) instead of after the job registration. No effect locally:
the `http` launch profile has no HTTPS port, so it does nothing.

**`StoreMember.Email`:**
- `Data/StoreMember.cs`: new `string? Email` (nullable: old rows have none yet).
- Filled when a store is **created** (`CreateAsync(sub, email, req)`, owner's email from the token)
  and when an invite is **accepted** (`normEmail`, already verified to match the invite).
- **Backfill for existing rows:** `StoreService.SyncMemberEmailAsync(sub, email)` runs on every
  `GET /stores`. It's a single `UPDATE ... WHERE UserSub = @sub AND (Email IS NULL OR Email <> @email)`,
  so it normally changes 0 rows. It also keeps emails right if a user changes theirs in Auth0.
- Migration: `AddStoreMemberEmail` (adds one nullable `text` column; no data loss).

**Files:** `Program.cs` (rewritten), new `Setup/*.cs` (4), new `Endpoints/*.cs` (4), new
`Security/ClaimsPrincipalExtensions.cs`, `Data/StoreMember.cs`, `Services/StoreService.cs`,
`WooPulse.Api.http`, `Migrations/*_AddStoreMemberEmail*` (generated).

**How to verify:**
- Done: API compiles (0 warnings, 0 errors).
- Pending: generate + apply the migration (needs the running API stopped, because it locks `bin/`).
- **Needs you:** restart the API; click through the whole app as before (list, create, rename,
  delete, invite, accept). Behavior should be identical. `/hangfire` and `/swagger` still work.

**Result:** the owner ran `dotnet ef migrations add AddStoreMemberEmail` and
`dotnet ef database update` and confirmed the app works. Checked: migration
`20261119164051_AddStoreMemberEmail` is in `__EFMigrationsHistory`, `StoreMembers.Email`
exists and is nullable, and the backfill already filled 8 of 12 rows.

---

### #010 — 2026-11-19 — Step 1B: team management, invite revoke, restore deleted stores

**What:** the remaining team/store features of MVP goal 1, end to end
(API → BFF route → UI).

**New API endpoints**

| Method + route | Who | Service method |
|---|---|---|
| `GET /stores/{storeId}/members` | any member | `MemberService.ListAsync` |
| `PUT /stores/{storeId}/members/{memberId}` body `{ role: 2\|3 }` | Owner; not on the Owner | `MemberService.ChangeRoleAsync` |
| `DELETE /stores/{storeId}/members/{memberId}` | Owner: anyone but self · Admin: Members only | `MemberService.RemoveAsync` |
| `POST /stores/{storeId}/leave` | anyone but the Owner | `MemberService.LeaveAsync` |
| `GET /stores/{storeId}/invites` | Owner/Admin | `StoreService.ListStoreInvitesAsync` |
| `POST /stores/{storeId}/invites/{inviteId}/revoke` | Owner/Admin | `StoreService.RevokeInviteAsync` |
| `POST /stores/{id}/restore` | Owner | `StoreService.RestoreAsync` |

**Status codes used, and why:**
- **404** when you're not a member, or the thing doesn't exist. We never answer 403 to
  outsiders, because a 403 would confirm the store exists.
- **403** when you *are* a member but your role doesn't allow it (with a `detail` message).
- **400** for invalid input or impossible actions (remove yourself, change the Owner's role).
- **409** for a state conflict (Owner tries to leave; invite no longer pending).
- **204 No Content** for successful actions that return nothing (remove, leave, revoke).

**Design decisions:**
1. **New `MemberService`** (`Services/MemberService.cs`) instead of growing `StoreService`.
   Team rules are their own concern inside the Store domain (the DDD idea: group code by
   business concept). Invite and restore methods stayed in `StoreService`, next to the
   invite/delete code they mirror.
2. **`db.FindMembershipAsync(storeId, sub)`** (`Data/MembershipQueries.cs`), an extension
   method on `AppDbContext`. Nearly every rule starts with "what's my membership in this
   store?", so it's defined once. Because of the query filter it returns `null` for
   soft-deleted stores, so those automatically 404.
3. **Revoke uses the conditional-UPDATE pattern** from #008:
   `WHERE Id = @id AND Status = Pending`. An invite accepted a split second earlier can't be
   flipped to Revoked; you get a 409 instead.
4. **Revoke is `POST .../revoke`, not `DELETE`**: the row isn't deleted, its status changes
   and it stays as an audit record. REST convention: use DELETE only when the resource is
   actually removed.
5. **Restore** loads with `IgnoreQueryFilters()` (deleted stores are hidden by default), checks
   Owner, clears the `Deleted*` fields and records `UpdatedAtUtc/By`. After the 30-day
   purge job runs, the store is gone and restore returns 404.
6. **The Owner can't leave**, so a store always has an owner. Ownership transfer is out of
   MVP scope.

**C# syntax introduced:**
- `newRole is not (StoreRole.Admin or StoreRole.Member)`: pattern matching with `or`/`not` (C# 9).
- `me.Role switch { StoreRole.Owner => true, StoreRole.Admin => ..., _ => false }`: a switch
  *expression* that returns a value; `_` means "anything else".
- `condition ? a : b`: ternary operator (in `RevokeInviteAsync`).
- `public MemberService(AppDbContext db) => _db = db;`: expression-bodied constructor.
- Route parameter in a group prefix: `MapGroup("/stores/{storeId:int}/members")` binds
  `int storeId` in every handler of the group.

**BFF routes** (`app/api/stores/...`, each a few lines using `proxyToApi`):
`deleted/route.ts` (GET), `[id]/members/route.ts` (GET), `[id]/members/[memberId]/route.ts`
(PUT, DELETE), `[id]/leave/route.ts` (POST), `[id]/invites/route.ts` (added GET),
`[id]/invites/[inviteId]/revoke/route.ts` (POST), `[id]/restore/route.ts` (POST).
Next.js routing note: the static folder `deleted/` wins over the dynamic `[id]/`, so
`/api/stores/deleted` never reaches the `[id]` route.

**UI (first real components, MVP goal 3 groundwork):**
- `lib/client-api.ts`: `fetchJson` and `ApiError` moved out of `page.tsx`, plus a new
  `toApiError(e: unknown)`. It replaces `catch (e: any)` with a typed check, which is the
  lint-clean way to handle errors in TypeScript.
- `components/TeamPanel.tsx`: members of the active store (role dropdown, Remove, Leave)
  and pending invites (Revoke). It's **self-contained**: it fetches its own data from
  `storeId`. Buttons are shown according to the same rules as the API, but **hiding a button
  is UX, not security**; the API enforces the rules regardless.
- `components/TrashPanel.tsx`: deleted stores with "permanently deleted in N days" and
  Restore (shown only for stores you own). It renders nothing when the trash is empty.
- `page.tsx` changes:
  - imports `fetchJson` from `lib/`;
  - renders the two panels;
  - new `dataVersion` counter, bumped after each successful `load()`. The panels receive it as
    `refreshKey` and reload when it changes. It's a simple way for a parent to tell
    self-contained children "data changed".
  - **bug fix:** the active store now resets to the first store if it disappears from the
    list (after you leave or delete it). Before, the old id stayed selected. It uses the
    functional form `setActiveStoreId(current => ...)`, which reads the latest state
    instead of the value captured when `load` was created (a "stale closure").

**React concepts used:** `useCallback` keeps `load` the same function between renders
(unless `storeId` changes), so `useEffect(..., [load, refreshKey])` re-runs only when it
should and doesn't loop.

**Files:** API: new `Dtos/MemberDtos.cs`, `Data/MembershipQueries.cs`,
`Services/MemberService.cs`, `Endpoints/MemberEndpoints.cs`; changed
`Services/StoreService.cs`, `Endpoints/StoreEndpoints.cs`, `Endpoints/InviteEndpoints.cs`,
`Setup/AppServicesSetup.cs`, `Program.cs`. Web: new `lib/client-api.ts`,
`components/TeamPanel.tsx`, `components/TrashPanel.tsx`, 6 new route files + 1 changed; `app/page.tsx`.
No database migration needed.

**How to verify:**
- Done: API compiles (0 warnings, 0 errors); `tsc` passes; the new files have no lint
  problems (total lint dropped 20 → 18, all in older `page.tsx`/`profile` code).
  Logged out, all 8 new BFF routes return 401 (they exist and require login).
- **Needs you** (restart the API; two browsers or a normal + incognito window help):
  1. Owner: Team panel lists members with emails; change a member to Admin and back.
  2. Owner: Remove a member. Admin: try removing another Admin → no button (and the API
     would 403). Admin can remove a Member.
  3. Member: "Leave store" → the store disappears from their list. Owner: no Leave button.
  4. Owner/Admin: invite someone → it shows under "Pending invites" → Revoke → it
     disappears, and the invitee can no longer accept it.
  5. Owner: delete a store → it shows in Trash with "permanently deleted in 30 days" →
     Restore → it's back in the list.
