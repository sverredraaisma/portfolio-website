<script setup lang="ts">
import type { Block, PostDocument } from '~/types/blocks'
import BlockRenderer from '~/components/BlockRenderer.vue'
import TerminalComments from '~/components/TerminalComments.vue'
import { formatTime } from '~/composables/useDate'
import { readingTimeMinutes } from '~/composables/useReadingTime'
import { useAuthStore } from '~/stores/auth'

type PostFull = {
  id: string; title: string; slug: string;
  blocks: PostDocument; createdAt: string; updatedAt: string; author: string
}
const route = useRoute()
const rpc = useRpc()
const auth = useAuthStore()
const { data: post } = await useAsyncData<PostFull>(`post:${route.params.slug}`,
  () => rpc.call<PostFull>('posts.get', { slug: route.params.slug }))

// Pull a usable description out of the post body: first text block, falling
// back to the first header. Trimmed to a sensible meta length.
function description(blocks: Block[] | undefined): string {
  if (!blocks?.length) return ''
  const text = blocks.find(b => b.type === 'text') as Extract<Block, { type: 'text' }> | undefined
  if (text?.data.markdown) return text.data.markdown.replace(/\s+/g, ' ').slice(0, 200)
  const header = blocks.find(b => b.type === 'header') as Extract<Block, { type: 'header' }> | undefined
  return header?.data.text ?? ''
}

const readMinutes = computed(() => readingTimeMinutes(post.value?.blocks?.blocks))

useSeoMeta({
  title: () => post.value?.title,
  description: () => description(post.value?.blocks?.blocks),
  ogTitle: () => post.value?.title,
  ogDescription: () => description(post.value?.blocks?.blocks),
  ogType: 'article'
})
</script>

<template>
  <article v-if="post" class="max-w-3xl mx-auto px-6 py-10">
    <header class="mb-8 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-3xl text-cyan-400">{{ post.title }}</h1>
        <div class="text-xs text-zinc-500 mt-1">
          {{ formatTime(post.createdAt) }} · {{ post.author }} · ~{{ readMinutes }} min read
        </div>
      </div>
      <NuxtLink
        v-if="auth.user?.isAdmin"
        :to="`/posts/${post.slug}/edit`"
        class="text-xs px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-700 text-zinc-400 hover:text-cyan-400"
      >edit</NuxtLink>
    </header>

    <div class="space-y-6">
      <BlockRenderer v-for="b in post.blocks.blocks" :key="b.id" :block="b" />
    </div>

    <section class="mt-12">
      <h2 class="text-sm text-zinc-500 mb-2">// comments</h2>
      <TerminalComments :post-id="post.id" />
    </section>
  </article>
</template>
