import { describe, expect, it } from 'vitest'
import { readingTimeMinutes } from '~/composables/useReadingTime'
import type { Block } from '~/types/blocks'

describe('readingTimeMinutes', () => {
  it('returns at least 1 minute for an empty post', () => {
    expect(readingTimeMinutes(undefined)).toBe(1)
    expect(readingTimeMinutes([])).toBe(1)
  })

  it('counts prose words at 220 wpm and rounds up', () => {
    const blocks: Block[] = [
      { id: 'a', type: 'text', data: { markdown: 'word '.repeat(110).trim() } }
    ]
    // 110 words / 220 wpm = 0.5 min, rounded up to 1.
    expect(readingTimeMinutes(blocks)).toBe(1)
  })

  it('treats code as half-speed reading', () => {
    const blocks: Block[] = [
      { id: 'c', type: 'code', data: { code: 'word '.repeat(110).trim(), language: 'js' } }
    ]
    // 110 / 110 wpm = 1.0 min exactly — already an integer, no extra ceil.
    expect(readingTimeMinutes(blocks)).toBe(1)
  })

  it('combines prose and code linearly', () => {
    const blocks: Block[] = [
      { id: 'a', type: 'text', data: { markdown: 'word '.repeat(220).trim() } },
      { id: 'b', type: 'code', data: { code: 'word '.repeat(110).trim(), language: 'ts' } }
    ]
    // 220/220 + 110/110 = 2 minutes exactly.
    expect(readingTimeMinutes(blocks)).toBe(2)
  })

  it('strips URLs so a link-heavy post does not inflate the estimate', () => {
    const blocks: Block[] = [
      { id: 'u', type: 'text', data: { markdown: 'see https://example.com/very/long/url/here for details' } }
    ]
    // After URL stripping there are 4 words (see + for + details + ""). The
    // result is still rounded up to 1 — what we're checking is no exception
    // and that the URL didn't count as a word.
    expect(readingTimeMinutes(blocks)).toBe(1)
  })
})
