<script setup lang="ts">
import type { Block, PostDocument } from '~/types/blocks'
import BlockRenderer from '~/components/BlockRenderer.vue'
import TerminalComments from '~/components/TerminalComments.vue'
import { formatTime } from '~/composables/useDate'
import { readingTimeMinutes } from '~/composables/useReadingTime'
import { useAuthStore } from '~/stores/auth'
import { useToast } from '~/composables/useToast'

type PostFull = {
  id: string; title: string; slug: string;
  blocks: PostDocument; createdAt: string; updatedAt: string; author: string; tags: string[]
}
const route = useRoute()
const rpc = useRpc()
const auth = useAuthStore()
// Reactive key + explicit watch so navigating /posts/a → /posts/b (same
// component instance, different slug) actually refetches instead of
// re-rendering the previous post.
const slug = computed(() => String(route.params.slug ?? ''))
const { data: post } = await useAsyncData<PostFull>(
  () => `post:${slug.value}`,
  () => rpc.call<PostFull>('posts.get', { slug: slug.value }),
  { watch: [slug] }
)

type Neighbour = { title: string; slug: string }
type Adjacent = { previous: Neighbour | null; next: Neighbour | null }
const { data: adjacent } = await useAsyncData<Adjacent>(
  () => `post-adjacent:${slug.value}`,
  () => rpc.call<Adjacent>('posts.adjacent', { slug: slug.value }),
  // Tolerate unknown slug here — the post fetcher above will already have
  // surfaced the not-found state, no need to also blow up the prev/next.
  { watch: [slug], default: () => ({ previous: null, next: null }) }
)

// Pull a usable description out of the post body: first text block, falling
// back to the first header. Trimmed to a sensible meta length.
function description(blocks: Block[] | undefined): string {
  if (!blocks?.length) return ''
  const text = blocks.find(b => b.type === 'text') as Extract<Block, { type: 'text' }> | undefined
  if (text?.data.markdown) return text.data.markdown.replace(/\s+/g, ' ').slice(0, 200)
  const header = blocks.find(b => b.type === 'header') as Extract<Block, { type: 'header' }> | undefined
  return header?.data.text ?? ''
}

const readMinutes = computed(() => readingTimeMinutes(post.value?.blocks?.blocks))

useSeoMeta({
  title: () => post.value?.title,
  description: () => description(post.value?.blocks?.blocks),
  ogTitle: () => post.value?.title,
  ogDescription: () => description(post.value?.blocks?.blocks),
  ogType: 'article'
})

// ---- Bookmark state -------------------------------------------------------
// Bookmarking is auth-only — the backend rejects anonymous toggle, so we
// hide the button rather than render a disabled control. Initial state is
// fetched once after the post loads; subsequent toggles update locally and
// reconcile against the response.
const toast = useToast()
const isBookmarked = ref(false)
const bookmarkBusy = ref(false)
async function refreshBookmarkState() {
  if (!auth.isAuthenticated || !post.value) return
  try {
    const res = await rpc.call<{ isBookmarked: boolean }>('bookmarks.isBookmarked', { postId: post.value.id })
    isBookmarked.value = res.isBookmarked
  } catch {
    // Best-effort — leave the button in its default (unsaved) state.
  }
}
async function toggleBookmark() {
  if (!post.value || bookmarkBusy.value) return
  bookmarkBusy.value = true
  try {
    const res = await rpc.call<{ isBookmarked: boolean }>('bookmarks.toggle', { postId: post.value.id })
    isBookmarked.value = res.isBookmarked
    toast.info(res.isBookmarked ? 'Saved.' : 'Removed from saved.')
  } catch (e) {
    toast.error(e instanceof Error ? e.message : String(e))
  } finally {
    bookmarkBusy.value = false
  }
}
watch(() => post.value?.id, refreshBookmarkState, { immediate: true })
</script>

<template>
  <article v-if="post" class="max-w-3xl mx-auto px-6 py-10">
    <header class="mb-8 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-3xl text-cyan-400">{{ post.title }}</h1>
        <div class="text-xs text-zinc-500 mt-1">
          {{ formatTime(post.createdAt) }} ·
          <NuxtLink :to="`/u/${post.author}`" class="hover:underline text-zinc-500 hover:text-cyan-500">{{ post.author }}</NuxtLink>
          · ~{{ readMinutes }} min read
        </div>
      </div>
      <div class="flex items-center gap-2">
        <button
          v-if="auth.isAuthenticated"
          :disabled="bookmarkBusy"
          @click="toggleBookmark"
          class="text-xs px-2 py-1 rounded border transition disabled:opacity-50"
          :class="isBookmarked
            ? 'border-cyan-500 text-cyan-400 hover:border-cyan-400'
            : 'border-zinc-300 dark:border-zinc-700 text-zinc-400 hover:text-cyan-400 hover:border-cyan-700'"
          :title="isBookmarked ? 'Remove from saved' : 'Save for later'"
        >{{ isBookmarked ? '★ saved' : '☆ save' }}</button>
        <NuxtLink
          v-if="auth.user?.isAdmin"
          :to="`/posts/${post.slug}/edit`"
          class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-700 text-zinc-400 hover:text-cyan-400"
        >edit</NuxtLink>
      </div>
    </header>

    <div v-if="post.tags?.length" class="mb-6 flex flex-wrap gap-1">
      <NuxtLink
        v-for="t in post.tags"
        :key="t"
        :to="{ path: '/posts', query: { tag: t } }"
        class="text-xs bg-zinc-100 dark:bg-zinc-900 hover:bg-cyan-100 dark:hover:bg-cyan-950 hover:text-cyan-700 dark:hover:text-cyan-300 rounded px-2 py-0.5 text-zinc-600 dark:text-zinc-400"
      >#{{ t }}</NuxtLink>
    </div>

    <div class="space-y-6">
      <BlockRenderer v-for="b in post.blocks.blocks" :key="b.id" :block="b" />
    </div>

    <nav
      v-if="adjacent && (adjacent.previous || adjacent.next)"
      class="mt-12 grid grid-cols-2 gap-3 text-sm border-t border-zinc-200 dark:border-zinc-800 pt-4"
      aria-label="adjacent posts"
    >
      <NuxtLink
        v-if="adjacent.previous"
        :to="`/posts/${adjacent.previous.slug}`"
        class="border border-zinc-300 dark:border-zinc-800 rounded p-3 hover:border-cyan-700 transition"
      >
        <div class="text-xs text-zinc-500 mb-1">← previous</div>
        <div class="text-cyan-500 dark:text-cyan-400 truncate">{{ adjacent.previous.title }}</div>
      </NuxtLink>
      <span v-else />
      <NuxtLink
        v-if="adjacent.next"
        :to="`/posts/${adjacent.next.slug}`"
        class="border border-zinc-300 dark:border-zinc-800 rounded p-3 hover:border-cyan-700 transition text-right"
      >
        <div class="text-xs text-zinc-500 mb-1">next →</div>
        <div class="text-cyan-500 dark:text-cyan-400 truncate">{{ adjacent.next.title }}</div>
      </NuxtLink>
    </nav>

    <section id="comments" class="mt-12 scroll-mt-16">
      <h2 class="text-sm text-zinc-500 mb-2">// comments</h2>
      <TerminalComments :post-id="post.id" />
    </section>
  </article>
</template>
