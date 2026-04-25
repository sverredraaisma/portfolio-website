<script setup lang="ts">
import { useCanonical } from '~/composables/useCanonical'

const lines = [
  'BOOTING SVERRE.OS v1.0...',
  'LOADING PERSONALITY MODULE..............[ OK ]',
  'INITIALISING PROJECT INDEX..............[ OK ]',
  'RESUPPLYING WHITE MONSTER RESERVES......[ OK ]',
  '',
  'WELCOME, GUEST.',
  '',
  'TYPE `help` FOR THE COMMAND LIST.',
]
const shown = ref<string[]>([])
onMounted(async () => {
  for (const l of lines) {
    shown.value.push(l)
    await new Promise(r => setTimeout(r, 180))
  }
})
const router = useRouter()
const cmd = ref('')

// Keep this list in sync with the switch below — every routable
// command appears here so `help` doesn't lie.
const HELP = '> commands: posts, tags, map, random, verify, privacy, about, clear, help'

function run() {
  const c = cmd.value.trim().toLowerCase()
  cmd.value = ''
  if (!c) return
  switch (c) {
    case 'posts':   router.push('/posts'); break
    case 'tags':    router.push('/tags'); break
    case 'map':     router.push('/map'); break
    case 'random':  router.push('/posts/random'); break
    case 'verify':  router.push('/verify-statement'); break
    case 'privacy': router.push('/privacy'); break
    case 'about':   shown.value.push('> Hi, I make things. Software, hardware, mistakes.'); break
    case 'help':    shown.value.push(HELP); break
    case 'clear':   shown.value = []; break
    default:        shown.value.push(`> command not found: ${c}. type 'help'.`)
  }
}

const canonicalUrl = useCanonical('/')
useSeoMeta({
  title: 'sverre.dev',
  description: 'Personal site of Sverre — posts, projects, and a Falcon-512 signed proof line.'
})
useHead({
  link: () => canonicalUrl.value ? [{ rel: 'canonical', href: canonicalUrl.value }] : []
})
</script>

<template>
  <section class="crt min-h-[calc(100vh-49px)] px-6 py-10">
    <div class="relative z-10 max-w-3xl mx-auto">
      <pre class="text-xs sm:text-sm leading-6 whitespace-pre-wrap">{{ shown.join('\n') }}</pre>
      <div class="mt-2 flex items-center text-sm">
        <span class="mr-2">guest@sverre:~$</span>
        <input
          v-model="cmd"
          @keyup.enter="run"
          class="flex-1 bg-transparent border-none text-cyan-300 placeholder-cyan-700"
          placeholder="type 'help'"
          autofocus
        />
        <span class="blink ml-1">█</span>
      </div>
    </div>
  </section>
</template>
