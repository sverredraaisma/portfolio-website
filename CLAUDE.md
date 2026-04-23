# CLAUDE.md

Notes for Claude Code working in this repository.

## What this project is

A personal portfolio website. Two deployables:

- `frontend/` — Nuxt 3 (Vue, TypeScript). Retro-styled landing page, post viewer, post builder, terminal-style comment thread.
- `backend/` — C# / .NET 8 RPC API. EF Core on PostgreSQL.

A single Postgres instance serves the backend (see `docker-compose.yml`).

## Conventions

### RPC pattern
The backend is an RPC API, not REST. All client calls go to a single endpoint (`POST /rpc`) with a body of the form:

```json
{ "method": "posts.create", "params": { ... } }
```

Responses are `{ "result": ... }` or `{ "error": { "code": ..., "message": ... } }`. Methods live in `backend/src/PortfolioApi/Rpc/Methods/` and are registered in `RpcRouter`. To add a new method, add a handler and register it — no new route, no new controller.

### Auth
- The client hashes the password with SHA-256 before sending. The server treats the client hash as the "password" and re-hashes it with Argon2id + a per-user salt for storage. **Never** log either hash, and never accept a raw password from the client.
- Email verification uses a JWT signed with the same key as access tokens but with a `purpose: "email-verify"` claim and a longer expiry. The verify endpoint accepts the token, validates the purpose claim, and flips `EmailVerifiedAt`.
- Access tokens are short-lived (15 min). Refresh tokens are stored hashed in the DB.

### Posts
Post bodies are a JSON document of blocks:

```json
{ "blocks": [
  { "type": "header", "id": "...", "data": { "text": "Hi", "level": 1 } },
  { "type": "text",   "id": "...", "data": { "markdown": "..." } },
  { "type": "image",  "id": "...", "data": { "src": "/media/abc.webp", "alt": "..." } }
] }
```

The backend stores this as a `jsonb` column. To add a block type:
1. Add a TS interface in `frontend/types/blocks.ts`.
2. Add a renderer component in `frontend/components/blocks/`.
3. Add an editor component in `frontend/components/builder/`.
4. Register it in `frontend/composables/useBlocks.ts`.

The backend does not need to know about new block types unless they have server-side concerns (e.g. images need upload handling).

### Images
Uploaded images go through `ImageService.ConvertToWebpAsync` and are stored under `backend/media/`. The original is discarded. Use `Image.Load` from `SixLabors.ImageSharp` and save with `WebpEncoder`. Default quality 80.

### Comments
Comments are flat (no threading), ordered by creation time. The frontend renders them as a terminal scrollback; the input is the prompt line. Keep the API minimal: `comments.list`, `comments.create`, `comments.delete` (own only).

## Running locally

```bash
docker compose up -d                                        # postgres
dotnet run --project backend/src/PortfolioApi               # api on :5080
cd frontend && npm run dev                                  # nuxt on :3000
```

Migrations: `dotnet ef migrations add <Name> --project backend/src/PortfolioApi`.

## What not to do

- Don't add REST routes alongside the RPC endpoint — pick one pattern and stick with it.
- Don't store the original (non-WebP) image. The conversion is the point.
- Don't accept raw passwords on the wire. If a request looks like a raw password, reject it.
- Don't add server-side rendering for the post builder page — it's an authenticated editor, keep it client-only.
