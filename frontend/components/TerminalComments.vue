<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'
import { formatTime } from '~/composables/useDate'

type Comment = {
  id: string
  body: string
  createdAt: string
  author: string
  authorIsAdmin: boolean
  authorId?: string
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
  // If we landed via a #c-<id> hash (e.g. from the moderation queue), scroll
  // and pulse-highlight the matched row instead of bottoming out the list.
  if (typeof window !== 'undefined' && window.location.hash.startsWith('#c-')) {
    await nextTick()
    const el = document.getElementById(window.location.hash.slice(1))
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'center' })
      el.classList.add('comment-pulse')
      setTimeout(() => el.classList.remove('comment-pulse'), 2000)
      return
    }
  }
  await scrollToBottom()
})

watch(() => comments.value.length, () => {
  scrollToBottom()
})

function canDelete(c: Comment) {
  if (!auth.isAuthenticated) return false
  if (auth.user?.isAdmin) return true
  // Best-effort: backend doesn't currently return authorId, so non-admins
  // see their own comments by username match. Safe — the backend re-checks.
  return c.author === auth.user?.username
}

// Edit is author-only on the backend. The check here is just so the button
// doesn't show up for admins on other people's rows.
function canEdit(c: Comment) {
  return auth.isAuthenticated && c.author === auth.user?.username
}

const editingId = ref<string | null>(null)
const editDraft = ref('')
const editError = ref('')
const savingEdit = ref(false)

function startEdit(c: Comment) {
  editingId.value = c.id
  editDraft.value = c.body
  editError.value = ''
}
function cancelEdit() {
  editingId.value = null
  editDraft.value = ''
  editError.value = ''
}

async function saveEdit(c: Comment) {
  const body = editDraft.value.trim()
  if (!body) { editError.value = 'cannot be empty'; return }
  savingEdit.value = true
  editError.value = ''
  try {
    const updated = await rpc.call<Comment>('comments.update', { id: c.id, body })
    const idx = comments.value.findIndex(x => x.id === c.id)
    if (idx >= 0) comments.value[idx] = updated
    cancelEdit()
  } catch (e) {
    editError.value = e instanceof Error ? e.message : String(e)
  } finally {
    savingEdit.value = false
  }
}

async function remove(c: Comment) {
  try {
    await rpc.call<void>('comments.delete', { id: c.id })
    comments.value = comments.value.filter(x => x.id !== c.id)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

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
  <div class="bg-black border border-cyan-900 rounded p-4 text-sm leading-6 text-cyan-300">
    <div ref="scroller" class="space-y-1 max-h-80 overflow-y-auto">
      <div v-for="c in comments" :key="c.id" :id="`c-${c.id}`" class="group flex items-start">
        <div class="flex-1">
          <span class="text-cyan-600">[{{ formatTime(c.createdAt) }}]</span>
          <span :class="c.authorIsAdmin ? 'text-red-400' : 'text-cyan-600'">
            <template v-if="c.author === 'anonymous'">
              {{ c.author }}@portfolio
            </template>
            <NuxtLink
              v-else
              :to="`/u/${c.author}`"
              class="hover:underline focus:underline focus:outline-none"
            >{{ c.authorIsAdmin ? `root(${c.author})` : c.author }}@portfolio</NuxtLink>
          </span>
          <span class="text-zinc-500"> &gt; </span>

          <template v-if="editingId === c.id">
            <form @submit.prevent="saveEdit(c)" class="inline-flex items-center gap-2 w-full">
              <input
                v-model="editDraft"
                :disabled="savingEdit"
                @keyup.escape="cancelEdit"
                class="flex-1 bg-transparent border-b border-cyan-800 text-cyan-300"
                autofocus
              />
              <button :disabled="savingEdit" class="text-xs text-cyan-400 hover:text-cyan-300">save</button>
              <button type="button" @click="cancelEdit" class="text-xs text-zinc-500 hover:text-zinc-300">cancel</button>
            </form>
            <span v-if="editError" class="block text-xs text-red-400">{{ editError }}</span>
          </template>
          <span v-else class="whitespace-pre-wrap">{{ c.body }}</span>
        </div>

        <div v-if="editingId !== c.id" class="ml-2 flex gap-2 opacity-0 group-hover:opacity-100 text-xs">
          <button
            v-if="canEdit(c)"
            @click="startEdit(c)"
            class="text-zinc-700 hover:text-cyan-400"
            title="edit"
          >✎</button>
          <button
            v-if="canDelete(c)"
            @click="remove(c)"
            class="text-zinc-700 hover:text-red-400"
            :title="auth.user?.isAdmin && c.author !== auth.user?.username ? 'delete (mod)' : 'delete'"
          >✕</button>
        </div>
      </div>
      <div v-if="!comments.length" class="text-zinc-600">// no comments. be the first.</div>
    </div>

    <form @submit.prevent="send" class="mt-3 flex items-center">
      <span class="mr-2" :class="isAdmin ? 'text-red-400' : 'text-cyan-500'">
        <span v-if="isAdmin" class="text-red-500">[sudo]&nbsp;</span>{{ promptUser }}@{{ promptHost }}:~{{ promptTail }}
      </span>
      <input
        v-model="draft"
        :disabled="sending"
        :placeholder="auth.isAuthenticated ? 'type a comment, press enter' : 'log in to comment'"
        class="flex-1 bg-transparent border-none placeholder-cyan-800"
      />
      <span class="blink ml-1">█</span>
    </form>

    <p v-if="error" class="text-red-400 text-xs mt-2">{{ error }}</p>
  </div>
</template>

<style scoped>
/* Two-second cyan flash so a moderator landing via #c-<id> can spot the row.
   Defined locally so it doesn't leak elsewhere. */
:deep(.comment-pulse) {
  animation: pulse 1.8s ease-out;
  border-radius: 4px;
}
@keyframes pulse {
  0%   { background-color: rgba(34, 211, 238, 0.4); }
  100% { background-color: transparent; }
}
</style>
