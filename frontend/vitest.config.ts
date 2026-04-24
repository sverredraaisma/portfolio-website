import { defineConfig } from 'vitest/config'
import { fileURLToPath, URL } from 'node:url'

// Vitest with happy-dom — Nuxt isn't loaded for these tests; we only exercise
// pure composables and small components that don't need the framework. This
// keeps the test boot under a second.
//
// The ~ alias mirrors Nuxt's so the tested code can import as it would in the
// app without a fixture wrapper.
export default defineConfig({
  test: {
    environment: 'happy-dom',
    include: ['tests/**/*.test.ts'],
    globals: false
  },
  resolve: {
    alias: {
      '~': fileURLToPath(new URL('./', import.meta.url))
    }
  }
})
