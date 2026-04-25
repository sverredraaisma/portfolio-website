import { describe, expect, it, vi } from 'vitest'
import { bindShortcuts } from '~/composables/useShortcuts'

function makeRouter() {
  const calls: string[] = []
  return {
    calls,
    router: {
      push: (path: string) => { calls.push(path); return Promise.resolve() },
      currentRoute: { value: { path: '/' } }
    } as any
  }
}

function press(key: string, target?: EventTarget) {
  const e = new KeyboardEvent('keydown', { key, bubbles: true })
  if (target) Object.defineProperty(e, 'target', { value: target, enumerable: true })
  window.dispatchEvent(e)
}

describe('bindShortcuts', () => {
  it('navigates on a g+letter sequence within the prefix window', () => {
    const { router, calls } = makeRouter()
    const off = bindShortcuts({ router, toggleHelp: () => {} })

    press('g')
    press('p')

    expect(calls).toEqual(['/posts'])
    off()
  })

  it('opens the help overlay on ?', () => {
    const { router } = makeRouter()
    const toggleHelp = vi.fn()
    const off = bindShortcuts({ router, toggleHelp })

    press('?')

    expect(toggleHelp).toHaveBeenCalledOnce()
    off()
  })

  it('ignores keys originating in form controls so it does not hijack typing', () => {
    const { router, calls } = makeRouter()
    const off = bindShortcuts({ router, toggleHelp: () => {} })
    const input = document.createElement('input')
    document.body.appendChild(input)

    press('g', input)
    press('p', input)

    // no nav while focused in an input
    expect(calls).toEqual([])
    off()
    input.remove()
  })

  it('ignores modifier-prefixed combos so OS shortcuts are unaffected', () => {
    const { router, calls } = makeRouter()
    const off = bindShortcuts({ router, toggleHelp: () => {} })

    const e = new KeyboardEvent('keydown', { key: 'g', ctrlKey: true, bubbles: true })
    window.dispatchEvent(e)

    expect(calls).toEqual([])
    off()
  })

  it('the off() callback unbinds the listener', () => {
    const { router, calls } = makeRouter()
    const off = bindShortcuts({ router, toggleHelp: () => {} })

    off()
    press('g')
    press('p')

    expect(calls).toEqual([])
  })
})
