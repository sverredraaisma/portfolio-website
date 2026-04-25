/**
 * Validates a `?redirect=` query value so the login page (and similar
 * post-login pages) can't be tricked into open-redirecting off-site.
 *
 * Accept only same-site, single-path strings:
 *   - must start with "/"
 *   - second char must NOT be "/" or "\" (protocol-relative URLs and
 *     backslash-tricked URLs that some browsers normalise to "//")
 *   - length-bounded so a runaway query string can't flood the URL bar
 *
 * Returns the validated string (which may be safely passed to
 * `router.push`) or `null` to indicate the caller should fall back to
 * its default destination.
 */
export function safeRedirect(raw: unknown): string | null {
  if (typeof raw !== 'string') return null
  if (raw.length < 1 || raw.length > 1024) return null
  if (raw[0] !== '/') return null
  if (raw[1] === '/' || raw[1] === '\\') return null
  return raw
}
