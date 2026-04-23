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
    <img :src="src" :alt="block.data.alt" class="rounded border border-zinc-800" />
    <figcaption v-if="block.data.alt" class="text-xs text-zinc-500 mt-1">{{ block.data.alt }}</figcaption>
  </figure>
</template>
