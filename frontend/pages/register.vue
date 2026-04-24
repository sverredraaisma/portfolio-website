<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'
import { hashPasswordForTransit } from '~/composables/usePasswordHash'

const username = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const done = ref(false)
const loading = ref(false)
const rpc = useRpc()

const passwordOk = computed(() => password.value.length >= 8)
const formOk = computed(() =>
  username.value.trim().length >= 1 &&
  email.value.includes('@') &&
  passwordOk.value
)

async function submit() {
  if (!formOk.value) return
  error.value = ''
  loading.value = true
  try {
    const clientHash = await hashPasswordForTransit(password.value)
    await rpc.call<void>('auth.register', { username: username.value, email: email.value, clientHash })
    done.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="max-w-sm mx-auto px-6 py-12 sm:py-16">
    <h1 class="text-xl text-cyan-400 mb-6">$ register</h1>

    <div v-if="done" class="border border-cyan-300 dark:border-cyan-800 bg-cyan-50 dark:bg-cyan-950 rounded p-4 text-cyan-700 dark:text-cyan-300 text-sm space-y-2">
      <p class="font-bold">Almost there.</p>
      <p>We sent a verification link to <span class="font-mono">{{ email }}</span>. Click it to finish creating your account.</p>
      <p class="text-xs text-zinc-500">Didn't get it? Check spam, then <NuxtLink to="/login" class="underline hover:text-cyan-500">try logging in</NuxtLink> — you can resend the link from there.</p>
    </div>

    <form v-else @submit.prevent="submit" class="space-y-4">
      <label class="block">
        <span class="block text-xs text-zinc-500 mb-1">username</span>
        <input
          v-model="username"
          autofocus
          autocomplete="username"
          class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
        />
      </label>

      <label class="block">
        <span class="block text-xs text-zinc-500 mb-1">email</span>
        <input
          v-model="email"
          type="email"
          autocomplete="email"
          class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
        />
      </label>

      <label class="block">
        <span class="block text-xs text-zinc-500 mb-1">
          password
          <span v-if="password && !passwordOk" class="text-yellow-600 dark:text-yellow-400 ml-2">
            ({{ 8 - password.length }} more chars)
          </span>
        </span>
        <input
          v-model="password"
          type="password"
          autocomplete="new-password"
          class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2"
        />
      </label>

      <button
        :disabled="loading || !formOk"
        class="w-full bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded py-2 disabled:opacity-50 inline-flex items-center justify-center gap-2"
      >
        <Spinner v-if="loading" size="sm" />
        <span>{{ loading ? 'creating account' : 'register' }}</span>
      </button>
    </form>

    <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>
    <p v-if="!done" class="text-xs text-zinc-500 mt-4">
      Already have an account? <NuxtLink to="/login" class="hover:text-cyan-400 underline">log in</NuxtLink>.
    </p>
  </section>
</template>
