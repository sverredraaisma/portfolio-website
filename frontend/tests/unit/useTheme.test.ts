import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// Provide a Nuxt-like useState shim before importing the module under test.
import * as vue from 'vue'
;(globalThis as any).useState = (_key: string, init: () => any) => vue.ref(init())

const { useTheme } = await import('~/composables/useTheme')

describe('useTheme', () => {
  beforeEach(() => {
    localStorage.clear()
    document.documentElement.classList.remove('dark')
    document.documentElement.style.colorScheme = ''
  })

  afterEach(() => vi.restoreAllMocks())

  it('init() picks up a stored preference and applies it to <html>', () => {
    localStorage.setItem('theme', 'light')
    const t = useTheme()

    t.init()

    expect(t.theme.value).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(document.documentElement.style.colorScheme).toBe('light')
  })

  it('init() falls back to the OS preference on first visit', () => {
    // happy-dom doesn't simulate matchMedia by default; install a stub that
    // claims a light preference.
    vi.spyOn(window, 'matchMedia').mockImplementation((q: string) => ({
      matches: q.includes('light'),
      media: q,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      onchange: null,
      dispatchEvent: () => false
    } as any))

    const t = useTheme()
    t.init()

    expect(t.theme.value).toBe('light')
  })

  it('toggle() flips dark→light, persists to localStorage, and updates <html>', () => {
    const t = useTheme()
    t.setTheme('dark')

    t.toggle()

    expect(t.theme.value).toBe('light')
    expect(localStorage.getItem('theme')).toBe('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('setTheme(dark) toggles the .dark class on the html element', () => {
    const t = useTheme()

    t.setTheme('dark')

    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(document.documentElement.style.colorScheme).toBe('dark')
  })
})
