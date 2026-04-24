<script setup lang="ts">
import { formatTime } from '~/composables/useDate'

definePageMeta({ middleware: 'admin' })

type PostSummary = {
  id: string; title: string; slug: string; createdAt: string;
  author: string; published: boolean
}
type PostsPage = { items: PostSummary[]; page: number; pageSize: number; hasMore: boolean }

const PAGE_SIZE = 50
const rpc = useRpc()

const posts = ref<PostSummary[]>([])
const page = ref(1)
const hasMore = ref(false)
const loading = ref(true)
const loadingMore = ref(false)
const error = ref('')
const busyId = ref<string | null>(null)

async function load(p = 1) {
  loading.value = p === 1
  loadingMore.value = p > 1
  try {
    const res = await rpc.call<PostsPage>('posts.list', {
      page: p, pageSize: PAGE_SIZE, includeDrafts: true
    })
    posts.value = p === 1 ? res.items : [...posts.value, ...res.items]
    page.value = res.page
    hasMore.value = res.hasMore
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
    loadingMore.value = false
  }
}

async function togglePublish(p: PostSummary) {
  busyId.value = p.id
  try {
    await rpc.call<void>('posts.update', { id: p.id, published: !p.published })
    p.published = !p.published
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busyId.value = null
  }
}

async function remove(p: PostSummary) {
  if (!confirm(`Delete "${p.title}"? This cannot be undone.`)) return
  busyId.value = p.id
  try {
    await rpc.call<void>('posts.delete', { id: p.id })
    posts.value = posts.value.filter(x => x.id !== p.id)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    busyId.value = null
  }
}

onMounted(() => load(1))
</script>

<template>
  <section class="max-w-4xl mx-auto px-6 py-10">
    <div class="flex items-center justify-between mb-6">
      <h1 class="text-2xl text-cyan-400">$ admin/posts</h1>
      <NuxtLink to="/posts/new" class="text-sm bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-3 py-1">
        + new
      </NuxtLink>
    </div>

    <p v-if="loading" class="text-zinc-500">loading...</p>
    <p v-else-if="!posts.length" class="text-zinc-500">No posts yet.</p>

    <ul v-else class="divide-y divide-zinc-200 dark:divide-zinc-800 border border-zinc-300 dark:border-zinc-800 rounded">
      <li
        v-for="p in posts"
        :key="p.id"
        class="flex items-center gap-3 px-4 py-3"
      >
        <div class="flex-1 min-w-0">
          <NuxtLink :to="`/posts/${p.slug}`" class="block truncate hover:text-cyan-400">
            {{ p.title }}
          </NuxtLink>
          <div class="text-xs text-zinc-500 truncate">
            <span :class="p.published ? 'text-cyan-500' : 'text-yellow-500'">
              {{ p.published ? 'published' : 'draft' }}
            </span>
            · {{ formatTime(p.createdAt) }} · /{{ p.slug }}
          </div>
        </div>

        <button
          :disabled="busyId === p.id"
          @click="togglePublish(p)"
          class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-zinc-500 disabled:opacity-50"
        >
          {{ p.published ? 'unpublish' : 'publish' }}
        </button>

        <NuxtLink
          :to="`/posts/${p.slug}/edit`"
          class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-zinc-500"
        >
          edit
        </NuxtLink>

        <button
          :disabled="busyId === p.id"
          @click="remove(p)"
          class="text-xs px-2 py-1 rounded border border-red-300 dark:border-red-900 text-red-400 hover:border-red-700 disabled:opacity-50"
        >
          ✕
        </button>
      </li>
    </ul>

    <div v-if="hasMore" class="mt-6 flex justify-center">
      <button
        :disabled="loadingMore"
        @click="load(page + 1)"
        class="px-4 py-2 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded text-sm disabled:opacity-50"
      >
        {{ loadingMore ? '...' : 'load more' }}
      </button>
    </div>

    <p v-if="error" class="text-red-400 text-sm mt-4">{{ error }}</p>
  </section>
</template>
