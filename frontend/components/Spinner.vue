<script setup lang="ts">
// Inherits text colour from the parent so it can sit inside any button or
// status text. The animation lives in main.css so it survives if Tailwind's
// JIT misses a one-off use site.
withDefaults(defineProps<{ label?: string; size?: 'xs' | 'sm' | 'md' }>(), {
  label: 'loading',
  size: 'sm'
})
</script>

<template>
  <span :class="['inline-flex items-center gap-1 align-middle', {
    'text-xs': size === 'xs',
    'text-sm': size === 'sm',
    'text-base': size === 'md'
  }]" role="status">
    <span class="ui-spinner" aria-hidden="true" />
    <span class="sr-only">{{ label }}</span>
  </span>
</template>
