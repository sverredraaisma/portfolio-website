<script setup lang="ts">
import LocationMap from '~/components/LocationMap.vue'
import Spinner from '~/components/Spinner.vue'

type Pin = {
  username: string
  isAdmin: boolean
  latitude: number
  longitude: number
  label?: string | null
  updatedAt: string
}

const rpc = useRpc()
const auth = useAuthStore()

const pins = ref<Pin[]>([])
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  try {
    pins.value = await rpc.call<Pin[]>('location.list')
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    loading.value = false
  }
}

onMounted(load)

useSeoMeta({
  title: 'Map',
  description: 'Where on Earth the people of sverre.dev are right now.'
})

import { useCanonical } from '~/composables/useCanonical'
const canonicalUrl = useCanonical('/map')
useHead({
  link: () => canonicalUrl.value ? [{ rel: 'canonical', href: canonicalUrl.value }] : []
})
</script>

<template>
  <section class="max-w-4xl mx-auto px-6 py-10 space-y-4">
    <header class="flex items-baseline justify-between flex-wrap gap-2">
      <h1 class="text-2xl text-cyan-400">$ map</h1>
      <p class="text-xs text-zinc-500">
        {{ pins.length }} {{ pins.length === 1 ? 'person' : 'people' }} sharing a location
      </p>
    </header>

    <p class="text-xs text-zinc-500 max-w-prose">
      Coordinates are rounded to ~110m before they reach this page so an
      exact home address can't be inferred. Anyone with an account can
      share their location from <NuxtLink to="/account" class="underline hover:text-cyan-400">/account</NuxtLink>;
      it's opt-in and clearable any time.
    </p>

    <div v-if="loading" class="text-zinc-500 inline-flex items-center gap-2">
      <Spinner size="sm" /> loading the map...
    </div>

    <LocationMap v-else :pins="pins" />

    <p v-if="error" class="text-red-400 text-sm">{{ error }}</p>

    <p v-if="!loading && !pins.length && auth.isAuthenticated" class="text-sm text-zinc-500">
      Nobody is sharing yet — be the first from
      <NuxtLink to="/account" class="underline hover:text-cyan-400">your account page</NuxtLink>.
    </p>
  </section>
</template>
