<script setup lang="ts">
import PostBuilder from '~/components/PostBuilder.vue'
import type { PostDocument } from '~/types/blocks'

definePageMeta({ middleware: 'admin' })

type PostFull = {
  id: string; title: string; slug: string;
  blocks: PostDocument; createdAt: string; updatedAt: string;
  published: boolean; author: string
}

const route = useRoute()
const router = useRouter()
const rpc = useRpc()

const id = ref('')
const title = ref('')
const slug = ref('')
const doc = ref<PostDocument>({ blocks: [] })
const published = ref(false)

const loading = ref(true)
const saving = ref(false)
const deleting = ref(false)
const error = ref('')

onMounted(async () => {
  try {
    const post = await rpc.call<PostFull>('posts.get', { slug: route.params.slug })
    id.value = post.id
    title.value = post.title
    slug.value = post.slug
    doc.value = post.blocks ?? { blocks: [] }
    published.value = post.published
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
})

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
      published: target
    })
    published.value = target
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
