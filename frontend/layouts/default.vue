<script setup lang="ts">
import Toaster from '~/components/Toaster.vue'
import ShortcutsHelp from '~/components/ShortcutsHelp.vue'
import { useAuthStore } from '~/stores/auth'
import { bindShortcuts } from '~/composables/useShortcuts'
const auth = useAuthStore()
const rpc = useRpc()
const { theme, toggle } = useTheme()
const route = useRoute()
const router = useRouter()

const shortcutsOpen = ref(false)

onMounted(() => {
  const off = bindShortcuts({ router, toggleHelp: () => shortcutsOpen.value = !shortcutsOpen.value })
  onBeforeUnmount(off)
})

const navOpen = ref(false)
function closeNav() { navOpen.value = false }

// Close the mobile menu whenever the route changes.
watch(() => route.fullPath, closeNav)

async function logout() {
  if (auth.refreshToken) {
    try { await rpc.call('auth.logout', { refreshToken: auth.refreshToken }) } catch {}
  }
  auth.logout()
  closeNav()
}

const showVerifyBanner = computed(() =>
  auth.isAuthenticated &&
  auth.user?.emailVerified === false &&
  route.path !== '/account'
)
</script>

<template>
  <div>
    <header class="border-b border-zinc-300 dark:border-zinc-800 px-4 sm:px-6 py-3 flex items-center justify-between gap-3">
      <NuxtLink to="/" class="text-cyan-400 font-bold tracking-widest shrink-0">~/sverre</NuxtLink>

      <!-- Desktop nav -->
      <nav class="hidden md:flex items-center gap-4 text-sm">
        <NuxtLink to="/posts" class="hover:text-cyan-400">posts</NuxtLink>
        <NuxtLink to="/map" class="hover:text-cyan-400">map</NuxtLink>
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
          aria-label="toggle theme"
          class="text-zinc-500 hover:text-cyan-400 ml-1"
        >{{ theme === 'dark' ? '☀' : '☾' }}</button>
      </nav>

      <!-- Mobile: theme toggle stays visible alongside the hamburger. -->
      <div class="md:hidden flex items-center gap-3">
        <button
          @click="toggle"
          :title="theme === 'dark' ? 'switch to light mode' : 'switch to dark mode'"
          aria-label="toggle theme"
          class="text-zinc-500 hover:text-cyan-400"
        >{{ theme === 'dark' ? '☀' : '☾' }}</button>
        <button
          @click="navOpen = !navOpen"
          :aria-expanded="navOpen"
          aria-controls="mobile-nav"
          class="text-zinc-700 dark:text-zinc-300 hover:text-cyan-400 p-1"
        >
          <span class="sr-only">menu</span>
          <span aria-hidden="true">{{ navOpen ? '✕' : '☰' }}</span>
        </button>
      </div>
    </header>

    <!-- Mobile drawer. Stacks links vertically with breathable tap targets. -->
    <nav
      v-if="navOpen"
      id="mobile-nav"
      class="md:hidden border-b border-zinc-300 dark:border-zinc-800 bg-white dark:bg-zinc-950 px-4 py-3 flex flex-col text-sm divide-y divide-zinc-200 dark:divide-zinc-800"
    >
      <NuxtLink to="/posts" class="py-2 hover:text-cyan-400">posts</NuxtLink>
      <NuxtLink to="/map" class="py-2 hover:text-cyan-400">map</NuxtLink>
      <NuxtLink to="/verify-statement" class="py-2 hover:text-cyan-400">verify a signature</NuxtLink>
      <NuxtLink to="/privacy" class="py-2 hover:text-cyan-400">privacy</NuxtLink>
      <template v-if="auth.user?.isAdmin">
        <NuxtLink to="/admin/posts" class="py-2 hover:text-cyan-400">manage posts</NuxtLink>
        <NuxtLink to="/admin/comments" class="py-2 hover:text-cyan-400">moderate comments</NuxtLink>
        <NuxtLink to="/posts/new" class="py-2 hover:text-cyan-400">new post</NuxtLink>
        <NuxtLink to="/sign" class="py-2 hover:text-cyan-400">sign a statement</NuxtLink>
      </template>
      <template v-if="auth.isAuthenticated">
        <NuxtLink to="/account" class="py-2 hover:text-cyan-400">account ({{ auth.user?.username }})</NuxtLink>
        <button class="py-2 text-left hover:text-red-400" @click="logout">logout</button>
      </template>
      <template v-else>
        <NuxtLink to="/login" class="py-2 hover:text-cyan-400">login</NuxtLink>
        <NuxtLink to="/register" class="py-2 hover:text-cyan-400">register</NuxtLink>
      </template>
    </nav>

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

    <Toaster />
    <ShortcutsHelp v-model="shortcutsOpen" />
  </div>
</template>
