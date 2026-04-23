<script setup lang="ts">
import type { ImageBlock } from '~/types/blocks'

const props = defineProps<{ block: ImageBlock }>()
const emit = defineEmits<{ (e: 'update', b: ImageBlock): void }>()

const rpc = useRpc()
const config = useRuntimeConfig()
const uploading = ref(false)
const error = ref('')

function setAlt(v: string) {
  emit('update', { ...props.block, data: { ...props.block.data, alt: v } })
}

async function onPick(e: Event) {
  const file = (e.target as HTMLInputElement).files?.[0]
  if (!file) return
  uploading.value = true
  error.value = ''
  try {
    const dataBase64 = await toBase64(file)
    const res = await rpc.call<{ url: string }>('posts.uploadImage', { dataBase64 })
    emit('update', { ...props.block, data: { ...props.block.data, src: res.url } })
  } catch (err) {
    error.value = err instanceof Error ? err.message : String(err)
  } finally {
    uploading.value = false
  }
}

function toBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const r = new FileReader()
    r.onload = () => {
      const result = r.result as string
      // strip "data:<mime>;base64," prefix
      resolve(result.slice(result.indexOf(',') + 1))
    }
    r.onerror = () => reject(r.error)
    r.readAsDataURL(file)
  })
}

const previewSrc = computed(() =>
  props.block.data.src.startsWith('http')
    ? props.block.data.src
    : `${config.public.apiBase}${props.block.data.src}`
)
</script>

<template>
  <div class="space-y-2">
    <input type="file" accept="image/*" @change="onPick" class="text-sm" />
    <p v-if="uploading" class="text-xs text-zinc-500">uploading & converting to webp...</p>
    <p v-if="error" class="text-xs text-red-400">{{ error }}</p>
    <img v-if="block.data.src" :src="previewSrc" class="max-h-48 rounded border border-zinc-800" />
    <input
      :value="block.data.alt"
      @input="setAlt(($event.target as HTMLInputElement).value)"
      placeholder="alt text"
      class="w-full bg-zinc-900 border border-zinc-700 rounded px-3 py-2 text-sm"
    />
  </div>
</template>
