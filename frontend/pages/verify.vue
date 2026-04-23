<script setup lang="ts">
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
  <section class="max-w-md mx-auto px-6 py-16 text-center">
    <p v-if="status === 'pending'">verifying...</p>
    <div v-else-if="status === 'ok'" class="text-green-400">
      Email verified. <NuxtLink to="/login" class="underline">Log in</NuxtLink>.
    </div>
    <p v-else class="text-red-400">Verification failed or link expired.</p>
  </section>
</template>
