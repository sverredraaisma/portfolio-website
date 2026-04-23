<script setup lang="ts">
import type { TextBlock } from '~/types/blocks'
defineProps<{ block: TextBlock }>()
</script>

<template>
  <p class="whitespace-pre-wrap leading-relaxed">{{ block.data.markdown }}</p>
</template>
