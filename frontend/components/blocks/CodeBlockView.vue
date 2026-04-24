<script setup lang="ts">
import type { CodeBlock } from '~/types/blocks'
defineProps<{ block: CodeBlock }>()
</script>

<template>
  <figure class="rounded border border-zinc-300 dark:border-zinc-800 overflow-hidden">
    <figcaption
      v-if="block.data.language"
      class="px-3 py-1 text-xs text-zinc-500 bg-zinc-100 dark:bg-zinc-900 border-b border-zinc-300 dark:border-zinc-800"
    >{{ block.data.language }}</figcaption>
    <pre class="m-0 p-3 text-xs leading-relaxed overflow-x-auto bg-zinc-50 dark:bg-zinc-950"><code>{{ block.data.code }}</code></pre>
  </figure>
</template>
