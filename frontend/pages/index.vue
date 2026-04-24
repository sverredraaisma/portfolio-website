<script setup lang="ts">
const lines = [
  'BOOTING SVERRE.OS v1.0...',
  'LOADING PERSONALITY MODULE..............[ OK ]',
  'INITIALISING PROJECT INDEX..............[ OK ]',
  'WARMING UP COFFEE SUBSYSTEM.............[ OK ]',
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

const HELP = '> commands: posts, about, verify, privacy, clear, help'

function run() {
  const c = cmd.value.trim().toLowerCase()
  cmd.value = ''
  if (!c) return
  switch (c) {
    case 'posts':   router.push('/posts'); break
    case 'verify':  router.push('/verify-statement'); break
    case 'privacy': router.push('/privacy'); break
    case 'about':   shown.value.push('> Hi, I build things. Frontends, backends, the occasional bad joke.'); break
    case 'help':    shown.value.push(HELP); break
    case 'clear':   shown.value = []; break
    default:        shown.value.push(`> command not found: ${c}. type 'help'.`)
  }
}
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
          class="flex-1 bg-transparent outline-none border-none text-cyan-300 placeholder-cyan-700"
          placeholder="type 'help'"
          autofocus
        />
        <span class="blink ml-1">█</span>
      </div>
    </div>
  </section>
</template>
