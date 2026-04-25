// Global keyboard shortcuts. The g+letter "go to" pattern (gmail-style)
// means a single binding doesn't collide with browser/page shortcuts.
//
// All bindings ignore key events that originate inside form controls so we
// don't hijack typing. The handler is mounted once from the default layout
// and cleaned up on unmount, so SSR doesn't accidentally bind to anything.

import type { Router } from 'vue-router'

type ShortcutContext = {
  router: Router
  toggleHelp: () => void
}

const G_PREFIX_WINDOW_MS = 800

function isTypingTarget(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  const tag = target.tagName
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true
  if (target.isContentEditable) return true
  return false
}

export function bindShortcuts(ctx: ShortcutContext) {
  if (typeof window === 'undefined') return () => {}

  let gArmedAt = 0

  function focusSearchOnPostsPage() {
    if (ctx.router.currentRoute.value.path !== '/posts') {
      ctx.router.push('/posts')
      // Wait for the page to mount, then focus.
      requestAnimationFrame(() => requestAnimationFrame(() => focusFirstSearchInput()))
    } else {
      focusFirstSearchInput()
    }
  }

  function focusFirstSearchInput() {
    // Posts listing has exactly one text input (the search box).
    const el = document.querySelector<HTMLInputElement>('input[placeholder*="search"]')
    el?.focus()
    el?.select()
  }

  function onKey(e: KeyboardEvent) {
    if (e.metaKey || e.ctrlKey || e.altKey) return
    if (isTypingTarget(e.target)) return

    const key = e.key.toLowerCase()

    // 'g' arms the "go to" prefix. The next letter within G_PREFIX_WINDOW_MS
    // resolves to a route; outside that window it expires.
    if (key === 'g') {
      gArmedAt = Date.now()
      return
    }

    if (gArmedAt && Date.now() - gArmedAt < G_PREFIX_WINDOW_MS) {
      gArmedAt = 0
      switch (key) {
        case 'h': e.preventDefault(); ctx.router.push('/'); return
        case 'p': e.preventDefault(); ctx.router.push('/posts'); return
        case 'a': e.preventDefault(); ctx.router.push('/account'); return
        case 'v': e.preventDefault(); ctx.router.push('/verify-statement'); return
        case 'l': e.preventDefault(); ctx.router.push('/map'); return
        case 'm': e.preventDefault(); ctx.router.push('/admin/comments'); return
        case 'n': e.preventDefault(); ctx.router.push('/posts/new'); return
      }
      return
    }

    // Unprefixed shortcuts.
    if (key === '/') {
      e.preventDefault()
      focusSearchOnPostsPage()
      return
    }
    if (key === '?') {
      e.preventDefault()
      ctx.toggleHelp()
      return
    }
  }

  window.addEventListener('keydown', onKey)
  return () => window.removeEventListener('keydown', onKey)
}

// Static map for the help overlay, kept here so the labels don't drift from
// the bindings.
export const SHORTCUTS = [
  { keys: 'g h', label: 'Go to home' },
  { keys: 'g p', label: 'Go to posts' },
  { keys: 'g l', label: 'Go to the map' },
  { keys: 'g v', label: 'Go to verify-statement' },
  { keys: 'g a', label: 'Go to account (when signed in)' },
  { keys: 'g m', label: 'Go to moderation queue (admin)' },
  { keys: 'g n', label: 'Go to new post (admin)' },
  { keys: '/',   label: 'Focus the search box on /posts' },
  { keys: '?',   label: 'Show this list' }
]
