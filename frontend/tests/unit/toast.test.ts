import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetUseState } from '~/tests/useStateShim'

const { useToast } = await import('~/composables/useToast')

describe('useToast', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    resetUseState('toasts')
  })
  afterEach(() => vi.useRealTimers())

  it('adds toasts to the queue under the right kind', () => {
    const t = useToast()
    t.success('saved')
    t.error('boom')
    t.info('fyi')

    expect(t.toasts.value).toHaveLength(3)
    expect(t.toasts.value.map(x => x.kind)).toEqual(['success', 'error', 'info'])
  })

  it('auto-dismisses after the kind-specific TTL', () => {
    const t = useToast()
    t.success('saved')
    expect(t.toasts.value).toHaveLength(1)

    vi.advanceTimersByTime(2999)
    expect(t.toasts.value).toHaveLength(1, 'still alive just before the 3s mark')

    vi.advanceTimersByTime(2)
    expect(t.toasts.value).toHaveLength(0)
  })

  it('error toasts stay around longer than success toasts', () => {
    const t = useToast()
    const successId = t.success('ok')
    const errorId = t.error('bad')

    vi.advanceTimersByTime(3500)
    // success (3s) is gone; error (6s) still standing.
    expect(t.toasts.value.map(x => x.id)).toEqual([errorId])

    vi.advanceTimersByTime(3000)
    expect(t.toasts.value).toHaveLength(0)
  })

  it('dismiss removes the toast immediately', () => {
    const t = useToast()
    const id = t.info('hi')

    t.dismiss(id)

    expect(t.toasts.value).toHaveLength(0)
  })
})
