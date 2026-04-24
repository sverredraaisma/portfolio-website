<script setup lang="ts">
import Spinner from '~/components/Spinner.vue'

const route = useRoute()
const rpc = useRpc()
const status = ref<'pending' | 'ok' | 'fail'>('pending')

onMounted(async () => {
  const token = String(route.query.token ?? '')
  if (!token) { status.value = 'fail'; return }
  try {
    const res = await rpc.call<{ verified: boolean }>('auth.verifyEmail', { token })
    status.value = res.verified ? 'ok' : 'fail'
  } catch {
    status.value = 'fail'
  }
})
</script>

<template>
  <section class="max-w-md mx-auto px-6 py-16 text-center space-y-3">
    <div v-if="status === 'pending'" class="text-zinc-500 inline-flex items-center gap-2">
      <Spinner size="md" /> verifying your email...
    </div>
    <template v-else-if="status === 'ok'">
      <p class="text-cyan-500 dark:text-cyan-400 text-lg font-bold">✓ email verified</p>
      <p class="text-sm text-zinc-500">
        You're set. <NuxtLink to="/login" class="underline hover:text-cyan-400">Log in</NuxtLink>
        and start commenting.
      </p>
    </template>
    <template v-else>
      <p class="text-red-500 text-lg font-bold">verification failed</p>
      <p class="text-sm text-zinc-500">The link may have expired or already been used.</p>
      <p class="text-sm text-zinc-500">
        <NuxtLink to="/login" class="underline hover:text-cyan-400">Log in</NuxtLink>
        and we can send a fresh link.
      </p>
    </template>
  </section>
</template>
