<script setup lang="ts">
import type { NuxtError } from '#app'

const props = defineProps<{ error: NuxtError }>()

const code = computed(() => props.error?.statusCode ?? 500)
const message = computed(() => {
  if (code.value === 404) return 'segmentation fault: route not found'
  if (code.value === 403) return 'permission denied'
  if (code.value === 401) return 'authentication required'
  return 'kernel panic: an unexpected error occurred'
})

function home() {
  clearError({ redirect: '/' })
}
</script>

<template>
  <section class="min-h-screen flex items-center justify-center px-6 bg-black text-green-300">
    <div class="max-w-lg w-full">
      <pre class="text-xs leading-5 text-green-600">
$ cat /var/log/error.log
[ERR {{ code }}] {{ message }}
[hint] check the URL or return to the home directory
      </pre>
      <div class="mt-4 flex items-center gap-3 text-sm">
        <button
          @click="home"
          class="px-3 py-1 bg-green-600 hover:bg-green-500 text-black font-bold rounded"
        >cd ~</button>
        <span class="text-zinc-500">or</span>
        <NuxtLink to="/posts" class="hover:text-green-400">$ ls posts/</NuxtLink>
      </div>
    </div>
  </section>
</template>
