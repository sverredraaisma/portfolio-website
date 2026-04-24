<script setup lang="ts">
import { hashPasswordForTransit } from '~/composables/usePasswordHash'

const route = useRoute()
const router = useRouter()
const rpc = useRpc()

const token = computed(() => String(route.query.token ?? ''))
const password = ref('')
const confirm = ref('')
const loading = ref(false)
const done = ref(false)
const error = ref('')

async function submit() {
  error.value = ''
  if (password.value.length < 8) {
    error.value = 'Password must be at least 8 characters.'
    return
  }
  if (password.value !== confirm.value) {
    error.value = 'Passwords do not match.'
    return
  }
  if (!token.value) {
    error.value = 'Reset link is missing its token. Request a new one.'
    return
  }

  loading.value = true
  try {
    const clientHash = await hashPasswordForTransit(password.value)
    await rpc.call('auth.resetPassword', { token: token.value, clientHash })
    done.value = true
    setTimeout(() => router.push('/login'), 1500)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="max-w-sm mx-auto px-6 py-16">
    <h1 class="text-xl text-cyan-400 mb-6">$ reset password</h1>

    <p v-if="!token" class="text-red-400 text-sm">
      No token in URL. Open the link from your reset email.
    </p>

    <div v-else-if="done" class="text-cyan-300 text-sm">
      Password updated. Redirecting to login…
    </div>

    <form v-else @submit.prevent="submit" class="space-y-3">
      <input
        v-model="password"
        type="password"
        placeholder="new password"
        autocomplete="new-password"
        required
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
      />
      <input
        v-model="confirm"
        type="password"
        placeholder="confirm password"
        autocomplete="new-password"
        required
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
      />
      <button
        :disabled="loading"
        class="w-full bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded py-2 disabled:opacity-50"
      >
        {{ loading ? '...' : 'set new password' }}
      </button>
      <p class="text-xs text-zinc-500">
        Resetting your password will sign you out of any active sessions on other devices.
      </p>
    </form>

    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>
  </section>
</template>
