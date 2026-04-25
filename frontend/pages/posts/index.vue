<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'
import { formatTime } from '~/composables/useDate'

type PostSummary = {
  id: string
  title: string
  slug: string
  createdAt: string
  author: string
  tags: string[]
}
type PostsPage = { items: PostSummary[]; page: number; pageSize: number; hasMore: boolean }

const PAGE_SIZE = 20
const route = useRoute()
const router = useRouter()
const rpc = useRpc()

// Query state — backed by the URL so deep-links and back/forward work. The
// ref mirrors route.query and writes back on debounce.
const q = ref<string>(String(route.query.q ?? ''))
const tag = ref<string>(String(route.query.tag ?? ''))

const posts = ref<PostSummary[]>([])
const page = ref(1)
const hasMore = ref(false)
const loading = ref(false)
const loadingMore = ref(false)

function requestParams(nextPage: number) {
  return {
    page: nextPage,
    pageSize: PAGE_SIZE,
    ...(q.value ? { q: q.value } : {}),
    ...(tag.value ? { tag: tag.value } : {})
  }
}

const initialKey = `posts:initial:${q.value}:${tag.value}`
const { data: initial } = await useAsyncData<PostsPage>(initialKey,
  () => rpc.call<PostsPage>('posts.list', requestParams(1)))

if (initial.value) {
  posts.value = initial.value.items
  page.value = initial.value.page
  hasMore.value = initial.value.hasMore
}

async function refresh() {
  loading.value = true
  try {
    const res = await rpc.call<PostsPage>('posts.list', requestParams(1))
    posts.value = res.items
    page.value = res.page
    hasMore.value = res.hasMore
  } finally {
    loading.value = false
  }
}

async function loadMore() {
  if (loadingMore.value || !hasMore.value) return
  loadingMore.value = true
  try {
    const next = await rpc.call<PostsPage>('posts.list', requestParams(page.value + 1))
    posts.value = [...posts.value, ...next.items]
    page.value = next.page
    hasMore.value = next.hasMore
  } finally {
    loadingMore.value = false
  }
}

// Debounced URL + fetch when the search box changes.
let debounce: ReturnType<typeof setTimeout> | null = null
watch(q, (v) => {
  if (debounce) clearTimeout(debounce)
  debounce = setTimeout(() => {
    router.replace({ query: { ...(v ? { q: v } : {}), ...(tag.value ? { tag: tag.value } : {}) } })
    refresh()
  }, 200)
})

function clearTag() {
  tag.value = ''
  router.replace({ query: { ...(q.value ? { q: q.value } : {}) } })
  refresh()
}

// React to back/forward swapping tag in the URL.
watch(() => route.query.tag, (v) => {
  const s = String(v ?? '')
  if (s !== tag.value) {
    tag.value = s
    refresh()
  }
})

// Feed-reader auto-discovery: when filtering by tag, expose the per-tag
// RSS link so a "subscribe" button in the user's reader points at the
// narrow feed instead of the whole site. The site-wide alternate stays
// in nuxt.config.ts.
useHead({
  link: () => tag.value
    ? [{ rel: 'alternate', type: 'application/rss+xml', title: `sverre.dev — #${tag.value}`, href: `/rss/${tag.value}.xml` }]
    : []
})
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <div class="flex items-center justify-between mb-4 gap-3">
      <h1 class="text-2xl text-cyan-400">$ ls posts/</h1>
      <input
        v-model="q"
        placeholder="search title..."
        class="flex-1 max-w-xs bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-1 text-sm"
      />
    </div>

    <div v-if="tag" class="mb-3 text-xs flex items-center gap-2">
      <span class="text-zinc-500">filtered by tag:</span>
      <span class="inline-flex items-center gap-1 bg-cyan-100 dark:bg-cyan-950 text-cyan-700 dark:text-cyan-300 rounded px-2 py-0.5">
        #{{ tag }}
        <button @click="clearTag" class="hover:text-red-400" title="clear">✕</button>
      </span>
    </div>

    <div v-if="loading" class="text-zinc-500 text-sm flex items-center gap-2">
      <Spinner size="sm" /> fetching...
    </div>

    <ul v-else-if="posts.length" class="space-y-3">
      <li v-for="p in posts" :key="p.id" class="border border-zinc-300 dark:border-zinc-800 rounded p-4 hover:border-cyan-700 transition">
        <NuxtLink :to="`/posts/${p.slug}`" class="block">
          <div class="text-lg">{{ p.title }}</div>
        </NuxtLink>
        <div class="text-xs text-zinc-500 mt-1">
          {{ formatTime(p.createdAt) }} ·
          <NuxtLink :to="`/u/${p.author}`" class="hover:underline text-zinc-500 hover:text-cyan-500">{{ p.author }}</NuxtLink>
        </div>
        <div v-if="p.tags?.length" class="mt-2 flex flex-wrap gap-1">
          <NuxtLink
            v-for="t in p.tags"
            :key="t"
            :to="{ path: '/posts', query: { tag: t } }"
            class="text-xs bg-zinc-100 dark:bg-zinc-900 hover:bg-cyan-100 dark:hover:bg-cyan-950 hover:text-cyan-700 dark:hover:text-cyan-300 rounded px-2 py-0.5 text-zinc-600 dark:text-zinc-400"
          >#{{ t }}</NuxtLink>
        </div>
      </li>
    </ul>

    <!-- Friendly empty states. The wording differs depending on whether the
         user is exploring an empty archive or got zero results back from a
         filter — the next steps are not the same. -->
    <div v-else class="text-center py-12 text-zinc-500 text-sm">
      <template v-if="q || tag">
        <p class="text-base mb-2">No posts match your filter.</p>
        <button
          @click="(q = '', tag = '', router.replace({ query: {} }), refresh())"
          class="text-cyan-500 hover:text-cyan-400 underline"
        >clear filter</button>
      </template>
      <template v-else>
        <p class="text-base mb-1">Nothing here yet.</p>
        <p class="text-xs">The first post will land here when it does.</p>
      </template>
    </div>

    <div v-if="hasMore" class="mt-6 flex justify-center">
      <button
        :disabled="loadingMore"
        @click="loadMore"
        class="px-4 py-2 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded text-sm disabled:opacity-50"
      >
        {{ loadingMore ? '...' : 'load more' }}
      </button>
    </div>
  </section>
</template>
