<script setup lang="ts">
import PostBuilder from '~/components/PostBuilder.vue'
import type { PostDocument } from '~/types/blocks'

definePageMeta({ middleware: 'admin' })

const router = useRouter()
const rpc = useRpc()

const title = ref('')
const slug = ref('')
const doc = ref<PostDocument>({ blocks: [] })
const saving = ref(false)
const error = ref('')

async function save(published: boolean) {
  saving.value = true
  error.value = ''
  try {
    const res = await rpc.call<{ slug: string }>('posts.create', {
      title: title.value,
      slug: slug.value || title.value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, ''),
      blocks: doc.value,
      published
    })
    router.push(published ? `/posts/${res.slug}` : '/admin/posts')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <h1 class="text-2xl text-cyan-400 mb-6">$ new post</h1>
    <div class="space-y-3 mb-6">
      <input v-model="title" placeholder="title" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" />
      <input v-model="slug" placeholder="slug (optional)" class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2" />
    </div>

    <PostBuilder v-model="doc" />

    <div class="mt-6 flex items-center gap-3">
      <button :disabled="saving" @click="save(false)" class="bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-700 dark:hover:bg-zinc-600 text-cyan-300 rounded px-4 py-2 disabled:opacity-50">
        {{ saving ? '...' : 'save draft' }}
      </button>
      <button :disabled="saving" @click="save(true)" class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 disabled:opacity-50">
        {{ saving ? '...' : 'publish' }}
      </button>
      <span v-if="error" class="text-red-400 text-sm">{{ error }}</span>
    </div>
  </section>
</template>
