<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
import { formatTime } from '~/composables/useDate'

type Comment = {
  id: string
  body: string
  createdAt: string
  author: string
  authorIsAdmin: boolean
}

type CommentsPage = {
  items: Comment[]
  page: number
  pageSize: number
  hasMore: boolean
}

const props = defineProps<{ postId: string }>()
const auth = useAuthStore()
const rpc = useRpc()

const comments = ref<Comment[]>([])
const draft = ref('')
const sending = ref(false)
const error = ref('')
const scroller = ref<HTMLElement | null>(null)

async function load() {
  const page = await rpc.call<CommentsPage>('comments.list', { postId: props.postId })
  comments.value = page.items
}

async function scrollToBottom() {
  await nextTick()
  const el = scroller.value
  if (el) el.scrollTop = el.scrollHeight
}

onMounted(async () => {
  await load()
  await scrollToBottom()
})

watch(() => comments.value.length, () => {
  scrollToBottom()
})

async function send() {
  const body = draft.value.trim()
  if (!body) return
  if (!auth.isAuthenticated) {
    error.value = 'log in to comment'
    return
  }
  sending.value = true
  error.value = ''
  try {
    const c = await rpc.call<Comment>('comments.create', { postId: props.postId, body })
    comments.value.push(c)
    draft.value = ''
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    sending.value = false
  }
}

// Prompt vocab: regular users get a normal $ shell, admins get a root-style
// `[sudo] user@portfolio:~#` prompt as a visual privilege cue.
const isAdmin = computed(() => !!auth.user?.isAdmin)
const promptUser = computed(() => auth.user?.username ?? 'guest')
const promptHost = computed(() => 'portfolio')
const promptTail = computed(() => (isAdmin.value ? '#' : '$'))
</script>

<template>
  <div class="bg-black border border-green-900 rounded p-4 text-sm leading-6 text-green-300">
    <div ref="scroller" class="space-y-1 max-h-80 overflow-y-auto">
      <div v-for="c in comments" :key="c.id">
        <span class="text-green-600">[{{ formatTime(c.createdAt) }}]</span>
        <span :class="c.authorIsAdmin ? 'text-red-400' : 'text-green-600'">
          {{ c.authorIsAdmin ? `root(${c.author})` : c.author }}@portfolio
        </span>
        <span class="text-zinc-500"> &gt; </span>
        <span class="whitespace-pre-wrap">{{ c.body }}</span>
      </div>
      <div v-if="!comments.length" class="text-zinc-600">// no comments. be the first.</div>
    </div>

    <form @submit.prevent="send" class="mt-3 flex items-center">
      <span class="mr-2" :class="isAdmin ? 'text-red-400' : 'text-green-500'">
        <span v-if="isAdmin" class="text-red-500">[sudo]&nbsp;</span>{{ promptUser }}@{{ promptHost }}:~{{ promptTail }}
      </span>
      <input
        v-model="draft"
        :disabled="sending"
        :placeholder="auth.isAuthenticated ? 'type a comment, press enter' : 'log in to comment'"
        class="flex-1 bg-transparent outline-none border-none placeholder-green-800"
      />
      <span class="blink ml-1">█</span>
    </form>

    <p v-if="error" class="text-red-400 text-xs mt-2">{{ error }}</p>
  </div>
</template>
