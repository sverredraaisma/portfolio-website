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
Comments are flat (no threading), ordered by creation time. The frontend renders them as a terminal scrollback; the input is the prompt line. Keep the API minimal: `comments.list`, `comments.create`, `comments.delete` (own comments, or any if admin).

### Signing
The site holds a Falcon-512 (NIST PQC) keypair generated on first boot at `backend/keys/`. The admin can sign arbitrary statements at `/sign`; visitors verify them at `/verify-statement`. RPCs:

- `signing.publicKey` (public) — algorithm, base64 public key, SHA-256 fingerprint.
- `signing.sign` (admin) — signs a statement, returns the full envelope.
- `signing.verify` (public) — verifies `{ statement, signatureBase64, publicKeyBase64? }`. When `publicKeyBase64` is omitted the site's current key is used.

On-disk format is PKCS#8 / SubjectPublicKeyInfo (via `Pqc*Factory`) so files survive BouncyCastle version bumps. Wire format is the raw 896-byte Falcon-512 public key. **Never** read or copy `keys/falcon.priv` outside the backend container.

### Site policy snapshots
The privacy policy at `/privacy` is signed and downloadable as proof of what the site committed to on a given date. The flow has two sources of truth that **must** stay in sync when the policy text changes:

- `backend/src/PortfolioApi/Resources/privacy-policy.txt` — the canonical plain text. Must contain a `Last-Updated: YYYY-MM-DD` header line. The signing service signs these exact bytes at startup and caches the signature for the process lifetime.
- `frontend/pages/privacy.vue` — the rich HTML rendering. The page also fetches the canonical text via `policy.privacy` and shows it in a `<details>` block at the bottom, so visitors can see what was signed.

When the policy changes: edit both files, bump `Last-Updated:` in the .txt file. Restart the backend so the cached signature regenerates.

RPC: `policy.privacy` (public) returns `{ subject, text, lastUpdated, algorithm, signatureBase64, publicKeyBase64, publicKeyFingerprint }`. The frontend's "Download signed snapshot" button bundles this into a JSON file the visitor can later re-verify at `/verify-statement`.

## Running locally

Full stack (everything behind nginx on `http://localhost`):

```bash
JWT_KEY=$(openssl rand -base64 48) docker compose up --build
```

Hot-reload dev mode (backend + frontend on the host, only postgres in docker):

```bash
docker compose up -d postgres mailpit
Jwt__Key=$(openssl rand -base64 48) dotnet run --project backend/src/PortfolioApi  # :5080
cd frontend && NUXT_PUBLIC_API_BASE=http://localhost:5080 npm run dev               # :3000
```

In docker mode the browser sees a single origin and CORS is moot. In dev mode the browser hits `:5080` cross-origin, so the backend's CORS policy needs to allow the dev frontend's origin (default `http://localhost:3000`).

Migrations: generated via the SDK image so you don't need a local .NET SDK:

```bash
docker run --rm -v "$(pwd)/backend:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  bash -c 'dotnet tool install -g dotnet-ef --version 8.0.11 >/dev/null && \
           export PATH=$PATH:/root/.dotnet/tools && \
           dotnet ef migrations add <Name> --project src/PortfolioApi'
```

`AppDbContextFactory` exists so EF tools can scaffold without bootstrapping the host (which now refuses to start without `Jwt:Key`). Don't delete it.

## What not to do

- Don't add REST routes alongside the RPC endpoint — pick one pattern and stick with it.
- Don't store the original (non-WebP) image. The conversion is the point.
- Don't accept raw passwords on the wire. If a request looks like a raw password, reject it.
- Don't add server-side rendering for the post builder page — it's an authenticated editor, keep it client-only.
