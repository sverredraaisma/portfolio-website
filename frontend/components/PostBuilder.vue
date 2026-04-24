<script setup lang="ts">
import { type Block, type PostDocument, newId } from '~/types/blocks'

const props = defineProps<{ modelValue: PostDocument }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: PostDocument): void }>()

const registry = useBlocks()

function update(blocks: Block[]) {
  emit('update:modelValue', { blocks })
}

function add(type: Block['type']) {
  const next: Block =
    type === 'header' ? { id: newId(), type: 'header', data: { text: '', level: 2 } } :
    type === 'text'   ? { id: newId(), type: 'text',   data: { markdown: '' } } :
    type === 'code'   ? { id: newId(), type: 'code',   data: { code: '', language: '' } } :
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

// --- Drag and drop ---------------------------------------------------------
//
// HTML5 native DnD only — no extra dep. The drag is initiated from a small
// handle on each block (so the user doesn't accidentally start a drag while
// trying to select text in a textarea). On drop, the dragged block moves to
// the position of the drop target. Up/down buttons stay for keyboard users.

const draggingId = ref<string | null>(null)
const dropTargetId = ref<string | null>(null)

function onDragStart(e: DragEvent, id: string) {
  draggingId.value = id
  if (e.dataTransfer) {
    // We carry the id in dataTransfer for cross-component scenarios, but
    // resolve from the ref locally — that survives data-transfer wipes that
    // some browsers do for certain drop targets.
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', id)
  }
}

function onDragOver(e: DragEvent, overId: string) {
  if (!draggingId.value || draggingId.value === overId) return
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
  dropTargetId.value = overId
}

function onDrop(e: DragEvent, targetId: string) {
  e.preventDefault()
  const sourceId = draggingId.value
  draggingId.value = null
  dropTargetId.value = null
  if (!sourceId || sourceId === targetId) return

  const arr = [...props.modelValue.blocks]
  const from = arr.findIndex(b => b.id === sourceId)
  const to = arr.findIndex(b => b.id === targetId)
  if (from < 0 || to < 0) return
  const [moved] = arr.splice(from, 1)
  arr.splice(to, 0, moved)
  update(arr)
}

function onDragEnd() {
  draggingId.value = null
  dropTargetId.value = null
}
</script>

<template>
  <div class="space-y-3">
    <div
      v-if="!modelValue.blocks.length"
      class="border border-dashed border-zinc-300 dark:border-zinc-700 rounded p-6 text-center text-zinc-500 text-sm"
    >
      No blocks yet — start by adding a header or some text below.
    </div>

    <div
      v-for="(b, i) in modelValue.blocks"
      :key="b.id"
      class="group border rounded p-3 transition"
      :class="[
        draggingId === b.id ? 'opacity-40' : 'opacity-100',
        dropTargetId === b.id ? 'border-cyan-500 ring-2 ring-cyan-500/40' : 'border-zinc-300 dark:border-zinc-800 hover:border-zinc-400 dark:hover:border-zinc-700'
      ]"
      @dragover="onDragOver($event, b.id)"
      @drop="onDrop($event, b.id)"
      @dragend="onDragEnd"
    >
      <div class="flex justify-between items-center mb-2">
        <div class="flex items-center gap-2 text-xs uppercase tracking-wide text-zinc-500">
          <!-- Drag handle. Only this element is draggable so a user can
               freely select text inside the block editors below. -->
          <span
            draggable="true"
            @dragstart="onDragStart($event, b.id)"
            class="cursor-grab active:cursor-grabbing text-zinc-400 hover:text-cyan-500 select-none px-1"
            title="drag to reorder"
            aria-label="drag to reorder"
          >⋮⋮</span>
          <span class="inline-block w-1.5 h-1.5 rounded-full bg-cyan-500 align-middle" />
          {{ b.type }}
        </div>
        <div class="flex gap-1 text-xs opacity-50 group-hover:opacity-100 transition">
          <button
            :disabled="i === 0"
            class="px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500 disabled:opacity-30 disabled:hover:border-zinc-300 disabled:dark:hover:border-zinc-700"
            title="move up"
            @click="move(b.id, -1)"
          >↑</button>
          <button
            :disabled="i === modelValue.blocks.length - 1"
            class="px-2 py-1 rounded border border-zinc-300 dark:border-zinc-700 hover:border-cyan-500 disabled:opacity-30 disabled:hover:border-zinc-300 disabled:dark:hover:border-zinc-700"
            title="move down"
            @click="move(b.id, 1)"
          >↓</button>
          <button
            class="px-2 py-1 rounded border border-red-200 dark:border-red-900 text-red-500 hover:border-red-500"
            title="delete block"
            @click="remove(b.id)"
          >✕</button>
        </div>
      </div>
      <component
        :is="registry[b.type].editor"
        :block="b"
        @update="(nb: Block) => patch(b.id, () => nb)"
      />
    </div>

    <div class="flex gap-2 text-sm flex-wrap pt-2">
      <button class="px-3 py-1 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded" @click="add('header')">+ header</button>
      <button class="px-3 py-1 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded" @click="add('text')">+ text</button>
      <button class="px-3 py-1 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded" @click="add('code')">+ code</button>
      <button class="px-3 py-1 bg-zinc-200 hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700 rounded" @click="add('image')">+ image</button>
    </div>
  </div>
</template>
