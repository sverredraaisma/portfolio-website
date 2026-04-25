import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import type { Block } from '~/types/blocks'

const BlockRenderer = (await import('~/components/BlockRenderer.vue')).default

describe('<BlockRenderer>', () => {
  it('renders a header block as the matching <hN>', async () => {
    const block: Block = { id: '1', type: 'header', data: { text: 'Hello', level: 2 } }
    const w = mount(BlockRenderer, { props: { block } })

    expect(w.text()).toContain('Hello')
  })

  it('renders a text block by passing through to TextBlockView', async () => {
    const block: Block = { id: '1', type: 'text', data: { markdown: 'just words' } }
    const w = mount(BlockRenderer, { props: { block } })
    // Wait a tick for the async marked import inside TextBlockView.
    await new Promise(r => setTimeout(r, 30))

    expect(w.html()).toContain('just words')
  })

  it('renders a code block with the language label', async () => {
    const block: Block = { id: '1', type: 'code', data: { code: 'let x = 1', language: 'js' } }
    const w = mount(BlockRenderer, { props: { block } })
    // Wait for highlight.js's lazy import to settle. Once colourised the
    // source text is wrapped in spans, so we assert on .text() — which
    // ignores tags — rather than .html().
    await new Promise(r => setTimeout(r, 100))

    expect(w.html()).toContain('js')
    expect(w.text()).toContain('let x = 1')
  })

  it('renders an image block with src + alt', async () => {
    const block: Block = { id: '1', type: 'image', data: { src: '/m.webp', alt: 'a moon' } }
    const w = mount(BlockRenderer, { props: { block } })

    const img = w.find('img')
    expect(img.exists()).toBe(true)
    expect(img.attributes('alt')).toBe('a moon')
  })
})
