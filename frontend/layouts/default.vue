<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
const auth = useAuthStore()
const rpc = useRpc()
const { theme, toggle } = useTheme()

async function logout() {
  // Try to revoke the refresh token server-side; ignore failures (the local
  // session is cleared either way).
  if (auth.refreshToken) {
    try { await rpc.call('auth.logout', { refreshToken: auth.refreshToken }) } catch {}
  }
  auth.logout()
}
</script>

<template>
  <div>
    <header class="border-b border-zinc-300 dark:border-zinc-800 px-6 py-3 flex items-center justify-between">
      <NuxtLink to="/" class="text-cyan-400 font-bold tracking-widest">~/sverre</NuxtLink>
      <nav class="flex gap-4 text-sm">
        <NuxtLink to="/posts" class="hover:text-cyan-400">posts</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/admin/posts" class="hover:text-cyan-400">manage</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/posts/new" class="hover:text-cyan-400">new</NuxtLink>
        <template v-if="auth.isAuthenticated">
          <span class="text-zinc-500">{{ auth.user?.username }}</span>
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
    <main>
      <slot />
    </main>
  </div>
</template>
