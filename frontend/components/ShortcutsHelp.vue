<script setup lang="ts">
import { SHORTCUTS } from '~/composables/useShortcuts'

const open = defineModel<boolean>({ required: true })
function close() { open.value = false }

// Esc closes the overlay; bound while it's open so it doesn't fight the
// global shortcut listener. immediate:true so a dialog that mounts in the
// open=true state (externally controlled, deep link, etc) still wires the
// listener on first paint instead of waiting for the next toggle.
function onKey(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}
watch(open, (v) => {
  if (typeof window === 'undefined') return
  if (v) window.addEventListener('keydown', onKey)
  else window.removeEventListener('keydown', onKey)
}, { immediate: true })
</script>

<template>
  <Teleport to="body">
    <div
      v-if="open"
      class="fixed inset-0 z-40 bg-black/50 flex items-center justify-center px-4"
      @click.self="close"
    >
      <div
        class="w-full max-w-md bg-white dark:bg-zinc-950 border border-zinc-300 dark:border-zinc-800 rounded p-5 shadow-lg"
        role="dialog"
        aria-label="keyboard shortcuts"
      >
        <div class="flex items-center justify-between mb-3">
          <h2 class="text-cyan-400 text-lg">$ shortcuts</h2>
          <button class="text-zinc-500 hover:text-zinc-300 text-sm" @click="close">esc ✕</button>
        </div>
        <ul class="text-sm divide-y divide-zinc-200 dark:divide-zinc-800">
          <li v-for="s in SHORTCUTS" :key="s.keys" class="flex items-center justify-between py-1.5">
            <span class="text-zinc-600 dark:text-zinc-300">{{ s.label }}</span>
            <kbd class="font-mono text-xs bg-zinc-100 dark:bg-zinc-900 border border-zinc-300 dark:border-zinc-700 rounded px-2 py-0.5">{{ s.keys }}</kbd>
          </li>
        </ul>
      </div>
    </div>
  </Teleport>
</template>
