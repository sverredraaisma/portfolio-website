# Portfolio Website

A personal portfolio site for posting about projects and sharing information about myself in a fun way.

## Stack

- **Frontend** — Nuxt 3 (Vue 3, TypeScript)
- **Backend** — C# / .NET 8 RPC API
- **Database** — PostgreSQL
- **Auth** — OAuth 2.0 style flow, password hashed on the client, re-hashed with a salt server-side, email verification via JWT link

## Layout

```
.
├── backend/           # .NET RPC API
├── frontend/          # Nuxt site
├── docker-compose.yml # Postgres for local dev
└── CLAUDE.md          # Notes for Claude Code
```

## Features

### Content
- **Post builder** — Compose posts from blocks (header, text, image, code). The block tree is serialised to JSON and stored in Postgres so new block types only need a renderer added on the frontend. Drag-and-drop block reordering with up/down keyboard fallback.
- **Markdown** in text blocks (rendered via `marked` + `DOMPurify`); **syntax highlighting** in code blocks via `highlight.js`.
- **Image pipeline** — Uploads converted to WebP server-side to save storage.
- **Tags + search** — Posts carry a `text[]` of tags (GIN-indexed); search box on `/posts` does a case-insensitive title match, both URL-synced.
- **Terminal-style comments** — Rendered as a scrollback; the input is the prompt. Edit own comments, admins can delete any (moderation queue at `/admin/comments`).
- **RSS feed** at `/rss.xml`, plus `sitemap.xml` and `robots.txt` — all auto-discovered.
- **Retro landing page** with CRT vibes; theme toggle (cyan accent, light/dark).
- **Keyboard shortcuts** (g+letter to navigate, `/` to focus search, `?` for help).

### Auth
- **Passwordless login** via WebAuthn passkeys (FIDO2 / Fido2NetLib).
- **Password fallback** with SHA-256 client-hash + Argon2id+salt server-side.
- **TOTP 2FA** with single-use SHA-256-hashed recovery codes; QR enrolment.
- **Per-username brute-force throttle** (5 fails / 15 min) on top of the IP-based limiter.
- **Email verification** with sent-at + expiry shown to the user, resend any time.
- **Email change** flow with re-verification; both old + new addresses notified.
- **"Was this you?"** email alerts on every sensitive account action; audit log on `/account`.

### Privacy (AVG / GDPR)
- Self-service **data export** (JSON download) and **account delete** (with anonymise/delete choice for comments) from `/account`.
- Full **privacy policy** at `/privacy`. No analytics, no third-party trackers.

### Crypto
- **Falcon-512** post-quantum signing identity. Admin signs statements at `/sign`; anyone verifies at `/verify-statement`.

## Testing

```bash
# Backend — xUnit + FluentAssertions; SQLite-in-memory for integration tests
cd backend && dotnet test

# Frontend — Vitest + happy-dom for composable tests
cd frontend && pnpm test
```

CI runs both on every push to `main` and every PR (see `.github/workflows/ci.yml`).

Copy `.env.example` to `.env` and fill in `JWT_KEY` (and `PASSKEY_RP_ID` for non-localhost deployments) before running `docker compose up`.

## Getting started

### Option A — everything in Docker

```bash
JWT_KEY=$(openssl rand -base64 48) docker compose up --build
```

The whole site lives behind a single nginx reverse proxy on `http://localhost`:

- `/` → Nuxt frontend
- `/api/*` → .NET backend (so `POST /api/rpc`, `GET /api/media/...`)
- `http://localhost:8025` → Mailpit web UI for catching outbound mail

Same-origin means **no CORS configuration to maintain**. Backend and frontend containers are not exposed to the host directly — if you need to poke the backend from outside, add a `ports: ["5080:8080"]` block under `backend` temporarily.

`PUBLIC_PORT` overrides the host port (defaults to 80). `PUBLIC_ORIGIN` overrides the URL embedded in verification / reset emails (defaults to `http://localhost`).

### Option B — local dev (hot reload)

Prerequisites: Node 20+, .NET 8 SDK, Docker (for Postgres + Mailpit).

```bash
docker compose up -d postgres mailpit
Jwt__Key=$(openssl rand -base64 48) dotnet run --project backend/src/PortfolioApi
cd frontend && npm install && NUXT_PUBLIC_API_BASE=http://localhost:5080 npm run dev
```

In dev mode the frontend hits the backend directly on `:5080` and CORS *does* apply — `Cors:FrontendOrigin` defaults to `http://localhost:3000`, override via the `Cors__FrontendOrigin` env var if you run Nuxt elsewhere. Note that `dotnet run` defaults Kestrel to `http://localhost:5080` per `appsettings.json`.

## Auth flow

1. Register: client generates `sha256(password)` and sends `{ email, username, clientHash }`.
2. Server generates a salt, stores `argon2(clientHash, salt)`.
3. Server creates a JWT verification token, emails it as a link to the user's address.
4. User clicks the link → frontend POSTs the token to the backend → email marked verified.
5. Login: client sends `{ username, sha256(password) }`. Server re-derives `argon2` and compares.
6. On success, server returns an access JWT (short-lived) and refresh token.

The client never sends the raw password. The server never sees the raw password. The DB never holds the raw password or the client-side hash.
