<script setup lang="ts">
// "Surprise me" — picks a random published post and redirects.
// SSR-friendly: navigateTo on the server emits a 302 instead of
// rendering the loader. Anonymous callers are fine — the RPC is
// public.
const rpc = useRpc()
const { data } = await useAsyncData<{ slug: string | null }>(
  'posts:random',
  () => rpc.call<{ slug: string | null }>('posts.random')
)

if (data.value?.slug) {
  await navigateTo(`/posts/${data.value.slug}`, { redirectCode: 302 })
}

useHead({
  title: 'random post',
  meta: [{ name: 'robots', content: 'noindex' }]
})
</script>

<template>
  <section class="max-w-md mx-auto px-6 py-16 text-center text-zinc-500">
    <p>No posts yet — nothing random to send you to.</p>
    <NuxtLink to="/posts" class="text-cyan-500 hover:text-cyan-400 underline">browse the (empty) archive</NuxtLink>
  </section>
</template>
