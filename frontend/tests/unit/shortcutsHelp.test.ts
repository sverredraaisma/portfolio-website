import { afterEach, describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const ShortcutsHelp = (await import('~/components/ShortcutsHelp.vue')).default

describe('<ShortcutsHelp>', () => {
  afterEach(() => { document.body.innerHTML = '' })

  it('renders nothing when not open', () => {
    mount(ShortcutsHelp, { props: { modelValue: false }, attachTo: document.body })

    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
  })

  it('renders the help dialog with one row per binding when open', async () => {
    mount(ShortcutsHelp, { props: { modelValue: true }, attachTo: document.body })
    await flushPromises()

    const dialog = document.body.querySelector('[role="dialog"]')!
    expect(dialog).not.toBeNull()
    expect(dialog.querySelectorAll('li').length).toBeGreaterThan(0)
    // The help dialog at minimum mentions a couple of the bindings.
    expect(dialog.textContent).toContain('posts')
    expect(dialog.textContent).toContain('search')
  })

  it('emits update:modelValue=false when the close button is clicked', async () => {
    const w = mount(ShortcutsHelp, {
      props: { modelValue: true },
      attachTo: document.body
    })
    await flushPromises()

    const close = document.body.querySelector('[role="dialog"] button')!
    ;(close as HTMLButtonElement).click()
    await flushPromises()

    expect(w.emitted('update:modelValue')?.at(-1)?.[0]).toBe(false)
    w.unmount()
  })

  it('Esc closes the dialog while it is open', async () => {
    const w = mount(ShortcutsHelp, {
      props: { modelValue: true },
      attachTo: document.body
    })
    await flushPromises()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()

    expect(w.emitted('update:modelValue')?.at(-1)?.[0]).toBe(false)
    w.unmount()
  })
})
