<script setup lang="ts">
type PostSummary = { id: string; title: string; slug: string; createdAt: string; author: string }
const rpc = useRpc()
const { data: posts } = await useAsyncData('posts', () => rpc.call<PostSummary[]>('posts.list'))
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <h1 class="text-2xl text-green-400 mb-6">$ ls posts/</h1>
    <ul class="space-y-3">
      <li v-for="p in posts" :key="p.id" class="border border-zinc-800 rounded p-4 hover:border-green-700 transition">
        <NuxtLink :to="`/posts/${p.slug}`" class="block">
          <div class="text-lg">{{ p.title }}</div>
          <div class="text-xs text-zinc-500">{{ new Date(p.createdAt).toLocaleDateString() }} · {{ p.author }}</div>
        </NuxtLink>
      </li>
      <li v-if="!posts?.length" class="text-zinc-500">No posts yet.</li>
    </ul>
  </section>
</template>
