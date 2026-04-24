<script setup lang="ts">
definePageMeta({ middleware: 'admin' })

type SignedStatement = {
  algorithm: string
  statement: string
  signatureBase64: string
  publicKeyBase64: string
  publicKeyFingerprint: string
  signedAt: string
}

const rpc = useRpc()
const statement = ref('')
const result = ref<SignedStatement | null>(null)
const loading = ref(false)
const error = ref('')
const copied = ref<'env' | 'sig' | null>(null)

async function sign() {
  if (!statement.value.trim()) return
  loading.value = true
  error.value = ''
  result.value = null
  try {
    result.value = await rpc.call<SignedStatement>('signing.sign', { statement: statement.value })
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

const envelope = computed(() => result.value ? JSON.stringify(result.value, null, 2) : '')

async function copy(kind: 'env' | 'sig') {
  if (!result.value) return
  const text = kind === 'env' ? envelope.value : result.value.signatureBase64
  try {
    await navigator.clipboard.writeText(text)
    copied.value = kind
    setTimeout(() => { if (copied.value === kind) copied.value = null }, 1500)
  } catch {
    error.value = 'Could not copy to clipboard.'
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <h1 class="text-2xl text-cyan-400 mb-2">$ falcon sign</h1>
    <p class="text-xs text-zinc-500 mb-6">
      Signs the statement with the website's Falcon-512 private key.
      Anyone can verify it at <NuxtLink to="/verify-statement" class="hover:text-cyan-400 underline">/verify-statement</NuxtLink>.
    </p>

    <textarea
      v-model="statement"
      rows="6"
      placeholder="paste the statement to sign..."
      class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm leading-relaxed"
    />

    <div class="mt-3 flex items-center gap-3">
      <button
        :disabled="loading || !statement.trim()"
        @click="sign"
        class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
      >{{ loading ? '...' : 'sign' }}</button>
      <span v-if="error" class="text-red-400 text-sm">{{ error }}</span>
    </div>

    <div v-if="result" class="mt-6 border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-3">
      <div class="text-xs text-zinc-500">
        algorithm: <span class="text-cyan-400">{{ result.algorithm }}</span>
        · signed: {{ new Date(result.signedAt).toLocaleString() }}
      </div>
      <div class="text-xs text-zinc-500 break-all">
        fingerprint: <span class="text-cyan-400">{{ result.publicKeyFingerprint }}</span>
      </div>

      <div>
        <div class="text-xs text-zinc-500 mb-1">signature (base64):</div>
        <div class="flex gap-2 items-start">
          <pre class="flex-1 bg-zinc-100 dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-800 rounded p-2 text-xs whitespace-pre-wrap break-all">{{ result.signatureBase64 }}</pre>
          <button
            @click="copy('sig')"
            class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
          >{{ copied === 'sig' ? 'copied' : 'copy' }}</button>
        </div>
      </div>

      <div>
        <div class="text-xs text-zinc-500 mb-1">full envelope (publishable):</div>
        <div class="flex gap-2 items-start">
          <pre class="flex-1 bg-zinc-100 dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-800 rounded p-2 text-xs whitespace-pre-wrap break-all">{{ envelope }}</pre>
          <button
            @click="copy('env')"
            class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
          >{{ copied === 'env' ? 'copied' : 'copy' }}</button>
        </div>
      </div>
    </div>
  </section>
</template>
