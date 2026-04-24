<script setup lang="ts">
import type { HeaderBlock } from '~/types/blocks'
defineProps<{ block: HeaderBlock }>()
</script>

<template>
  <div>
    <h2 v-if="block.data.level === 1" class="text-3xl text-cyan-300">{{ block.data.text }}</h2>
    <h3 v-else-if="block.data.level === 2" class="text-2xl text-cyan-300">{{ block.data.text }}</h3>
    <h4 v-else class="text-xl text-cyan-300">{{ block.data.text }}</h4>
  </div>
</template>
