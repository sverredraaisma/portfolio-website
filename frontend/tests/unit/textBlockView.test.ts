import { describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import type { TextBlock } from '~/types/blocks'

const TextBlockView = (await import('~/components/blocks/TextBlockView.vue')).default

describe('<TextBlockView>', () => {
  it('renders bold markdown into a <strong>', async () => {
    const block: TextBlock = { id: '1', type: 'text', data: { markdown: '**hi**' } }
    const w = mount(TextBlockView, { props: { block } })
    await flushPromises()

    expect(w.html()).toContain('<strong>hi</strong>')
  })

  it('strips a <script> tag from the rendered HTML so stored markdown can\'t XSS', async () => {
    const block: TextBlock = {
      id: '1',
      type: 'text',
      data: { markdown: '<script>alert(1)</script>safe text' }
    }
    const w = mount(TextBlockView, { props: { block } })
    await flushPromises()

    expect(w.html()).not.toContain('<script>')
    expect(w.text()).toContain('safe text')
  })

  it('keeps inline code and renders it inside a <code>', async () => {
    const block: TextBlock = { id: '1', type: 'text', data: { markdown: 'use `npm test`' } }
    const w = mount(TextBlockView, { props: { block } })
    await flushPromises()

    expect(w.html()).toContain('<code>npm test</code>')
  })

  it('renders an empty markdown body without crashing', async () => {
    const block: TextBlock = { id: '1', type: 'text', data: { markdown: '' } }
    const w = mount(TextBlockView, { props: { block } })
    await flushPromises()

    expect(w.html()).toContain('prose-text')
  })
})
