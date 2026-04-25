import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setMockRpc } from '~/tests/nuxtAutoImports'
import type { ImageBlock } from '~/types/blocks'

const ImageBlockEditor = (await import('~/components/builder/ImageBlockEditor.vue')).default

const sampleBlock: ImageBlock = {
  id: 'b1',
  type: 'image',
  data: { src: '', alt: '' }
}

// happy-dom doesn't ship a real FileReader; we shim a minimal one so the
// component's onPick can finish without exploding. The shim resolves with
// a deterministic data: URL so the prefix-strip in toBase64 produces a
// known payload.
class FakeFileReader {
  result: string | null = null
  error: unknown = null
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  readAsDataURL(_blob: Blob) {
    queueMicrotask(() => {
      this.result = 'data:image/png;base64,RkFLRQ=='
      this.onload?.()
    })
  }
}

function fakeFile(size: number, type = 'image/png'): File {
  const f = new File([new Uint8Array(0)], 'pic.png', { type })
  // happy-dom's File doesn't honour the constructed size; redefine.
  Object.defineProperty(f, 'size', { value: size })
  return f
}

describe('<ImageBlockEditor>', () => {
  beforeEach(() => {
    ;(globalThis as any).FileReader = FakeFileReader
  })

  afterEach(() => {
    setMockRpc(null)
    document.body.innerHTML = ''
  })

  it('only offers MIME types ImageSharp can decode (excludes SVG)', () => {
    const w = mount(ImageBlockEditor, { props: { block: sampleBlock } })
    const accept = w.find('input[type="file"]').attributes('accept')!

    expect(accept).toContain('image/png')
    expect(accept).toContain('image/jpeg')
    expect(accept).toContain('image/webp')
    expect(accept).not.toContain('svg')
    expect(accept).not.toContain('image/*')
  })

  it('rejects oversized picks before the upload round trip and shows the cap', async () => {
    // Mock RPC throws so we know the test fails loudly if the preflight
    // is bypassed and the request actually goes out.
    setMockRpc(() => { throw new Error('rpc should not be called for oversized pick') })

    const w = mount(ImageBlockEditor, { props: { block: sampleBlock } })
    const input = w.find('input[type="file"]')
    const huge = fakeFile(7 * 1024 * 1024) // 7 MiB > 6 MiB cap

    Object.defineProperty(input.element, 'files', { value: [huge], configurable: true })
    await input.trigger('change')
    await flushPromises()

    expect(w.text()).toContain('too large')
    expect(w.text()).toContain('6 MiB')
  })

  it('uploads a small file and emits an updated block with the returned URL', async () => {
    const calls: { method: string; params?: unknown }[] = []
    setMockRpc(async (method, params) => {
      calls.push({ method, params })
      return { url: '/media/abc.webp' }
    })

    const w = mount(ImageBlockEditor, { props: { block: sampleBlock } })
    const input = w.find('input[type="file"]')
    const ok = fakeFile(50_000)

    Object.defineProperty(input.element, 'files', { value: [ok], configurable: true })
    await input.trigger('change')
    await flushPromises()

    expect(calls.map(c => c.method)).toEqual(['posts.uploadImage'])
    const emitted = w.emitted('update')
    expect(emitted).toBeTruthy()
    const updated = emitted![0][0] as ImageBlock
    expect(updated.data.src).toBe('/media/abc.webp')
  })

  it('clears the previous error when a fresh pick is made', async () => {
    setMockRpc(() => { throw new Error('rpc should not be called') })

    const w = mount(ImageBlockEditor, { props: { block: sampleBlock } })
    const input = w.find('input[type="file"]')

    // First pick: too large → error shown
    Object.defineProperty(input.element, 'files', { value: [fakeFile(7 * 1024 * 1024)], configurable: true })
    await input.trigger('change')
    await flushPromises()
    expect(w.text()).toContain('too large')

    // Switch the rpc mock to one that succeeds, pick a small file:
    setMockRpc(async () => ({ url: '/media/ok.webp' }))
    Object.defineProperty(input.element, 'files', { value: [fakeFile(1024)], configurable: true })
    await input.trigger('change')
    await flushPromises()

    // The "too large" message should be gone — error cleared on each pick.
    expect(w.text()).not.toContain('too large')
  })

  it('surfaces server-side rejection (e.g. dimensions cap) as a visible error', async () => {
    setMockRpc(() => {
      throw new Error('invalid: Image dimensions exceed the maximum 8000x8000')
    })

    const w = mount(ImageBlockEditor, { props: { block: sampleBlock } })
    const input = w.find('input[type="file"]')

    Object.defineProperty(input.element, 'files', { value: [fakeFile(1024)], configurable: true })
    await input.trigger('change')
    await flushPromises()

    expect(w.text()).toContain('dimensions exceed')
  })
})
