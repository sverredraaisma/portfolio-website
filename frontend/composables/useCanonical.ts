/**
 * Builds an absolute canonical URL for the given path. Pages should call this
 * and pipe the result into `useHead({ link: [{ rel: 'canonical', href: ... }] })`
 * (and into og:url where appropriate).
 *
 * Origin precedence:
 *   1. `runtimeConfig.public.siteOrigin` if set — pinned at deploy time so a
 *      hostile Host header can't poison the rendered canonical.
 *   2. The SSR request's Host header — dev-mode fallback when no siteOrigin
 *      is configured.
 *   3. `window.location.origin` on client navigation.
 *   4. The path alone — last resort, better than emitting a wrong absolute URL.
 *
 * `path` MUST start with "/". A trailing "/" on the configured siteOrigin is
 * stripped so the join doesn't double up.
 */
export function useCanonical(path: import('vue').MaybeRefOrGetter<string | undefined>) {
  const config = useRuntimeConfig()
  return computed(() => {
    const p = typeof path === 'function' ? path() : (path as any)?.value ?? path
    if (!p || typeof p !== 'string' || !p.startsWith('/')) return undefined

    const configured = config.public.siteOrigin as string | undefined
    if (configured) return `${configured.replace(/\/$/, '')}${p}`

    if (import.meta.server) {
      const event = useRequestEvent()
      if (event) {
        const proto = event.node.req.headers['x-forwarded-proto'] || 'http'
        const host = event.node.req.headers.host
        if (host) return `${proto}://${host}${p}`
      }
    } else if (typeof window !== 'undefined') {
      return `${window.location.origin}${p}`
    }
    return p
  })
}
