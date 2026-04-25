<script setup lang="ts">
// "Surprise me" — picks a random published post and redirects.
// Direct rpc.call instead of useAsyncData: a static-key useAsyncData
// would memoise the *first* random slug for the rest of the session
// and every later /posts/random hit would resolve to the same post.
// SSR-friendly: navigateTo on the server emits a 302 instead of
// rendering the loader; the await ensures the redirect happens
// before the response goes out.
const rpc = useRpc()
const result = await rpc.call<{ slug: string | null }>('posts.random')
if (result.slug) {
  await navigateTo(`/posts/${result.slug}`, { redirectCode: 302 })
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
