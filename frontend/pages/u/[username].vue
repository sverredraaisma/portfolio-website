<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'
import { formatTime } from '~/composables/useDate'

type ProfilePost = { id: string; title: string; slug: string; createdAt: string }
type ProfileComment = {
  id: string
  body: string
  createdAt: string
  postId: string
  postTitle: string
  postSlug: string
}
type Profile = {
  username: string
  isAdmin: boolean
  createdAt: string
  bio: string
  postCount: number
  commentCount: number
  recentPosts: ProfilePost[]
  recentComments: ProfileComment[]
}

const route = useRoute()
const rpc = useRpc()

// Route param is the username; trim defensively even though the router
// should never give us anything else.
const username = computed(() => String(route.params.username ?? '').trim())

// `key` is reactive AND `watch` is set so navigating /u/alice → /u/bob (same
// component instance, different param) actually triggers a refetch instead
// of re-using the alice payload.
const { data: profile, pending } = await useAsyncData<Profile | null>(
  () => `profile:${username.value}`,
  async () => {
    if (!username.value) return null
    try {
      return await rpc.call<Profile>('users.getProfile', { username: username.value })
    } catch {
      // The server returns "User not found" as a generic invalid-op error;
      // fall through so the template renders the not-found state.
      return null
    }
  },
  { watch: [username] }
)

// On SSR, surface the 404 status so search engines + monitoring don't index
// a missing username as a 200 OK empty page. setResponseStatus is a no-op
// in the browser, so no client-side guard needed.
if (!profile.value) {
  const event = useRequestEvent()
  if (event) setResponseStatus(event, 404)
}

// Function form so the title + noindex meta track the current profile
// across client-side navigation. Without this, going /u/alice → /u/missing
// (same component instance, refetched data) would keep the alice title
// and skip the noindex on the now-404 page.
useHead({
  title: () => profile.value ? `${profile.value.username} — profile` : 'profile not found',
  meta: () => profile.value ? [] : [{ name: 'robots', content: 'noindex' }]
})

// One-line preview of a comment body for the strip on a profile. We don't
// trust the server to truncate; capping here keeps the layout stable when
// someone leaves a 2000-char wall of text.
function preview(body: string, max = 140): string {
  const oneLine = body.replace(/\s+/g, ' ').trim()
  return oneLine.length <= max ? oneLine : oneLine.slice(0, max - 1) + '…'
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <div v-if="pending" class="text-zinc-500 text-sm flex items-center gap-2">
      <Spinner size="sm" /> loading profile...
    </div>

    <div v-else-if="!profile" class="text-center py-12 text-zinc-500 text-sm">
      <p class="text-base mb-2">No user named <span class="text-zinc-700 dark:text-zinc-300">{{ username }}</span>.</p>
      <NuxtLink to="/posts" class="text-cyan-500 hover:text-cyan-400 underline">browse posts</NuxtLink>
    </div>

    <template v-else>
      <header class="mb-6">
        <h1 class="text-2xl text-cyan-400 flex items-center gap-2">
          <span>$ whoami</span>
          <span class="text-zinc-700 dark:text-zinc-200">{{ profile.username }}</span>
          <span
            v-if="profile.isAdmin"
            class="text-xs uppercase tracking-wider bg-red-100 dark:bg-red-950 text-red-700 dark:text-red-300 rounded px-1.5 py-0.5"
            title="this user is a site admin"
          >admin</span>
        </h1>
        <p class="text-xs text-zinc-500 mt-1">joined {{ formatTime(profile.createdAt) }}</p>
        <p class="text-xs text-zinc-500 mt-1">
          {{ profile.postCount }} {{ profile.postCount === 1 ? 'post' : 'posts' }}
          ·
          {{ profile.commentCount }} {{ profile.commentCount === 1 ? 'comment' : 'comments' }}
        </p>
        <p
          v-if="profile.bio"
          class="text-sm text-zinc-700 dark:text-zinc-300 mt-3 whitespace-pre-wrap"
        >{{ profile.bio }}</p>
      </header>

      <section v-if="profile.recentPosts.length" class="mb-8">
        <h2 class="text-sm uppercase tracking-wider text-zinc-500 mb-2">recent posts</h2>
        <ul class="space-y-2">
          <li
            v-for="p in profile.recentPosts"
            :key="p.id"
            class="border border-zinc-300 dark:border-zinc-800 rounded p-3 hover:border-cyan-700 transition"
          >
            <NuxtLink :to="`/posts/${p.slug}`" class="block">
              <div class="text-base">{{ p.title }}</div>
              <div class="text-xs text-zinc-500">{{ formatTime(p.createdAt) }}</div>
            </NuxtLink>
          </li>
        </ul>
      </section>

      <section v-if="profile.recentComments.length">
        <h2 class="text-sm uppercase tracking-wider text-zinc-500 mb-2">recent comments</h2>
        <ul class="space-y-2">
          <li
            v-for="c in profile.recentComments"
            :key="c.id"
            class="border border-zinc-300 dark:border-zinc-800 rounded p-3 hover:border-cyan-700 transition"
          >
            <NuxtLink :to="`/posts/${c.postSlug}`" class="block">
              <div class="text-sm text-zinc-700 dark:text-zinc-200">{{ preview(c.body) }}</div>
              <div class="text-xs text-zinc-500 mt-1">
                on <span class="text-cyan-600 dark:text-cyan-400">{{ c.postTitle }}</span>
                · {{ formatTime(c.createdAt) }}
              </div>
            </NuxtLink>
          </li>
        </ul>
      </section>

      <p
        v-if="!profile.recentPosts.length && !profile.recentComments.length"
        class="text-zinc-500 text-sm"
      >
        Nothing public yet.
      </p>
    </template>
  </section>
</template>
