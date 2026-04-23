<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'

type Comment = { id: string; body: string; createdAt: string; author: string }

const props = defineProps<{ postId: string }>()
const auth = useAuthStore()
const rpc = useRpc()

const comments = ref<Comment[]>([])
const draft = ref('')
const sending = ref(false)
const error = ref('')

async function load() {
  comments.value = await rpc.call<Comment[]>('comments.list', { postId: props.postId })
}
onMounted(load)

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
  } catch (e: any) {
    error.value = e.message
  } finally {
    sending.value = false
  }
}

function ts(iso: string) {
  return new Date(iso).toLocaleString()
}
</script>

<template>
  <div class="bg-black border border-green-900 rounded p-4 text-sm leading-6 text-green-300">
    <div class="space-y-1">
      <div v-for="c in comments" :key="c.id">
        <span class="text-green-600">[{{ ts(c.createdAt) }}] {{ c.author }}@portfolio</span>
        <span class="text-zinc-500"> &gt; </span>
        <span class="whitespace-pre-wrap">{{ c.body }}</span>
      </div>
      <div v-if="!comments.length" class="text-zinc-600">// no comments. be the first.</div>
    </div>

    <form @submit.prevent="send" class="mt-3 flex items-center">
      <span class="mr-2 text-green-500">
        {{ auth.user?.username ?? 'guest' }}@portfolio:~$
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
