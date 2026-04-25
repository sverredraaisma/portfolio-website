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
      ],
      link: [
        // Feed readers auto-discover /rss.xml from this <link>.
        { rel: 'alternate', type: 'application/rss+xml', title: 'sverre.dev posts', href: '/rss.xml' }
      ],
      script: [
        // Pre-hydration theme set: reads localStorage *before* Vue runs so a
        // light-mode user doesn't see the SSR dark default flash to light on
        // mount. Mirrors useTheme.applyToDocument — keep the keys in sync.
        // tagPosition: 'head' so the script runs as early as possible (after
        // <html> but before body content paints).
        {
          tagPosition: 'head',
          innerHTML:
            ";(function(){try{var s=localStorage.getItem('theme');" +
            "var d=window.matchMedia&&window.matchMedia('(prefers-color-scheme: light)').matches?'light':'dark';" +
            "var t=(s==='light'||s==='dark')?s:d;" +
            "document.documentElement.classList.toggle('dark',t==='dark');" +
            "document.documentElement.style.colorScheme=t;}catch(_){}})();"
        }
      ]
    }
  }
})
