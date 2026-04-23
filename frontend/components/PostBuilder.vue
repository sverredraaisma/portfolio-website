<script setup lang="ts">
import { type Block, type PostDocument, newId } from '~/types/blocks'
import HeaderBlockEditor from '~/components/builder/HeaderBlockEditor.vue'
import TextBlockEditor from '~/components/builder/TextBlockEditor.vue'
import ImageBlockEditor from '~/components/builder/ImageBlockEditor.vue'

const props = defineProps<{ modelValue: PostDocument }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: PostDocument): void }>()

function update(blocks: Block[]) {
  emit('update:modelValue', { blocks })
}

function add(type: Block['type']) {
  const next: Block =
    type === 'header' ? { id: newId(), type: 'header', data: { text: '', level: 2 } } :
    type === 'text'   ? { id: newId(), type: 'text',   data: { markdown: '' } } :
                        { id: newId(), type: 'image',  data: { src: '', alt: '' } }
  update([...props.modelValue.blocks, next])
}

function patch(id: string, patcher: (b: Block) => Block) {
  update(props.modelValue.blocks.map(b => b.id === id ? patcher(b) : b))
}

function remove(id: string) {
  update(props.modelValue.blocks.filter(b => b.id !== id))
}

function move(id: string, dir: -1 | 1) {
  const arr = [...props.modelValue.blocks]
  const i = arr.findIndex(b => b.id === id)
  const j = i + dir
  if (i < 0 || j < 0 || j >= arr.length) return
  ;[arr[i], arr[j]] = [arr[j], arr[i]]
  update(arr)
}
</script>

<template>
  <div class="space-y-3">
    <div v-for="b in modelValue.blocks" :key="b.id" class="border border-zinc-800 rounded p-3">
      <div class="flex justify-between items-center mb-2">
        <span class="text-xs text-zinc-500 uppercase">{{ b.type }}</span>
        <div class="flex gap-1 text-xs">
          <button class="px-2 py-1 bg-zinc-800 rounded" @click="move(b.id, -1)">↑</button>
          <button class="px-2 py-1 bg-zinc-800 rounded" @click="move(b.id, 1)">↓</button>
          <button class="px-2 py-1 bg-red-900 rounded" @click="remove(b.id)">✕</button>
        </div>
      </div>
      <HeaderBlockEditor v-if="b.type === 'header'" :block="b" @update="(nb) => patch(b.id, () => nb)" />
      <TextBlockEditor   v-else-if="b.type === 'text'"  :block="b" @update="(nb) => patch(b.id, () => nb)" />
      <ImageBlockEditor  v-else-if="b.type === 'image'" :block="b" @update="(nb) => patch(b.id, () => nb)" />
    </div>

    <div class="flex gap-2 text-sm">
      <button class="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 rounded" @click="add('header')">+ header</button>
      <button class="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 rounded" @click="add('text')">+ text</button>
      <button class="px-3 py-1 bg-zinc-800 hover:bg-zinc-700 rounded" @click="add('image')">+ image</button>
    </div>
  </div>
</template>
