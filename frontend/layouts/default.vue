<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
const auth = useAuthStore()
const rpc = useRpc()
const { theme, toggle } = useTheme()
const route = useRoute()

async function logout() {
  // Try to revoke the refresh token server-side; ignore failures (the local
  // session is cleared either way).
  if (auth.refreshToken) {
    try { await rpc.call('auth.logout', { refreshToken: auth.refreshToken }) } catch {}
  }
  auth.logout()
}

// Banner: only nag when the user *just* logged in but never confirmed.
// `auth.user.emailVerified` is set on the session payload; `false` means the
// user did slip through (only possible if email-verify is bypassed) or the
// flag is just missing from the stored object — either way, hint at /account
// for the resend flow. Hide on /account itself so it doesn't render twice.
const showVerifyBanner = computed(() =>
  auth.isAuthenticated &&
  auth.user?.emailVerified === false &&
  route.path !== '/account'
)
</script>

<template>
  <div>
    <header class="border-b border-zinc-300 dark:border-zinc-800 px-6 py-3 flex items-center justify-between">
      <NuxtLink to="/" class="text-cyan-400 font-bold tracking-widest">~/sverre</NuxtLink>
      <nav class="flex gap-4 text-sm">
        <NuxtLink to="/posts" class="hover:text-cyan-400">posts</NuxtLink>
        <NuxtLink to="/verify-statement" class="hover:text-cyan-400">verify</NuxtLink>
        <NuxtLink to="/privacy" class="hover:text-cyan-400">privacy</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/admin/posts" class="hover:text-cyan-400">manage</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/admin/comments" class="hover:text-cyan-400">moderate</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/posts/new" class="hover:text-cyan-400">new</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/sign" class="hover:text-cyan-400">sign</NuxtLink>
        <template v-if="auth.isAuthenticated">
          <NuxtLink to="/account" class="text-zinc-500 hover:text-cyan-400">{{ auth.user?.username }}</NuxtLink>
          <button class="hover:text-red-400" @click="logout">logout</button>
        </template>
        <template v-else>
          <NuxtLink to="/login" class="hover:text-cyan-400">login</NuxtLink>
          <NuxtLink to="/register" class="hover:text-cyan-400">register</NuxtLink>
        </template>
        <button
          @click="toggle"
          :title="theme === 'dark' ? 'switch to light mode' : 'switch to dark mode'"
          class="text-zinc-500 hover:text-cyan-400 ml-2"
        >{{ theme === 'dark' ? '☀' : '☾' }}</button>
      </nav>
    </header>
    <div
      v-if="showVerifyBanner"
      class="bg-yellow-100 dark:bg-yellow-950 border-b border-yellow-400 dark:border-yellow-700 text-yellow-800 dark:text-yellow-300 text-sm px-6 py-2 text-center"
    >
      Your email is not yet verified —
      <NuxtLink to="/account" class="underline hover:text-yellow-900 dark:hover:text-yellow-200">resend the link</NuxtLink>.
    </div>
    <main>
      <slot />
    </main>
  </div>
</template>
