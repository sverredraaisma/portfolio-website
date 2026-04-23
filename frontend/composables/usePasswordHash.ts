/**
 * Hash the user's password before it leaves the browser.
 * The server then re-hashes this digest with Argon2 + a per-user salt.
 * The raw password never touches the network.
 */
export async function hashPasswordForTransit(password: string): Promise<string> {
  const enc = new TextEncoder().encode(password)
  const digest = await crypto.subtle.digest('SHA-256', enc)
  return Array.from(new Uint8Array(digest))
    .map(b => b.toString(16).padStart(2, '0'))
    .join('')
}
