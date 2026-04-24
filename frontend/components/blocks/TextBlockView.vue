<script setup lang="ts">
import { marked } from 'marked'
import DOMPurify from 'isomorphic-dompurify'
import type { TextBlock } from '~/types/blocks'

const props = defineProps<{ block: TextBlock }>()

// Render markdown to HTML, then sanitise. Without DOMPurify, raw <script>
// or javascript: hrefs in a stored markdown body would execute on render.
const html = computed(() => {
  const raw = marked.parse(props.block.data.markdown ?? '', { async: false }) as string
  return DOMPurify.sanitize(raw, { USE_PROFILES: { html: true } })
})
</script>

<template>
  <div class="prose-text leading-relaxed" v-html="html" />
</template>

<style scoped>
.prose-text :deep(p)             { margin-bottom: 0.75em; }
.prose-text :deep(strong)        { color: rgb(165 243 252); font-weight: 600; }
.prose-text :deep(a)             { color: rgb(34 211 238); text-decoration: underline; }
.prose-text :deep(code)          { font-family: inherit; padding: 0 0.25em; background: rgba(34,211,238,0.08); border-radius: 0.25em; }
.prose-text :deep(pre)           { padding: 0.75em; background: rgba(0,0,0,0.4); border-radius: 0.375em; overflow-x: auto; }
.prose-text :deep(pre code)      { background: transparent; padding: 0; }
.prose-text :deep(ul)            { list-style: disc; padding-left: 1.25em; margin-bottom: 0.75em; }
.prose-text :deep(ol)            { list-style: decimal; padding-left: 1.25em; margin-bottom: 0.75em; }
.prose-text :deep(blockquote)    { border-left: 3px solid rgba(34,211,238,0.4); padding-left: 0.75em; opacity: 0.8; margin: 0.5em 0; }
</style>
