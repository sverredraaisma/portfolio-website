<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'

definePageMeta({ middleware: 'auth' })

type AccountExport = {
  id: string
  username: string
  email: string
  emailVerified: boolean
  isAdmin: boolean
  createdAt: string
  posts: Array<{ id: string; title: string; slug: string; createdAt: string; updatedAt: string; published: boolean }>
  comments: Array<{ id: string; postId: string; body: string; createdAt: string }>
  refreshTokens: Array<{ id: string; createdAt: string; expiresAt: string; revokedAt: string | null }>
}

const auth = useAuthStore()
const rpc = useRpc()
const router = useRouter()

const data = ref<AccountExport | null>(null)
const loading = ref(true)
const error = ref('')

const exporting = ref(false)
const deleting = ref(false)
const commentStrategy = ref<'anonymise' | 'delete'>('anonymise')
const confirmText = ref('')

async function loadExport() {
  loading.value = true
  error.value = ''
  try {
    data.value = await rpc.call<AccountExport>('account.export')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

onMounted(loadExport)

async function downloadExport() {
  exporting.value = true
  error.value = ''
  try {
    const fresh = await rpc.call<AccountExport>('account.export')
    const blob = new Blob([JSON.stringify(fresh, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `account-${fresh.username}-${new Date().toISOString().slice(0, 10)}.json`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    exporting.value = false
  }
}

const expectedConfirm = computed(() => `delete ${data.value?.username ?? ''}`)

async function deleteAccount() {
  if (confirmText.value.trim() !== expectedConfirm.value) {
    error.value = `Type "${expectedConfirm.value}" to confirm.`
    return
  }
  deleting.value = true
  error.value = ''
  try {
    await rpc.call<void>('account.delete', { commentStrategy: commentStrategy.value })
    auth.logout()
    router.push('/?deleted=1')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    deleting.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10 space-y-8">
    <header>
      <h1 class="text-2xl text-cyan-400">$ account</h1>
      <p class="text-xs text-zinc-500 mt-1">
        Your data, your rights. See <NuxtLink to="/privacy" class="hover:text-cyan-400 underline">privacy policy</NuxtLink>.
      </p>
    </header>

    <p v-if="loading" class="text-zinc-500">loading...</p>
    <p v-if="error" class="text-red-400 text-sm">{{ error }}</p>

    <template v-if="data">
      <div class="border border-zinc-300 dark:border-zinc-800 rounded p-4 space-y-1 text-sm">
        <div><span class="text-zinc-500">username:</span> {{ data.username }}</div>
        <div><span class="text-zinc-500">email:</span> {{ data.email }}
          <span v-if="!data.emailVerified" class="text-yellow-500 text-xs">(unverified)</span>
        </div>
        <div><span class="text-zinc-500">role:</span> {{ data.isAdmin ? 'admin' : 'member' }}</div>
        <div><span class="text-zinc-500">joined:</span> {{ new Date(data.createdAt).toLocaleString() }}</div>
        <div><span class="text-zinc-500">posts:</span> {{ data.posts.length }}
          · <span class="text-zinc-500">comments:</span> {{ data.comments.length }}
          · <span class="text-zinc-500">sessions:</span> {{ data.refreshTokens.length }}
        </div>
      </div>

      <section>
        <h2 class="text-lg text-cyan-400 mb-2">$ export</h2>
        <p class="text-xs text-zinc-500 mb-3">
          Download every piece of data tied to your account as a JSON file (AVG art. 15 / 20).
        </p>
        <button
          :disabled="exporting"
          @click="downloadExport"
          class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >{{ exporting ? '...' : 'download my data' }}</button>
      </section>

      <section class="border border-red-300 dark:border-red-900 rounded p-4 space-y-3">
        <h2 class="text-lg text-red-400">$ delete account</h2>
        <p class="text-xs text-zinc-500">
          Removes your profile, all posts you authored, every active session, and your comments
          (per the choice below). This cannot be undone (AVG art. 17).
        </p>

        <div class="space-y-2 text-sm">
          <label class="flex items-start gap-2">
            <input v-model="commentStrategy" type="radio" value="anonymise" class="mt-1" />
            <span>
              <span class="text-cyan-400">anonymise</span> my comments —
              keep the comment body and timestamp but break the link to my account.
              Comments will display as "anonymous".
            </span>
          </label>
          <label class="flex items-start gap-2">
            <input v-model="commentStrategy" type="radio" value="delete" class="mt-1" />
            <span>
              <span class="text-red-400">delete</span> all my comments along with the account.
            </span>
          </label>
        </div>

        <div class="space-y-2">
          <label class="text-xs text-zinc-500">
            Type <code class="bg-zinc-100 dark:bg-zinc-900 px-1 rounded">{{ expectedConfirm }}</code> to confirm:
          </label>
          <input
            v-model="confirmText"
            class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm"
            :placeholder="expectedConfirm"
          />
        </div>

        <button
          :disabled="deleting || confirmText.trim() !== expectedConfirm"
          @click="deleteAccount"
          class="bg-red-700 hover:bg-red-600 text-white rounded px-4 py-2 disabled:opacity-50"
        >{{ deleting ? '...' : 'delete my account permanently' }}</button>
      </section>
    </template>
  </section>
</template>
