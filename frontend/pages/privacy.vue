<script setup lang="ts">
useSeoMeta({
  title: 'Privacy policy',
  description: 'How this site collects, uses, and protects your personal data, and how to exercise your rights under the AVG / GDPR.'
})

type PolicySnapshot = {
  subject: string
  text: string
  lastUpdated: string
  algorithm: string
  signatureBase64: string
  publicKeyBase64: string
  publicKeyFingerprint: string
}

const rpc = useRpc()

// Fetch the canonical signed snapshot via SSR so the page (and any "View
// Source" save) already includes the signature. The cached server-side
// signature is byte-stable across visitors, so two readers who save on the
// same day get the same signature bytes — which makes "I saved this on
// date X" claims unambiguous.
const { data: snapshot } = await useAsyncData<PolicySnapshot>(
  'policy:privacy',
  () => rpc.call<PolicySnapshot>('policy.privacy')
)

// "Last updated" mirror for the human-readable header. Falls back to a
// hard-coded date if the snapshot fetch failed (network blip during SSR);
// the canonical signed text below is the source of truth.
const lastUpdated = computed(() => snapshot.value?.lastUpdated ?? '2026-04-24')

const sig = computed(() => snapshot.value?.signatureBase64 ?? '')
const sigShort = computed(() => sig.value
  ? sig.value.slice(0, 24) + '…' + sig.value.slice(-12)
  : '')

const copyState = ref<'idle' | 'copied' | 'error'>('idle')
async function copySignature() {
  if (!sig.value) return
  try {
    await navigator.clipboard.writeText(sig.value)
    copyState.value = 'copied'
    setTimeout(() => { if (copyState.value === 'copied') copyState.value = 'idle' }, 1500)
  } catch {
    copyState.value = 'error'
  }
}

// Build a self-contained JSON proof bundle and trigger a download. The
// shape mirrors what /verify-statement already accepts so a saver can
// paste `text` and `signatureBase64` straight in. Including the public
// key + fingerprint makes the bundle verifiable offline against any key
// the saver later wants to anchor to.
function downloadSnapshot() {
  if (!snapshot.value) return
  const bundle = {
    site: 'sverre.dev',
    subject: snapshot.value.subject,
    lastUpdated: snapshot.value.lastUpdated,
    savedAt: new Date().toISOString(),
    text: snapshot.value.text,
    algorithm: snapshot.value.algorithm,
    publicKeyBase64: snapshot.value.publicKeyBase64,
    publicKeyFingerprint: snapshot.value.publicKeyFingerprint,
    signatureBase64: snapshot.value.signatureBase64
  }
  const json = JSON.stringify(bundle, null, 2)
  // Memory-only Blob URL; revoke after click so a screen reader navigating
  // back to the page doesn't accumulate them.
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = `sverre.dev-privacy-${snapshot.value.lastUpdated}.json`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <article class="max-w-3xl mx-auto px-6 py-10 prose-invert space-y-6 text-sm leading-relaxed">
    <header>
      <h1 class="text-2xl text-cyan-400">$ cat privacy.txt</h1>
      <p class="text-xs text-zinc-500 mt-1">Last updated: {{ lastUpdated }}</p>
    </header>

    <p>
      This page describes how this website ("the site") handles your personal data, in line with the
      Algemene Verordening Gegevensbescherming (AVG) — the Dutch implementation of the EU General
      Data Protection Regulation (GDPR). If anything below is unclear, contact the data controller
      using the address at the bottom.
    </p>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">1. Who is the data controller</h2>
      <p>
        Sverre Draaisma is the natural person responsible for this site and acts as data controller
        within the meaning of art. 4(7) AVG. Contact:
        <a href="mailto:sverre@draaisma.dev" class="hover:text-cyan-400 underline">sverre@draaisma.dev</a>.
      </p>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">2. What data is collected, and why</h2>
      <ul class="list-disc list-inside space-y-2">
        <li>
          <strong>Account profile.</strong> Username, email address, account creation timestamp,
          email-verification timestamp, and a per-user random salt. Required to operate the
          account (art. 6(1)(b) AVG — performance of a contract).
        </li>
        <li>
          <strong>Password material.</strong> Your password is hashed in the browser with SHA-256
          before transit; the server then re-hashes that value with Argon2id and a per-user salt
          and stores only the result. The plaintext password never leaves your device.
        </li>
        <li>
          <strong>Sessions.</strong> Refresh tokens are stored as SHA-256 hashes (so leaked
          database backups cannot be used as session tokens). Each row records when it was
          created, when it expires, and when it was revoked.
        </li>
        <li>
          <strong>Posts and comments.</strong> Content you submit is stored verbatim and shown
          publicly under your username (or as "anonymous" if you have anonymised it).
        </li>
        <li>
          <strong>IP address.</strong> Held only in volatile memory by the rate-limiter to
          throttle abusive request patterns. Not logged or persisted (art. 6(1)(f) AVG —
          legitimate interest in keeping the service available).
        </li>
        <li>
          <strong>Email delivery.</strong> Verification and password-reset emails are sent
          through an SMTP server. The address you registered with is the only personal datum
          shared with the SMTP provider, and only for the duration of delivery.
        </li>
        <li>
          <strong>Shared location (opt-in).</strong> If you choose to share your location from
          your account page, the latitude and longitude you submit are stored alongside your
          username and shown on the public
          <NuxtLink to="/map" class="hover:text-cyan-400 underline">/map</NuxtLink>.
          The public list rounds the coordinates to ~110 m precision so an exact home
          address can't be inferred. You can clear it any time, and it disappears
          automatically when you delete your account.
          <br />
          When you share by typing a place name (e.g. "Amsterdam"), the lookup goes through
          the public OpenStreetMap geocoder
          (<a href="https://nominatim.openstreetmap.org/" target="_blank" rel="noopener" class="hover:text-cyan-400 underline">Nominatim</a>);
          the request is made server-side so your IP address is not shared with them.
        </li>
      </ul>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">3. What is <em>not</em> collected</h2>
      <ul class="list-disc list-inside space-y-2">
        <li>No analytics, no tracking pixels, no third-party advertising trackers.</li>
        <li>No cookies. The site uses <code>localStorage</code> only to remember your
          session and theme preference; both are cleared when you log out or use your
          browser's site-data controls.</li>
        <li>No data sold or shared with marketing partners. Ever.</li>
      </ul>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">4. Retention</h2>
      <p>
        Account data is kept for as long as the account exists. Refresh tokens expire after a
        bounded lifetime (currently a small number of days) and are then unusable; the rows
        themselves remain until you delete the account or the cleanup job removes them. Comments
        and posts persist until you delete them, anonymise them, or delete the account.
      </p>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">5. Your rights under the AVG</h2>
      <p>You can exercise the following rights from the
        <NuxtLink to="/account" class="hover:text-cyan-400 underline">account page</NuxtLink>
        (or by emailing the controller):</p>
      <ul class="list-disc list-inside space-y-2">
        <li><strong>Access (art. 15) and portability (art. 20).</strong> "Download my data" returns
          everything tied to your account in a machine-readable JSON file.</li>
        <li><strong>Rectification (art. 16).</strong> Correct your username/email by contacting the
          controller; corrections are applied within a reasonable time.</li>
        <li><strong>Erasure (art. 17).</strong> "Delete my account" removes the profile, all posts
          you authored, every active session, and (per your choice) either anonymises or hard-deletes
          your comments. The action is irreversible.</li>
        <li><strong>Restriction (art. 18) and objection (art. 21).</strong> Email the controller —
          the site processes minimal data, but legitimate-interest processing (rate limiting) can
          be objected to.</li>
        <li><strong>Complaint.</strong> If you believe your rights have not been respected, you
          can file a complaint with the Dutch supervisory authority,
          <a href="https://autoriteitpersoonsgegevens.nl/" target="_blank" rel="noopener" class="hover:text-cyan-400 underline">Autoriteit Persoonsgegevens</a>.</li>
      </ul>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">6. Security</h2>
      <ul class="list-disc list-inside space-y-2">
        <li>Passwords are double-hashed (SHA-256 in the browser, Argon2id with a per-user salt on the server).</li>
        <li>Refresh tokens are stored as SHA-256 digests; raw tokens never sit in the database.</li>
        <li>Authenticated sessions can be revoked at any time by logging out (which deletes the active refresh token) or by changing your password (which revokes every active refresh token).</li>
        <li>The site holds a long-lived Falcon-512 (post-quantum) signing keypair used to sign
          first-party statements. Anyone can verify those signatures from the
          <NuxtLink to="/verify-statement" class="hover:text-cyan-400 underline">verify page</NuxtLink>;
          no personal data is involved.</li>
      </ul>
    </section>

    <section>
      <h2 class="text-lg text-cyan-400 mt-4">7. Changes to this policy</h2>
      <p>
        Material changes will be reflected in the "last updated" date at the top of this page.
        If a change affects your rights, the controller will reach out via the email address
        on file before the change takes effect.
      </p>
    </section>

    <section v-if="snapshot" class="mt-10 border-t border-zinc-800 pt-6 space-y-3">
      <h2 class="text-lg text-cyan-400">8. Verifiable snapshot</h2>
      <p>
        Below is the canonical plain-text version of this policy along with a Falcon-512
        signature over those exact bytes. Save the snapshot if you want a portable proof
        that the site published this text on this date — anyone (including you, in the
        future) can paste the text and signature into the
        <NuxtLink to="/verify-statement" class="hover:text-cyan-400 underline">verify page</NuxtLink>
        to confirm the site signed it.
      </p>

      <details class="border border-zinc-800 rounded">
        <summary class="cursor-pointer px-3 py-2 text-xs text-zinc-400 hover:text-cyan-400 select-none">
          show canonical text ({{ snapshot.text.length }} chars) — the bytes the signature attests to
        </summary>
        <pre class="text-xs p-3 overflow-auto whitespace-pre-wrap font-mono text-zinc-300 max-h-96">{{ snapshot.text }}</pre>
      </details>

      <dl class="text-xs grid grid-cols-[max-content_1fr] gap-x-3 gap-y-1 font-mono text-zinc-400">
        <dt class="text-zinc-500">last-updated</dt><dd>{{ snapshot.lastUpdated }}</dd>
        <dt class="text-zinc-500">algorithm</dt><dd>{{ snapshot.algorithm }}</dd>
        <dt class="text-zinc-500">key fingerprint</dt><dd class="break-all">{{ snapshot.publicKeyFingerprint }}</dd>
        <dt class="text-zinc-500">signature</dt>
        <dd class="break-all">
          <span class="text-zinc-300">{{ sigShort }}</span>
          <button
            type="button"
            @click="copySignature"
            class="ml-2 text-cyan-500 hover:text-cyan-400 underline"
          >{{ copyState === 'copied' ? 'copied' : copyState === 'error' ? 'copy failed' : 'copy full' }}</button>
        </dd>
      </dl>

      <div class="flex flex-wrap gap-3 text-xs pt-2">
        <button
          type="button"
          @click="downloadSnapshot"
          class="px-3 py-1.5 bg-cyan-700 hover:bg-cyan-600 text-white rounded"
        >Download signed snapshot (.json)</button>
        <NuxtLink
          to="/verify-statement"
          class="px-3 py-1.5 border border-zinc-700 hover:border-cyan-700 rounded text-zinc-300 hover:text-cyan-400"
        >Verify a saved snapshot →</NuxtLink>
      </div>
    </section>
  </article>
</template>
