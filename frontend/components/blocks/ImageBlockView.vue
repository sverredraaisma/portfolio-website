<script setup lang="ts">
import type { ImageBlock } from '~/types/blocks'

const props = defineProps<{ block: ImageBlock }>()
const config = useRuntimeConfig()

const src = computed(() =>
  props.block.data.src.startsWith('http')
    ? props.block.data.src
    : `${config.public.apiBase}${props.block.data.src}`
)
</script>

<template>
  <figure>
    <!-- loading="lazy": below-the-fold images don't fetch until scrolled
         near. decoding="async": browser can decode off the main thread,
         doesn't block paint. Both opt-in attributes; no JS, no listeners. -->
    <img
      :src="src"
      :alt="block.data.alt"
      loading="lazy"
      decoding="async"
      class="rounded border border-zinc-800"
    />
    <figcaption v-if="block.data.alt" class="text-xs text-zinc-500 mt-1">{{ block.data.alt }}</figcaption>
  </figure>
</template>
