<script setup lang="ts">
import { hashPasswordForTransit } from '~/composables/usePasswordHash'
import { useAuthStore } from '~/stores/auth'

type AuthUser = { id: string; username: string; email: string; emailVerified?: boolean }
type LoginResult = { accessToken: string; refreshToken: string; user: AuthUser }

const username = ref('')
const password = ref('')
const email = ref('')
const error = ref('')
const loading = ref(false)
const unverified = ref(false)
const resendStatus = ref<'idle' | 'sending' | 'sent'>('idle')
const router = useRouter()
const auth = useAuthStore()
const rpc = useRpc()

// Surface the "verify your email" branch separately so the UI can offer a
// resend button — login throws this as a generic 'invalid' error otherwise.
const UNVERIFIED_NEEDLE = 'email not verified'

async function submit() {
  error.value = ''
  unverified.value = false
  loading.value = true
  try {
    const clientHash = await hashPasswordForTransit(password.value)
    const res = await rpc.call<LoginResult>(
      'auth.login',
      { username: username.value, clientHash }
    )
    auth.setSession(res.accessToken, res.refreshToken, res.user)
    router.push('/posts')
  } catch (e) {
    const msg = e instanceof Error ? e.message : String(e)
    if (msg.toLowerCase().includes(UNVERIFIED_NEEDLE)) {
      unverified.value = true
      error.value = 'Email not verified. Resend the verification link below.'
    } else {
      error.value = msg
    }
  } finally {
    loading.value = false
  }
}

async function resend() {
  if (!email.value) return
  resendStatus.value = 'sending'
  try {
    await rpc.call<void>('auth.resendVerification', { email: email.value })
  } finally {
    // Always show "sent" — backend silently no-ops on unknown/verified to
    // avoid leaking account state.
    resendStatus.value = 'sent'
  }
}
</script>

<template>
  <section class="max-w-sm mx-auto px-6 py-16">
    <h1 class="text-xl text-green-400 mb-6">$ login</h1>
    <form @submit.prevent="submit" class="space-y-3">
      <input v-model="username" placeholder="username" class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2" autocomplete="username" />
      <input v-model="password" type="password" placeholder="password" class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2" autocomplete="current-password" />
      <button :disabled="loading" class="w-full bg-green-600 hover:bg-green-500 text-black font-bold rounded py-2 disabled:opacity-50">
        {{ loading ? '...' : 'login' }}
      </button>
    </form>
    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>

    <div v-if="unverified" class="mt-4 border border-zinc-800 rounded p-3 space-y-2">
      <p class="text-xs text-zinc-400">enter your email to resend the verification link:</p>
      <div class="flex gap-2">
        <input
          v-model="email"
          type="email"
          placeholder="email"
          class="flex-1 bg-zinc-900 border border-zinc-700 rounded px-3 py-1 text-sm"
          autocomplete="email"
        />
        <button
          :disabled="!email || resendStatus === 'sending'"
          @click="resend"
          class="bg-zinc-700 hover:bg-zinc-600 text-green-300 rounded px-3 py-1 text-sm disabled:opacity-50"
        >resend</button>
      </div>
      <p v-if="resendStatus === 'sent'" class="text-xs text-green-400">
        If an unverified account exists for that email, we've sent a new link.
      </p>
    </div>

    <p class="text-xs text-zinc-500 mt-4">
      <NuxtLink to="/forgot-password" class="hover:text-green-400">forgot password?</NuxtLink>
    </p>
  </section>
</template>
