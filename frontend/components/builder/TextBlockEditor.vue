<script setup lang="ts">
import type { TextBlock } from '~/types/blocks'
const props = defineProps<{ block: TextBlock }>()
const emit = defineEmits<{ (e: 'update', b: TextBlock): void }>()

function set(v: string) { emit('update', { ...props.block, data: { markdown: v } }) }
</script>

<template>
  <textarea
    :value="block.data.markdown"
    @input="set(($event.target as HTMLTextAreaElement).value)"
    rows="5"
    placeholder="write something..."
    class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2 text-sm leading-relaxed"
  />
</template>
