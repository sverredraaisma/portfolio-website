<script setup lang="ts">
import type { PostDocument } from '~/types/blocks'
import BlockRenderer from '~/components/BlockRenderer.vue'
import TerminalComments from '~/components/TerminalComments.vue'

type PostFull = {
  id: string; title: string; slug: string;
  blocks: PostDocument; createdAt: string; updatedAt: string; author: string
}
const route = useRoute()
const rpc = useRpc()
const { data: post } = await useAsyncData(`post:${route.params.slug}`,
  () => rpc.call<PostFull>('posts.get', { slug: route.params.slug }))
</script>

<template>
  <article v-if="post" class="max-w-3xl mx-auto px-6 py-10">
    <header class="mb-8">
      <h1 class="text-3xl text-green-400">{{ post.title }}</h1>
      <div class="text-xs text-zinc-500 mt-1">
        {{ new Date(post.createdAt).toLocaleDateString() }} · {{ post.author }}
      </div>
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
