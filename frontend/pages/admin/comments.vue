<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'
import { formatTime } from '~/composables/useDate'
import { useToast } from '~/composables/useToast'

const toast = useToast()

definePageMeta({ middleware: 'admin' })

type ModRow = {
  id: string
  body: string
  createdAt: string
  author: string
  authorIsAdmin: boolean
  postId: string
  postTitle: string
  postSlug: string
}
type ModPage = { items: ModRow[]; page: number; pageSize: number; hasMore: boolean }

const PAGE_SIZE = 50
const rpc = useRpc()

const rows = ref<ModRow[]>([])
const page = ref(1)
const hasMore = ref(false)
const loading = ref(true)
const loadingMore = ref(false)
const busyId = ref<string | null>(null)
const error = ref('')

async function load(p = 1) {
  loading.value = p === 1
  loadingMore.value = p > 1
  try {
    const res = await rpc.call<ModPage>('comments.listAll', { page: p, pageSize: PAGE_SIZE })
    rows.value = p === 1 ? res.items : [...rows.value, ...res.items]
    page.value = res.page
    hasMore.value = res.hasMore
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
    loadingMore.value = false
  }
}

async function remove(r: ModRow) {
  if (!confirm(`Delete this comment by ${r.author}?`)) return
  busyId.value = r.id
  try {
    await rpc.call<void>('comments.delete', { id: r.id })
    rows.value = rows.value.filter(x => x.id !== r.id)
    toast.info('Comment deleted.')
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
      <h1 class="text-2xl text-cyan-400">$ admin/comments</h1>
      <NuxtLink to="/admin/posts" class="text-xs text-zinc-500 hover:text-cyan-400">manage posts →</NuxtLink>
    </div>

    <div v-if="loading" class="text-zinc-500 text-sm flex items-center gap-2">
      <Spinner size="sm" /> loading the queue...
    </div>
    <div v-else-if="!rows.length" class="text-center py-12 text-zinc-500">
      <p class="text-base">Inbox zero — nothing to moderate right now.</p>
    </div>

    <ul v-else class="divide-y divide-zinc-200 dark:divide-zinc-800 border border-zinc-300 dark:border-zinc-800 rounded">
      <li v-for="r in rows" :key="r.id" class="px-4 py-3 flex gap-3 items-start">
        <div class="flex-1 min-w-0">
          <div class="text-xs text-zinc-500 flex flex-wrap gap-x-2">
            <span :class="r.authorIsAdmin ? 'text-red-400' : 'text-cyan-400'">
              {{ r.authorIsAdmin ? `root(${r.author})` : r.author }}
            </span>
            <span>·</span>
            <span>{{ formatTime(r.createdAt) }}</span>
            <span>·</span>
            <NuxtLink :to="`/posts/${r.postSlug}#c-${r.id}`" class="hover:text-cyan-400 truncate">
              on "{{ r.postTitle }}"
            </NuxtLink>
          </div>
          <p class="mt-1 text-sm whitespace-pre-wrap break-words">{{ r.body }}</p>
        </div>
        <button
          :disabled="busyId === r.id"
          @click="remove(r)"
          class="text-xs px-2 py-1 rounded border border-red-300 dark:border-red-900 text-red-400 hover:border-red-500 disabled:opacity-50"
        >✕ delete</button>
      </li>
    </ul>

    <div v-if="hasMore" class="mt-6 flex justify-center">
      <button
        :disabled="loadingMore"
        @click="load(page + 1)"
        class="px-4 py-2 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded text-sm disabled:opacity-50"
      >{{ loadingMore ? '...' : 'load more' }}</button>
    </div>

    <p v-if="error" class="text-red-400 text-sm mt-4">{{ error }}</p>
  </section>
</template>
