import { describe, expect, it } from 'vitest'
import { safeRedirect } from '~/composables/useSafeRedirect'

describe('safeRedirect', () => {
  // ---- Accepts ----------------------------------------------------------

  it.each([
    ['/account'],
    ['/account?tab=privacy'],
    ['/posts/abc#c-123'],
    ['/u/alice'],
    ['/'] // bare root is fine
  ])('accepts same-origin path %s', (path) => {
    expect(safeRedirect(path)).toBe(path)
  })

  // ---- Open-redirect attempts ------------------------------------------

  it.each([
    // Protocol-relative — would resolve to https://evil.com on the user's side.
    ['//evil.example/x'],
    ['//evil.example'],
    // Backslash trick — Chrome normalises to "//evil"
    ['/\\evil.example'],
    ['/\\\\evil.example'],
    // Absolute URLs — first char isn't "/"
    ['https://evil.example/x'],
    ['http://evil.example'],
    ['//evil.example/?x=/safe'],
    // Empty / wrong shape
    [''],
    ['account'],          // missing leading /
    ['javascript:alert(1)'],
    ['data:text/html,x']
  ])('rejects open-redirect attempt %s', (raw) => {
    expect(safeRedirect(raw)).toBeNull()
  })

  // ---- Type / length guards --------------------------------------------

  it.each([
    [null],
    [undefined],
    [123],
    [{ toString: () => '/x' }],     // object passing as string
    [['/x']]                        // array (Vue Router can hand these out for repeated keys)
  ])('rejects non-string input %#', (input) => {
    expect(safeRedirect(input)).toBeNull()
  })

  it('rejects an absurdly long redirect to keep the URL bar bounded', () => {
    const huge = '/' + 'a'.repeat(1024)
    expect(safeRedirect(huge)).toBeNull()
  })
})
