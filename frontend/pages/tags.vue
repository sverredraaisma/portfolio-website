<script setup lang="ts">
type TagCount = { tag: string; count: number }

const rpc = useRpc()

const { data: tags } = await useAsyncData<TagCount[]>(
  'tags:all',
  () => rpc.call<TagCount[]>('posts.tags')
)

import { useCanonical } from '~/composables/useCanonical'
const canonicalUrl = useCanonical('/tags')
useHead({
  title: 'tags',
  link: () => canonicalUrl.value ? [{ rel: 'canonical', href: canonicalUrl.value }] : []
})

// Map a tag's count to a font-size step so the cloud reads as a cloud and
// not a uniform list. Steps are coarse on purpose — proportional sizing
// makes single-post tags illegibly tiny.
const TIERS = [
  { min: 8, cls: 'text-2xl font-bold text-cyan-400' },
  { min: 4, cls: 'text-xl text-cyan-500' },
  { min: 2, cls: 'text-base text-cyan-600 dark:text-cyan-400' },
  { min: 1, cls: 'text-sm text-zinc-500' }
]
function tierFor(count: number): string {
  return (TIERS.find(t => count >= t.min) ?? TIERS[TIERS.length - 1]).cls
}
</script>

<template>
  <section class="max-w-3xl mx-auto px-6 py-10">
    <header class="mb-6">
      <h1 class="text-2xl text-cyan-400">$ ls tags/</h1>
      <p class="text-xs text-zinc-500 mt-1">Browse posts by topic.</p>
    </header>

    <div v-if="!tags?.length" class="text-center py-12 text-zinc-500 text-sm">
      <p>Nothing tagged yet.</p>
    </div>

    <div v-else class="flex flex-wrap items-baseline gap-x-4 gap-y-2">
      <span v-for="t in tags" :key="t.tag" class="inline-flex items-baseline gap-1">
        <NuxtLink
          :to="{ path: '/posts', query: { tag: t.tag } }"
          :class="['hover:underline', tierFor(t.count)]"
          :title="`${t.count} ${t.count === 1 ? 'post' : 'posts'}`"
        >
          #{{ t.tag }}
          <sup class="text-xs text-zinc-500 font-normal ml-0.5">{{ t.count }}</sup>
        </NuxtLink>
        <a
          :href="`/rss/${t.tag}.xml`"
          class="text-xs text-zinc-500 hover:text-cyan-400"
          :title="`Subscribe to #${t.tag} (RSS)`"
        >rss</a>
      </span>
    </div>
  </section>
</template>
