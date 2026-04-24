<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
const auth = useAuthStore()
const rpc = useRpc()

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
    <header class="border-b border-zinc-800 px-6 py-3 flex items-center justify-between">
      <NuxtLink to="/" class="text-green-400 font-bold tracking-widest">~/sverre</NuxtLink>
      <nav class="flex gap-4 text-sm">
        <NuxtLink to="/posts" class="hover:text-green-400">posts</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/admin/posts" class="hover:text-green-400">manage</NuxtLink>
        <NuxtLink v-if="auth.user?.isAdmin" to="/posts/new" class="hover:text-green-400">new</NuxtLink>
        <template v-if="auth.isAuthenticated">
          <span class="text-zinc-500">{{ auth.user?.username }}</span>
          <button class="hover:text-red-400" @click="logout">logout</button>
        </template>
        <template v-else>
          <NuxtLink to="/login" class="hover:text-green-400">login</NuxtLink>
          <NuxtLink to="/register" class="hover:text-green-400">register</NuxtLink>
        </template>
      </nav>
    </header>
    <main>
      <slot />
    </main>
  </div>
</template>
