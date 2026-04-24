<script setup lang="ts">
type PublicKey = { algorithm: string; publicKeyBase64: string; fingerprint: string }
type VerifyResult = { valid: boolean; fingerprint: string }
type Envelope = {
  algorithm?: string
  statement: string
  signatureBase64: string
  publicKeyBase64?: string
}

const rpc = useRpc()

const statement = ref('')
const signature = ref('')
const customPublicKey = ref('')
const usePinnedKey = ref(false)
const pasteEnvelope = ref('')

const verifying = ref(false)
const result = ref<VerifyResult | null>(null)
const error = ref('')

const sitePublicKey = ref<PublicKey | null>(null)
onMounted(async () => {
  try {
    sitePublicKey.value = await rpc.call<PublicKey>('signing.publicKey')
  } catch {
    // Not fatal — user can still paste their own pubkey to verify against.
  }
})

// Convenience: paste a JSON envelope and have the fields broken out.
function applyEnvelope() {
  if (!pasteEnvelope.value.trim()) return
  try {
    const parsed = JSON.parse(pasteEnvelope.value) as Envelope
    if (typeof parsed.statement === 'string') statement.value = parsed.statement
    if (typeof parsed.signatureBase64 === 'string') signature.value = parsed.signatureBase64
    if (typeof parsed.publicKeyBase64 === 'string') {
      customPublicKey.value = parsed.publicKeyBase64
      usePinnedKey.value = true
    }
    error.value = ''
  } catch {
    error.value = 'Could not parse envelope JSON.'
  }
}

async function verify() {
  result.value = null
  error.value = ''
  if (!statement.value || !signature.value) {
    error.value = 'Statement and signature are both required.'
    return
  }
  verifying.value = true
  try {
    result.value = await rpc.call<VerifyResult>('signing.verify', {
      statement: statement.value,
      signatureBase64: signature.value,
      publicKeyBase64: usePinnedKey.value && customPublicKey.value ? customPublicKey.value : undefined
    })
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    verifying.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10 space-y-6">
    <header>
      <h1 class="text-2xl text-cyan-400">$ verify-statement</h1>
      <p class="text-xs text-zinc-500 mt-1">
        Confirm a statement was signed by this website's Falcon-512 key.
      </p>
    </header>

    <div v-if="sitePublicKey" class="border border-zinc-300 dark:border-zinc-800 rounded p-3 text-xs space-y-1">
      <div class="text-zinc-500">site key — algorithm: <span class="text-cyan-400">{{ sitePublicKey.algorithm }}</span></div>
      <div class="text-zinc-500 break-all">fingerprint: <span class="text-cyan-400">{{ sitePublicKey.fingerprint }}</span></div>
    </div>

    <details class="border border-zinc-300 dark:border-zinc-800 rounded p-3 text-sm">
      <summary class="cursor-pointer text-zinc-500">paste a full envelope (optional)</summary>
      <textarea
        v-model="pasteEnvelope"
        rows="5"
        placeholder='{"statement":"...","signatureBase64":"...","publicKeyBase64":"..."}'
        class="mt-2 w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-xs"
      />
      <button
        @click="applyEnvelope"
        class="mt-2 text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
      >apply</button>
    </details>

    <div class="space-y-2">
      <label class="text-xs text-zinc-500">statement</label>
      <textarea
        v-model="statement"
        rows="6"
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm leading-relaxed"
      />
    </div>

    <div class="space-y-2">
      <label class="text-xs text-zinc-500">signature (base64)</label>
      <textarea
        v-model="signature"
        rows="3"
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-xs break-all"
      />
    </div>

    <div class="space-y-2">
      <label class="flex items-center gap-2 text-xs text-zinc-500">
        <input v-model="usePinnedKey" type="checkbox" />
        verify against a specific public key (otherwise the site's current key is used)
      </label>
      <textarea
        v-if="usePinnedKey"
        v-model="customPublicKey"
        rows="3"
        placeholder="public key (base64)"
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-xs break-all"
      />
    </div>

    <div class="flex items-center gap-3">
      <button
        :disabled="verifying || !statement || !signature"
        @click="verify"
        class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
      >{{ verifying ? '...' : 'verify' }}</button>
      <span v-if="error" class="text-red-400 text-sm">{{ error }}</span>
    </div>

    <div
      v-if="result"
      class="rounded border p-3 text-sm"
      :class="result.valid
        ? 'border-cyan-700 bg-cyan-50 dark:bg-cyan-950 text-cyan-700 dark:text-cyan-300'
        : 'border-red-700 bg-red-50 dark:bg-red-950 text-red-700 dark:text-red-300'"
    >
      <div class="font-bold">
        {{ result.valid ? 'signature valid' : 'signature INVALID' }}
      </div>
      <div class="text-xs mt-1 break-all opacity-80">
        verified against fingerprint: {{ result.fingerprint || '(unknown)' }}
      </div>
    </div>
  </section>
</template>
