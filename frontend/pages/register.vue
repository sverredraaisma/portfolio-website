<script setup lang="ts">
import { hashPasswordForTransit } from '~/composables/usePasswordHash'

const username = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const done = ref(false)
const loading = ref(false)
const rpc = useRpc()

async function submit() {
  error.value = ''
  loading.value = true
  try {
    const clientHash = await hashPasswordForTransit(password.value)
    await rpc.call('auth.register', { username: username.value, email: email.value, clientHash })
    done.value = true
  } catch (e: any) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="max-w-sm mx-auto px-6 py-16">
    <h1 class="text-xl text-green-400 mb-6">$ register</h1>
    <div v-if="done" class="text-green-300">
      Check your inbox for a verification link.
    </div>
    <form v-else @submit.prevent="submit" class="space-y-3">
      <input v-model="username" placeholder="username" class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2" autocomplete="username" />
      <input v-model="email" type="email" placeholder="email" class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2" autocomplete="email" />
      <input v-model="password" type="password" placeholder="password" class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2" autocomplete="new-password" />
      <button :disabled="loading" class="w-full bg-green-600 hover:bg-green-500 text-black font-bold rounded py-2 disabled:opacity-50">
        {{ loading ? '...' : 'register' }}
      </button>
    </form>
    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>
  </section>
</template>
