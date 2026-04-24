<script setup lang="ts">
import { hashPasswordForTransit } from '~/composables/usePasswordHash'
import { useAuthStore } from '~/stores/auth'

type AuthUser = { id: string; username: string; email: string; emailVerified?: boolean }
type AuthSuccess = { accessToken: string; refreshToken: string; user: AuthUser }
type LoginResponse = { tokens: AuthSuccess | null; challenge: string | null }

const username = ref('')
const password = ref('')
const email = ref('')
const totpCode = ref('')
const challenge = ref<string | null>(null)
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

function applyResult(res: LoginResponse) {
  if (res.challenge) {
    challenge.value = res.challenge
    error.value = ''
    return
  }
  if (res.tokens) {
    auth.setSession(res.tokens.accessToken, res.tokens.refreshToken, res.tokens.user)
    router.push('/posts')
  }
}

async function submit() {
  error.value = ''
  unverified.value = false
  loading.value = true
  try {
    const clientHash = await hashPasswordForTransit(password.value)
    const res = await rpc.call<LoginResponse>(
      'auth.login',
      { username: username.value, clientHash }
    )
    applyResult(res)
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

async function submitTotp() {
  if (!challenge.value || !totpCode.value) return
  loading.value = true
  error.value = ''
  try {
    const res = await rpc.call<LoginResponse>(
      'auth.completeTotp',
      { challenge: challenge.value, code: totpCode.value }
    )
    applyResult(res)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

function cancelTotp() {
  challenge.value = null
  totpCode.value = ''
  error.value = ''
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
    <h1 class="text-xl text-cyan-400 mb-6">$ login</h1>

    <!-- Step 1: username + password. Hidden once the TOTP challenge arrives. -->
    <form v-if="!challenge" @submit.prevent="submit" class="space-y-3">
      <input v-model="username" placeholder="username" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" autocomplete="username" />
      <input v-model="password" type="password" placeholder="password" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" autocomplete="current-password" />
      <button :disabled="loading" class="w-full bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded py-2 disabled:opacity-50">
        {{ loading ? '...' : 'login' }}
      </button>
    </form>

    <!-- Step 2: TOTP. Only shown when the server hands back a challenge. -->
    <form v-else @submit.prevent="submitTotp" class="space-y-3">
      <p class="text-xs text-zinc-500">
        2FA enabled — enter the 6-digit code from your authenticator app,
        or a recovery code (XXXXX-XXXXX) if you've lost your device.
      </p>
      <input
        v-model="totpCode"
        autocomplete="one-time-code"
        autofocus
        placeholder="123456 or recovery code"
        class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-center font-mono tracking-widest"
      />
      <div class="flex gap-2">
        <button
          :disabled="loading || !totpCode.trim()"
          class="flex-1 bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded py-2 disabled:opacity-50"
        >{{ loading ? '...' : 'verify' }}</button>
        <button
          type="button"
          @click="cancelTotp"
          class="px-3 text-sm text-zinc-500 hover:text-zinc-300"
        >cancel</button>
      </div>
      <p class="text-xs text-zinc-500">
        Lost both? <NuxtLink to="/forgot-password" class="hover:text-cyan-400 underline">Reset your password</NuxtLink> —
        the email link clears 2FA so you can re-enrol from /account.
      </p>
    </form>

    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>

    <div v-if="unverified" class="mt-4 border border-zinc-300 dark:border-zinc-800 rounded p-3 space-y-2">
      <p class="text-xs text-zinc-400">enter your email to resend the verification link:</p>
      <div class="flex gap-2">
        <input
          v-model="email"
          type="email"
          placeholder="email"
          class="flex-1 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-1 text-sm"
          autocomplete="email"
        />
        <button
          :disabled="!email || resendStatus === 'sending'"
          @click="resend"
          class="bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-700 dark:hover:bg-zinc-600 text-cyan-300 rounded px-3 py-1 text-sm disabled:opacity-50"
        >resend</button>
      </div>
      <p v-if="resendStatus === 'sent'" class="text-xs text-cyan-400">
        If an unverified account exists for that email, we've sent a new link.
      </p>
    </div>

    <p class="text-xs text-zinc-500 mt-4">
      <NuxtLink to="/forgot-password" class="hover:text-cyan-400">forgot password?</NuxtLink>
    </p>
  </section>
</template>
