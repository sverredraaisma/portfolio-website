<script setup lang="ts">
import type { Block } from '~/types/blocks'
const props = defineProps<{ block: Block }>()
const config = useRuntimeConfig()

function imgSrc(src: string) {
  return src.startsWith('http') ? src : `${config.public.apiBase}${src}`
}
</script>

<template>
  <div>
    <h2 v-if="block.type === 'header' && block.data.level === 1" class="text-3xl text-green-300">{{ block.data.text }}</h2>
    <h3 v-else-if="block.type === 'header' && block.data.level === 2" class="text-2xl text-green-300">{{ block.data.text }}</h3>
    <h4 v-else-if="block.type === 'header'" class="text-xl text-green-300">{{ block.data.text }}</h4>

    <p v-else-if="block.type === 'text'" class="whitespace-pre-wrap leading-relaxed">{{ block.data.markdown }}</p>

    <figure v-else-if="block.type === 'image'">
      <img :src="imgSrc(block.data.src)" :alt="block.data.alt" class="rounded border border-zinc-800" />
      <figcaption v-if="block.data.alt" class="text-xs text-zinc-500 mt-1">{{ block.data.alt }}</figcaption>
    </figure>
  </div>
</template>
