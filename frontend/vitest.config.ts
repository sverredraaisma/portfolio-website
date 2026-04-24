import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

// Vitest with happy-dom — Nuxt isn't loaded for these tests; we only exercise
// pure composables and small components that don't need the framework. This
// keeps the test boot under a second.
//
// The ~ alias mirrors Nuxt's so the tested code can import as it would in the
// app without a fixture wrapper. The Vue plugin lets test files mount .vue
// components via @vue/test-utils.
export default defineConfig({
  plugins: [vue()],
  test: {
    environment: 'happy-dom',
    include: ['tests/**/*.test.ts'],
    globals: false
  },
  resolve: {
    alias: {
      '~': fileURLToPath(new URL('./', import.meta.url))
    }
  },
  // Nuxt sets `import.meta.client` at build time. Vitest doesn't, so the
  // pieces of code that gate browser-only work behind that flag (auth
  // store's localStorage writes, theme plugin) become no-ops under test.
  // Define the flag as true so the gated paths actually execute.
  define: {
    'import.meta.client': 'true',
    'import.meta.server': 'false'
  }
})
