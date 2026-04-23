export default defineNuxtConfig({
  compatibilityDate: '2025-01-01',
  devtools: { enabled: true },
  modules: ['@nuxtjs/tailwindcss', '@pinia/nuxt'],
  css: ['~/assets/css/main.css'],
  runtimeConfig: {
    // Server-only: used during SSR. In docker compose this points at
    // the backend service over the internal network (e.g. http://backend:8080).
    apiBaseInternal: process.env.NUXT_API_BASE_INTERNAL || '',
    public: {
      // Sent to the browser. Must be reachable from the user's machine.
      apiBase: process.env.NUXT_PUBLIC_API_BASE || 'http://localhost:5080'
    }
  },
  app: {
    head: {
      title: 'Portfolio',
      meta: [
        { name: 'viewport', content: 'width=device-width, initial-scale=1' }
      ]
    }
  }
})
