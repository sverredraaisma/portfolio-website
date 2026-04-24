<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'
import { formatTime } from '~/composables/useDate'
import { useToast } from '~/composables/useToast'

const toast = useToast()

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
    toast.success(p.published ? 'Published.' : 'Unpublished.')
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
    toast.info(`Deleted "${p.title}".`)
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

    <div v-if="loading" class="text-zinc-500 text-sm flex items-center gap-2">
      <Spinner size="sm" /> loading posts...
    </div>

    <div v-else-if="!posts.length" class="text-center py-12 text-zinc-500">
      <p class="text-base mb-3">No posts yet.</p>
      <NuxtLink to="/posts/new" class="text-sm bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-3 py-2">
        write your first post
      </NuxtLink>
    </div>

    <ul v-else class="divide-y divide-zinc-200 dark:divide-zinc-800 border border-zinc-300 dark:border-zinc-800 rounded">
      <li
        v-for="p in posts"
        :key="p.id"
        class="flex flex-col sm:flex-row sm:items-center gap-3 px-4 py-3"
      >
        <div class="flex-1 min-w-0">
          <NuxtLink :to="`/posts/${p.slug}`" class="block truncate hover:text-cyan-400">
            {{ p.title }}
          </NuxtLink>
          <div class="text-xs text-zinc-500 truncate flex flex-wrap gap-x-2 mt-0.5">
            <span
              class="inline-flex items-center gap-1"
              :class="p.published ? 'text-cyan-500' : 'text-yellow-500'"
            >
              <span class="inline-block w-1.5 h-1.5 rounded-full" :class="p.published ? 'bg-cyan-500' : 'bg-yellow-500'" />
              {{ p.published ? 'published' : 'draft' }}
            </span>
            <span>·</span>
            <span>{{ formatTime(p.createdAt) }}</span>
            <span>·</span>
            <span class="font-mono">/{{ p.slug }}</span>
          </div>
        </div>

        <div class="flex gap-2 sm:gap-1 flex-wrap">
          <button
            :disabled="busyId === p.id"
            @click="togglePublish(p)"
            class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500 disabled:opacity-50"
          >
            {{ p.published ? 'unpublish' : 'publish' }}
          </button>

          <NuxtLink
            :to="`/posts/${p.slug}/edit`"
            class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500"
          >edit</NuxtLink>

          <button
            :disabled="busyId === p.id"
            @click="remove(p)"
            class="text-xs px-2 py-1 rounded border border-red-300 dark:border-red-900 text-red-400 hover:border-red-500 disabled:opacity-50"
          >✕</button>
        </div>
      </li>
    </ul>

    <div v-if="hasMore" class="mt-6 flex justify-center">
      <button
        :disabled="loadingMore"
        @click="load(page + 1)"
        class="px-4 py-2 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded text-sm disabled:opacity-50 inline-flex items-center gap-2"
      >
        <Spinner v-if="loadingMore" size="sm" />
        <span>{{ loadingMore ? 'loading' : 'load more' }}</span>
      </button>
    </div>

    <p v-if="error" class="text-red-400 text-sm mt-4">{{ error }}</p>
  </section>
</template>
