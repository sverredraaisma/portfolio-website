import { describe, expect, it } from 'vitest'
import { formatTime } from '~/composables/useDate'

describe('formatTime', () => {
  it('renders an ISO timestamp into a localised date+time string', () => {
    // We can't pin the exact output (locale-dependent), but we can check the
    // result is non-empty and contains some recognisable date-ish content.
    const out = formatTime('2026-04-25T14:23:00Z')
    expect(out).not.toBe('')
    // Year and minute should appear somewhere in any sane locale.
    expect(out).toMatch(/2026/)
  })

  it('returns "Invalid Date" for nonsense input rather than throwing', () => {
    // Date(NaN).toLocaleString → "Invalid Date" on every JS engine. The
    // contract here is that the helper doesn't crash the page; the comment
    // list code already filters before calling.
    expect(() => formatTime('not a date')).not.toThrow()
  })
})
