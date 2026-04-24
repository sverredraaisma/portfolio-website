<script setup lang="ts">
import type { CodeBlock } from '~/types/blocks'

const props = defineProps<{ block: CodeBlock }>()
const emit = defineEmits<{ (e: 'update', b: CodeBlock): void }>()

function setCode(v: string) {
  emit('update', { ...props.block, data: { ...props.block.data, code: v } })
}
function setLang(v: string) {
  emit('update', { ...props.block, data: { ...props.block.data, language: v } })
}
</script>

<template>
  <div class="space-y-2">
    <input
      :value="block.data.language"
      @input="setLang(($event.target as HTMLInputElement).value)"
      placeholder="language (e.g. ts, py, sql) — optional"
      class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-xs"
    />
    <textarea
      :value="block.data.code"
      @input="setCode(($event.target as HTMLTextAreaElement).value)"
      rows="8"
      placeholder="paste code..."
      class="w-full bg-white dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-3 py-2 text-xs font-mono leading-relaxed"
    />
  </div>
</template>
