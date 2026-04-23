<script setup lang="ts">
import type { Block } from '~/types/blocks'

const props = defineProps<{ block: Block }>()
const registry = useBlocks()
const view = computed(() => registry[props.block.type].view)
</script>

<template>
  <component :is="view" :block="block" />
</template>
