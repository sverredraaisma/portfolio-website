<script setup lang="ts">
import { hashPasswordForTransit } from '~/composables/usePasswordHash'
import { useAuthStore } from '~/stores/auth'

type AuthUser = { id: string; username: string; email: string; emailVerified?: boolean }
type LoginResult = { accessToken: string; refreshToken: string; user: AuthUser }

const username = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)
const router = useRouter()
const auth = useAuthStore()
const rpc = useRpc()

async function submit() {
  error.value = ''
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
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
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
  </section>
</template>
