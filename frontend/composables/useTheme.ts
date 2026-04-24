// Light/dark mode preference. Backed by localStorage so the choice
// survives reloads, but falls back to the OS preference for first visits.
//
// Single source of truth: a Pinia-style state would be overkill here, so we
// expose a state ref via Nuxt's `useState` (which already coalesces across
// components within a request).

export type Theme = 'light' | 'dark'
const STORAGE_KEY = 'theme'

function systemPreference(): Theme {
  if (typeof window === 'undefined') return 'dark'
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark'
}

function readStored(): Theme | null {
  if (typeof window === 'undefined') return null
  const v = window.localStorage.getItem(STORAGE_KEY)
  return v === 'light' || v === 'dark' ? v : null
}

function applyToDocument(theme: Theme) {
  if (typeof document === 'undefined') return
  const root = document.documentElement
  root.classList.toggle('dark', theme === 'dark')
  // Tell native form controls and scrollbars to follow the theme.
  root.style.colorScheme = theme
}

export function useTheme() {
  // SSR default of 'dark' matches our existing visual identity; the client
  // plugin will hydrate to the real value before paint.
  const theme = useState<Theme>('theme', () => 'dark')

  function setTheme(next: Theme) {
    theme.value = next
    if (typeof window !== 'undefined') {
      window.localStorage.setItem(STORAGE_KEY, next)
      applyToDocument(next)
    }
  }

  function toggle() {
    setTheme(theme.value === 'dark' ? 'light' : 'dark')
  }

  function init() {
    const initial = readStored() ?? systemPreference()
    theme.value = initial
    applyToDocument(initial)
  }

  return { theme, setTheme, toggle, init }
}
