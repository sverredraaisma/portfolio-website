<script setup lang="ts">
import PostBuilder from '~/components/PostBuilder.vue'
import type { PostDocument } from '~/types/blocks'
import { useToast } from '~/composables/useToast'

definePageMeta({ middleware: 'admin' })

type PostFull = {
  id: string; title: string; slug: string;
  blocks: PostDocument; createdAt: string; updatedAt: string;
  published: boolean; author: string; tags: string[]
}

const route = useRoute()
const router = useRouter()
const rpc = useRpc()
const toast = useToast()

const id = ref('')
const title = ref('')
const slug = ref('')
const tagsInput = ref('')
const doc = ref<PostDocument>({ blocks: [] })
const published = ref(false)

function parseTags(s: string): string[] {
  return s.split(/[,\s]+/).map(t => t.trim()).filter(Boolean)
}

const loading = ref(true)
const saving = ref(false)
const deleting = ref(false)
const error = ref('')

// Loaded on mount AND on every slug change — without the watcher,
// navigating /posts/a/edit → /posts/b/edit (same component instance,
// different param) re-renders post a's draft, which would let an admin
// publish a body under the wrong slug. Same fix family as /u/[username]
// and /posts/[slug].
async function loadFromSlug(s: string | string[] | undefined) {
  loading.value = true
  error.value = ''
  try {
    const post = await rpc.call<PostFull>('posts.get', { slug: String(s ?? '') })
    id.value = post.id
    title.value = post.title
    slug.value = post.slug
    tagsInput.value = (post.tags ?? []).join(', ')
    doc.value = post.blocks ?? { blocks: [] }
    published.value = post.published
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
    id.value = ''
  } finally {
    loading.value = false
  }
}
watch(() => route.params.slug, loadFromSlug, { immediate: true })

async function save(nextPublished?: boolean) {
  saving.value = true
  error.value = ''
  const target = nextPublished ?? published.value
  try {
    await rpc.call<void>('posts.update', {
      id: id.value,
      title: title.value,
      slug: slug.value,
      blocks: doc.value,
      tags: parseTags(tagsInput.value),
      published: target
    })
    const wasPublished = published.value
    published.value = target
    // Phrase the toast around the transition rather than the final state so
    // "save" on a draft and "publish" on a draft don't both say the same thing.
    if (target !== wasPublished) toast.success(target ? 'Post published.' : 'Post unpublished.')
    else toast.success('Saved.')
    // Navigate to the (possibly-renamed) slug.
    router.push(target ? `/posts/${slug.value}` : `/admin/posts`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!confirm('Delete this post? This cannot be undone.')) return
  deleting.value = true
  error.value = ''
  try {
    await rpc.call<void>('posts.delete', { id: id.value })
    toast.info('Post deleted.')
    router.push('/admin/posts')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <h1 class="text-2xl text-cyan-400 mb-6">$ edit post</h1>

    <p v-if="loading" class="text-zinc-500">loading...</p>

    <template v-else-if="id">
      <div class="space-y-3 mb-6">
        <input v-model="title" placeholder="title" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" />
        <input v-model="slug" placeholder="slug" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" />
        <input v-model="tagsInput" placeholder="tags (comma-separated)" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" />
        <div class="text-xs text-zinc-500">
          status: <span :class="published ? 'text-cyan-400' : 'text-yellow-400'">{{ published ? 'published' : 'draft' }}</span>
        </div>
      </div>

      <PostBuilder v-model="doc" />

      <div class="mt-6 flex flex-wrap items-center gap-3">
        <button
          :disabled="saving || deleting"
          @click="save()"
          class="bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-700 dark:hover:bg-zinc-600 text-cyan-300 rounded px-4 py-2 disabled:opacity-50"
        >
          {{ saving ? '...' : 'save' }}
        </button>

        <button
          v-if="!published"
          :disabled="saving || deleting"
          @click="save(true)"
          class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >
          publish
        </button>
        <button
          v-else
          :disabled="saving || deleting"
          @click="save(false)"
          class="bg-yellow-600 hover:bg-yellow-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50"
        >
          unpublish
        </button>

        <button
          :disabled="saving || deleting"
          @click="remove"
          class="ml-auto bg-red-700 hover:bg-red-600 text-white rounded px-4 py-2 disabled:opacity-50"
        >
          {{ deleting ? '...' : 'delete' }}
        </button>
      </div>

      <p v-if="error" class="text-red-400 text-sm mt-3">{{ error }}</p>
    </template>

    <p v-else-if="error" class="text-red-400">{{ error }}</p>
  </section>
</template>
