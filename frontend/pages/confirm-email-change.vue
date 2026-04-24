<script setup lang="ts">
import { useAuthStore } from '~/stores/auth'

const route = useRoute()
const router = useRouter()
const rpc = useRpc()
const auth = useAuthStore()

const status = ref<'pending' | 'ok' | 'fail'>('pending')

onMounted(async () => {
  const token = String(route.query.token ?? '')
  if (!token) { status.value = 'fail'; return }
  try {
    const res = await rpc.call<{ verified: boolean }>('auth.confirmEmailChange', { token })
    if (res.verified) {
      // The server revokes every active session as part of the swap; clear
      // the local copy so we don't keep flashing a stale token at the API.
      auth.logout()
      status.value = 'ok'
    } else {
      status.value = 'fail'
    }
  } catch {
    status.value = 'fail'
  }
})

function goLogin() {
  router.push('/login')
}
</script>

<template>
  <section class="max-w-md mx-auto px-6 py-16 text-center space-y-3">
    <p v-if="status === 'pending'">applying email change...</p>
    <template v-else-if="status === 'ok'">
      <p class="text-cyan-400">Email updated. For your safety every session was signed out.</p>
      <button
        @click="goLogin"
        class="bg-cyan-600 hover:bg-cyan-500 text-black font-bold rounded px-4 py-2 text-sm"
      >log in with the new address</button>
    </template>
    <p v-else class="text-red-400">Email change failed. The link may have expired or already been used.</p>
  </section>
</template>
