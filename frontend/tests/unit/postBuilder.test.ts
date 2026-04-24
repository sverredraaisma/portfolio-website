import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import type { PostDocument } from '~/types/blocks'

const PostBuilder = (await import('~/components/PostBuilder.vue')).default

describe('<PostBuilder>', () => {
  function mountWith(initial: PostDocument = { blocks: [] }) {
    const w = mount(PostBuilder, {
      props: { modelValue: initial, 'onUpdate:modelValue': (v: PostDocument) => w.setProps({ modelValue: v }) }
    })
    return w
  }

  afterEach(() => { document.body.innerHTML = '' })

  it('renders the friendly empty state when there are no blocks', () => {
    const w = mountWith()
    expect(w.text()).toContain('No blocks yet')
  })

  it('renders one editor card per block', async () => {
    const w = mountWith({
      blocks: [
        { id: 'a', type: 'header', data: { text: 'h', level: 1 } },
        { id: 'b', type: 'text',   data: { markdown: 'hi' } }
      ]
    })
    await flushPromises()

    // Two blocks → two type labels visible.
    expect(w.text()).toContain('header')
    expect(w.text()).toContain('text')
  })

  it('emits update:modelValue when "+ header" is clicked, appending a header block', async () => {
    const w = mountWith()

    const header = w.findAll('button').find(b => b.text().includes('+ header'))!
    await header.trigger('click')

    const events = w.emitted('update:modelValue')!
    expect(events).toHaveLength(1)
    const next = events[0][0] as PostDocument
    expect(next.blocks).toHaveLength(1)
    expect(next.blocks[0].type).toBe('header')
  })

  it('+ code creates an empty code block with empty language', async () => {
    const w = mountWith()

    const codeBtn = w.findAll('button').find(b => b.text().includes('+ code'))!
    await codeBtn.trigger('click')

    const next = (w.emitted('update:modelValue')![0][0]) as PostDocument
    expect(next.blocks[0].type).toBe('code')
    expect((next.blocks[0] as any).data).toEqual({ code: '', language: '' })
  })

  it('the up button on the first block is disabled and the down button on the last block is disabled', async () => {
    const w = mountWith({
      blocks: [
        { id: 'a', type: 'header', data: { text: 'a', level: 1 } },
        { id: 'b', type: 'header', data: { text: 'b', level: 1 } }
      ]
    })
    await flushPromises()

    // Find the up/down buttons by title attribute (most stable selector).
    const ups = w.findAll('button[title="move up"]')
    const downs = w.findAll('button[title="move down"]')
    expect(ups[0].attributes('disabled')).toBeDefined()
    expect(ups[1].attributes('disabled')).toBeUndefined()
    expect(downs[0].attributes('disabled')).toBeUndefined()
    expect(downs[1].attributes('disabled')).toBeDefined()
  })

  it('moving the second block up swaps the order', async () => {
    const w = mountWith({
      blocks: [
        { id: 'a', type: 'header', data: { text: 'a', level: 1 } },
        { id: 'b', type: 'header', data: { text: 'b', level: 1 } }
      ]
    })
    await flushPromises()

    const ups = w.findAll('button[title="move up"]')
    await ups[1].trigger('click')

    const next = (w.emitted('update:modelValue')![0][0]) as PostDocument
    expect(next.blocks.map(b => b.id)).toEqual(['b', 'a'])
  })

  it('clicking ✕ on a block emits an update without that block', async () => {
    const w = mountWith({
      blocks: [
        { id: 'a', type: 'header', data: { text: 'a', level: 1 } },
        { id: 'b', type: 'header', data: { text: 'b', level: 1 } }
      ]
    })
    await flushPromises()

    const removes = w.findAll('button[title="delete block"]')
    await removes[0].trigger('click')

    const next = (w.emitted('update:modelValue')![0][0]) as PostDocument
    expect(next.blocks.map(b => b.id)).toEqual(['b'])
  })

  it('drag-and-drop simulation: dropping block-a onto block-b reorders them', async () => {
    const w = mountWith({
      blocks: [
        { id: 'a', type: 'header', data: { text: 'a', level: 1 } },
        { id: 'b', type: 'header', data: { text: 'b', level: 1 } }
      ]
    })
    await flushPromises()

    // The drag handle carries the dragstart listener; the block container
    // itself receives dragover/drop. happy-dom doesn't ship a real
    // DataTransfer, so we hand the handlers a minimal stub.
    const dataTransfer = {
      effectAllowed: '', dropEffect: '',
      data: '' as string,
      setData(_t: string, v: string) { this.data = v },
      getData(_t: string) { return this.data }
    }

    const handles = w.findAll('span[draggable="true"]')
    await handles[0].trigger('dragstart', { dataTransfer })

    // Find the block container for block-b (the second .group .border).
    const blocks = w.findAll('.group')
    await blocks[1].trigger('dragover', { dataTransfer })
    await blocks[1].trigger('drop', { dataTransfer })

    const events = w.emitted('update:modelValue')!
    const last = events[events.length - 1][0] as PostDocument
    expect(last.blocks.map(b => b.id)).toEqual(['b', 'a'])
  })
})
