<script setup lang="ts">
const email = ref('')
const loading = ref(false)
const done = ref(false)
const error = ref('')
const rpc = useRpc()

async function submit() {
  error.value = ''
  loading.value = true
  try {
    await rpc.call('auth.requestPasswordReset', { email: email.value })
    done.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="max-w-sm mx-auto px-6 py-16">
    <h1 class="text-xl text-cyan-400 mb-6">$ forgot password</h1>
    <div v-if="done" class="text-cyan-300 text-sm leading-relaxed">
      If that email belongs to an account, a reset link has been sent.
      Check your inbox (and spam folder).
    </div>
    <form v-else @submit.prevent="submit" class="space-y-3">
      <input
        v-model="email"
        type="email"
        required
        placeholder="email"
        autocomplete="email"
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
      />
      <button
        :disabled="loading"
        class="w-full bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded py-2 disabled:opacity-50"
      >
        {{ loading ? '...' : 'send reset link' }}
      </button>
      <p class="text-xs text-zinc-500">
        We won't tell you whether the address has an account — same response either way.
      </p>
    </form>
    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>
  </section>
</template>
