# Build stage
FROM node:20-alpine AS build
WORKDIR /app

# Use pnpm to honour pnpm-lock.yaml — npm and pnpm produce different lock
# formats, and the project lockfile is pnpm's. Falls back to a fresh resolve
# if the lockfile is missing.
RUN corepack enable && corepack prepare pnpm@10.33.0 --activate

COPY package.json pnpm-lock.yaml* ./
RUN pnpm install --frozen-lockfile

COPY . .
RUN pnpm run build

# Runtime stage
FROM node:20-alpine AS runtime
WORKDIR /app

ENV NODE_ENV=production
ENV NITRO_PORT=3000
ENV NITRO_HOST=0.0.0.0

COPY --from=build /app/.output ./.output

EXPOSE 3000
CMD ["node", ".output/server/index.mjs"]
