<script setup lang="ts">
import { formatTime } from '~/composables/useDate'

type PostSummary = { id: string; title: string; slug: string; createdAt: string; author: string }
type PostsPage = { items: PostSummary[]; page: number; pageSize: number; hasMore: boolean }

const PAGE_SIZE = 20
const rpc = useRpc()

const posts = ref<PostSummary[]>([])
const page = ref(1)
const hasMore = ref(false)
const loadingMore = ref(false)

const { data: initial } = await useAsyncData<PostsPage>('posts:1',
  () => rpc.call<PostsPage>('posts.list', { page: 1, pageSize: PAGE_SIZE }))

if (initial.value) {
  posts.value = initial.value.items
  page.value = initial.value.page
  hasMore.value = initial.value.hasMore
}

async function loadMore() {
  if (loadingMore.value || !hasMore.value) return
  loadingMore.value = true
  try {
    const next = await rpc.call<PostsPage>('posts.list', {
      page: page.value + 1,
      pageSize: PAGE_SIZE
    })
    posts.value = [...posts.value, ...next.items]
    page.value = next.page
    hasMore.value = next.hasMore
  } finally {
    loadingMore.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <h1 class="text-2xl text-green-400 mb-6">$ ls posts/</h1>
    <ul class="space-y-3">
      <li v-for="p in posts" :key="p.id" class="border border-zinc-800 rounded p-4 hover:border-green-700 transition">
        <NuxtLink :to="`/posts/${p.slug}`" class="block">
          <div class="text-lg">{{ p.title }}</div>
          <div class="text-xs text-zinc-500">{{ formatTime(p.createdAt) }} · {{ p.author }}</div>
        </NuxtLink>
      </li>
      <li v-if="!posts.length" class="text-zinc-500">No posts yet.</li>
    </ul>

    <div v-if="hasMore" class="mt-6 flex justify-center">
      <button
        :disabled="loadingMore"
        @click="loadMore"
        class="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 rounded text-sm disabled:opacity-50"
      >
        {{ loadingMore ? '...' : 'load more' }}
      </button>
    </div>
  </section>
</template>
