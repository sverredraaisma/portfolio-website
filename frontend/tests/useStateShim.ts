import { ref } from 'vue'

// Module-level cache keyed by the same string Nuxt's useState uses. Without
// this, every call to useState() in a test would hand back a fresh ref, so
// the component-under-test and the test body would each be looking at their
// own copy of the "shared" state.
//
// The cache is process-global; tests that need a clean slate should reset
// the relevant key directly. resetUseState(key) is exposed for that.

const store = new Map<string, ReturnType<typeof ref>>()

;(globalThis as any).useState = (key: string, init: () => any) => {
  if (!store.has(key)) store.set(key, ref(init()))
  return store.get(key)
}

export function resetUseState(key?: string) {
  if (key) store.delete(key)
  else store.clear()
}
