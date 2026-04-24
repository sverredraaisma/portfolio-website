// Nuxt auto-imports `ref`, `computed`, `watch`, `nextTick`, etc. from Vue
// and `useRpc`, `useToast`, etc. from composables — without explicit import
// statements. Component files compile against those globals.
//
// Vitest doesn't run Nuxt's macro pass, so we install the same names on
// globalThis here. Tests import this module once (typically via setup.ts)
// before mounting any component.

import { ref, reactive, computed, watch, watchEffect, nextTick, onMounted, onBeforeUnmount, defineModel } from 'vue'

const globals = globalThis as any

// Vue globals
globals.ref = ref
globals.reactive = reactive
globals.computed = computed
globals.watch = watch
globals.watchEffect = watchEffect
globals.nextTick = nextTick
globals.onMounted = onMounted
globals.onBeforeUnmount = onBeforeUnmount
globals.defineModel = defineModel

// Composable shim — replaced per-test via setMockRpc.
let _mockCall: ((method: string, params?: any) => Promise<any>) | null = null
export function setMockRpc(fn: ((method: string, params?: any) => Promise<any>) | null) {
  _mockCall = fn
}
globals.useRpc = () => ({
  call: async (method: string, params?: any) => {
    if (!_mockCall) throw new Error(`useRpc called without setMockRpc — method '${method}'`)
    return _mockCall(method, params)
  }
})

// useToast shim — uses the same key-cached useState shim if present.
import { useToast as realUseToast } from '~/composables/useToast'
globals.useToast = realUseToast

// useBlocks is a pure registry lookup; safe to expose directly.
import { useBlocks as realUseBlocks } from '~/composables/useBlocks'
globals.useBlocks = realUseBlocks
