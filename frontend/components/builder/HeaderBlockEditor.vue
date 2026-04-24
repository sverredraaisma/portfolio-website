<script setup lang="ts">
import type { HeaderBlock } from '~/types/blocks'
const props = defineProps<{ block: HeaderBlock }>()
const emit = defineEmits<{ (e: 'update', b: HeaderBlock): void }>()

function setText(v: string) { emit('update', { ...props.block, data: { ...props.block.data, text: v } }) }
function setLevel(v: number) {
  const lvl = (v === 1 || v === 2 || v === 3) ? v : 2
  emit('update', { ...props.block, data: { ...props.block.data, level: lvl as 1 | 2 | 3 } })
}
</script>

<template>
  <div class="flex gap-2">
    <select :value="block.data.level" @change="setLevel(Number(($event.target as HTMLSelectElement).value))"
      class="bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-2 py-1 text-sm">
      <option :value="1">H1</option>
      <option :value="2">H2</option>
      <option :value="3">H3</option>
    </select>
    <input :value="block.data.text" @input="setText(($event.target as HTMLInputElement).value)"
      placeholder="header text"
      class="flex-1 bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-sm" />
  </div>
</template>
