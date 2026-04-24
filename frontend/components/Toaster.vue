<script setup lang="ts">
import { useToast } from '~/composables/useToast'

const { toasts, dismiss } = useToast()

// Per-kind chrome. Tailwind-only so we don't pay for a runtime style dep.
const kindClass: Record<string, string> = {
  success: 'border-cyan-400 bg-cyan-50 text-cyan-700 dark:border-cyan-700 dark:bg-cyan-950 dark:text-cyan-300',
  error:   'border-red-400 bg-red-50 text-red-700 dark:border-red-700 dark:bg-red-950 dark:text-red-300',
  info:    'border-zinc-400 bg-white text-zinc-800 dark:border-zinc-600 dark:bg-zinc-900 dark:text-zinc-200'
}
</script>

<template>
  <Teleport to="body">
    <!-- Top-right on desktop, top-center on mobile so a thumb can dismiss without stretching. -->
    <div
      class="fixed z-50 flex flex-col gap-2 pointer-events-none
             top-3 left-3 right-3 sm:left-auto sm:right-4 sm:top-4 sm:max-w-sm"
      role="region"
      aria-label="notifications"
      aria-live="polite"
    >
      <transition-group name="toast" tag="div" class="flex flex-col gap-2">
        <button
          v-for="t in toasts"
          :key="t.id"
          type="button"
          class="pointer-events-auto text-left text-sm rounded border px-3 py-2 shadow-md
                 transition hover:opacity-80 focus-visible:opacity-90 w-full"
          :class="kindClass[t.kind]"
          @click="dismiss(t.id)"
        >
          {{ t.message }}
        </button>
      </transition-group>
    </div>
  </Teleport>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active { transition: all 200ms ease; }
.toast-enter-from   { opacity: 0; transform: translateY(-6px); }
.toast-leave-to     { opacity: 0; transform: translateX(20px); }
</style>
