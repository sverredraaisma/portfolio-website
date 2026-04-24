<script setup lang="ts">
import type { CodeBlock } from '~/types/blocks'

const props = defineProps<{ block: CodeBlock }>()

// Lazy-imported so highlight.js is only paid for on pages with code blocks.
// We load the common-languages bundle (smaller than the full one) and pass
// the language hint when present, falling back to auto-detect.
const highlighted = ref<string>('')

async function colourise() {
  const code = props.block.data.code ?? ''
  if (!code) { highlighted.value = ''; return }
  try {
    const { default: hljs } = await import('highlight.js/lib/common')
    const lang = props.block.data.language?.trim()
    const result = lang && hljs.getLanguage(lang)
      ? hljs.highlight(code, { language: lang, ignoreIllegals: true })
      : hljs.highlightAuto(code)
    highlighted.value = result.value
  } catch {
    // If highlight.js fails to load (e.g. offline build) fall back to plain
    // text — escapeHtml keeps angle brackets from rendering as tags.
    highlighted.value = escapeHtml(code)
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
}

onMounted(colourise)
watch(() => [props.block.data.code, props.block.data.language], colourise)
</script>

<template>
  <figure class="rounded border border-zinc-300 dark:border-zinc-800 overflow-hidden">
    <figcaption
      v-if="block.data.language"
      class="px-3 py-1 text-xs text-zinc-500 bg-zinc-100 dark:bg-zinc-900 border-b border-zinc-300 dark:border-zinc-800"
    >{{ block.data.language }}</figcaption>
    <pre class="m-0 p-3 text-xs leading-relaxed overflow-x-auto bg-zinc-50 dark:bg-zinc-950"
      ><code v-if="highlighted" class="hljs" v-html="highlighted" /><code v-else>{{ block.data.code }}</code></pre>
  </figure>
</template>

<style>
/* Minimal cyan-tinted highlight theme that works on both light and dark.
   Loaded once globally so subsequent code blocks don't refetch the CSS. */
.hljs                  { color: inherit; background: transparent; }
.hljs-comment,
.hljs-quote            { color: #71717a; font-style: italic; }
.hljs-keyword,
.hljs-selector-tag,
.hljs-literal,
.hljs-meta             { color: #06b6d4; }
.hljs-string,
.hljs-attr,
.hljs-symbol,
.hljs-bullet           { color: #16a34a; }
.dark .hljs-string,
.dark .hljs-attr,
.dark .hljs-symbol,
.dark .hljs-bullet     { color: #4ade80; }
.hljs-number,
.hljs-built_in,
.hljs-builtin-name     { color: #f59e0b; }
.hljs-title,
.hljs-section,
.hljs-name,
.hljs-selector-id,
.hljs-selector-class   { color: #a855f7; }
.hljs-variable,
.hljs-template-variable { color: #ec4899; }
.hljs-type,
.hljs-class .hljs-title { color: #0ea5e9; }
</style>
