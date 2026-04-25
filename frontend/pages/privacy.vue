<script setup lang="ts">
import { marked } from 'marked'
import DOMPurify from 'isomorphic-dompurify'

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

// Fetch the canonical signed snapshot via SSR. The cached server-side
// signature is byte-stable across visitors, so two readers who save on the
// same day get the same signature bytes — which makes "I saved this on
// date X" claims unambiguous. The Markdown source IS what's signed; the
// rendered HTML below is a presentation layer over those exact bytes, so
// the two can never drift.
const { data: snapshot } = await useAsyncData<PolicySnapshot>(
  'policy:privacy',
  () => rpc.call<PolicySnapshot>('policy.privacy')
)

// Render the canonical Markdown to HTML, then sanitise. DOMPurify defends
// against any future markdown that could try to embed an XSS vector
// (admin-controlled content today, but defence-in-depth is cheap).
const renderedHtml = computed(() => {
  const md = snapshot.value?.text ?? ''
  if (!md) return ''
  const raw = marked.parse(md, { async: false }) as string
  return DOMPurify.sanitize(raw, { USE_PROFILES: { html: true } })
})

const lastUpdated = computed(() => snapshot.value?.lastUpdated ?? '')

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
    site: 'draaisma.dev',
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
  a.download = `draaisma.dev-privacy-${snapshot.value.lastUpdated}.json`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
</script>

<template>
  <article class="max-w-3xl mx-auto px-6 py-10 space-y-6 text-sm leading-relaxed">
    <header>
      <h1 class="text-2xl text-cyan-400">$ cat privacy.md</h1>
      <p v-if="lastUpdated" class="text-xs text-zinc-500 mt-1">Last updated: {{ lastUpdated }}</p>
    </header>

    <!-- Body is rendered from the same Markdown the signature attests to,
         so there's no possible drift between "what the page says" and
         "what's signed". -->
    <div class="prose-policy" v-html="renderedHtml" />

    <section v-if="snapshot" class="mt-10 border-t border-zinc-800 pt-6 space-y-3">
      <h2 class="text-lg text-cyan-400">Verifiable snapshot</h2>
      <p>
        The body above is rendered from a canonical Markdown document signed with the site's
        Falcon-512 key. Save the snapshot if you want a portable proof that the site published
        this exact text on this date — anyone (including you, in the future) can paste the
        Markdown and signature into the
        <NuxtLink to="/verify-statement" class="hover:text-cyan-400 underline">verify page</NuxtLink>
        to confirm the site signed it.
      </p>

      <details class="border border-zinc-800 rounded">
        <summary class="cursor-pointer px-3 py-2 text-xs text-zinc-400 hover:text-cyan-400 select-none">
          show canonical Markdown ({{ snapshot.text.length }} chars) — the bytes the signature attests to
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

<style scoped>
/* Mirror the styling the hand-rolled HTML used to apply, so the rendered
   markdown looks the same as the previous version did. Headings, lists,
   inline code, and links all get the cyan accent treatment. */
.prose-policy :deep(h1)        { display: none; } /* The page header above is the visible H1. */
.prose-policy :deep(h2)        { color: rgb(34 211 238); font-size: 1.125rem; margin-top: 1.5rem; margin-bottom: 0.5rem; }
.prose-policy :deep(h3)        { color: rgb(34 211 238); font-size: 1rem; margin-top: 1rem; margin-bottom: 0.5rem; }
.prose-policy :deep(p)         { margin: 0.75rem 0; }
.prose-policy :deep(strong)    { color: rgb(165 243 252); font-weight: 600; }
.prose-policy :deep(em)        { font-style: italic; }
.prose-policy :deep(a)         { color: rgb(34 211 238); text-decoration: underline; text-underline-offset: 2px; }
.prose-policy :deep(a:hover)   { color: rgb(103 232 249); }
.prose-policy :deep(code)      { font-family: ui-monospace, monospace; padding: 0 0.25em; background: rgba(34,211,238,0.08); border-radius: 0.25em; font-size: 0.95em; }
.prose-policy :deep(ul)        { list-style: disc; padding-left: 1.25rem; margin: 0.5rem 0; }
.prose-policy :deep(ol)        { list-style: decimal; padding-left: 1.25rem; margin: 0.5rem 0; }
.prose-policy :deep(li)        { margin: 0.5rem 0; }
.prose-policy :deep(li > ul),
.prose-policy :deep(li > ol)   { margin: 0.25rem 0; }
.prose-policy :deep(li > p)    { margin: 0.25rem 0; }
</style>
