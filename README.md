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

- **Post builder** — Compose posts from blocks (header, text, image). The block tree is serialised to JSON and stored in Postgres so new block types only need a renderer added on the frontend.
- **Image pipeline** — Uploaded images are converted to WebP server-side to save storage.
- **Terminal-style comments** — Each post has a comment section rendered as a terminal log; the prompt line is where you type a new comment.
- **Retro landing page** — The home page leans into a retro look (CRT vibes, mono type, scanlines).

## Getting started

### Prerequisites
- Node 20+
- .NET 8 SDK
- Docker (for the local Postgres)

### Database
```bash
docker compose up -d
```

### Backend
```bash
cd backend
dotnet restore
dotnet ef database update --project src/PortfolioApi
dotnet run --project src/PortfolioApi
```

### Frontend
```bash
cd frontend
npm install
npm run dev
```

The frontend runs on `http://localhost:3000` and talks to the backend on `http://localhost:5080` by default. Override with `NUXT_PUBLIC_API_BASE`.

## Auth flow

1. Register: client generates `sha256(password)` and sends `{ email, username, clientHash }`.
2. Server generates a salt, stores `argon2(clientHash, salt)`.
3. Server creates a JWT verification token, emails it as a link to the user's address.
4. User clicks the link → frontend POSTs the token to the backend → email marked verified.
5. Login: client sends `{ username, sha256(password) }`. Server re-derives `argon2` and compares.
6. On success, server returns an access JWT (short-lived) and refresh token.

The client never sends the raw password. The server never sees the raw password. The DB never holds the raw password or the client-side hash.
