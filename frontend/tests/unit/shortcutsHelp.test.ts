import { afterEach, describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'

const ShortcutsHelp = (await import('~/components/ShortcutsHelp.vue')).default

// We don't use `attachTo: document.body` here. The component renders into
// document.body via <Teleport>, which we can query directly — and skipping
// `attachTo` lets us drive the open/closed transitions through props without
// racing the wrapper's own DOM cleanup. (Earlier versions used attachTo and
// got an "insertBefore on null" warning during the Esc-then-unmount path.)
describe('<ShortcutsHelp>', () => {
  let wrapper: ReturnType<typeof mount> | null = null

  afterEach(async () => {
    if (wrapper) {
      // Bring the dialog back to the closed state via the prop so Vue patches
      // the Teleport down cleanly, *then* unmount.
      await wrapper.setProps({ modelValue: false })
      await flushPromises()
      wrapper.unmount()
      wrapper = null
    }
    document.body.innerHTML = ''
  })

  it('renders nothing when not open', () => {
    wrapper = mount(ShortcutsHelp, { props: { modelValue: false } })

    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
  })

  it('renders the help dialog with one row per binding when open', async () => {
    wrapper = mount(ShortcutsHelp, { props: { modelValue: true } })
    await flushPromises()

    const dialog = document.body.querySelector('[role="dialog"]')!
    expect(dialog).not.toBeNull()
    expect(dialog.querySelectorAll('li').length).toBeGreaterThan(0)
    expect(dialog.textContent).toContain('posts')
    expect(dialog.textContent).toContain('search')
  })

  it('emits update:modelValue=false when the close button is clicked', async () => {
    wrapper = mount(ShortcutsHelp, { props: { modelValue: true } })
    await flushPromises()

    const close = document.body.querySelector('[role="dialog"] button')!
    ;(close as HTMLButtonElement).click()
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBe(false)
  })

  it('Esc closes the dialog while it is open', async () => {
    wrapper = mount(ShortcutsHelp, { props: { modelValue: true } })
    await flushPromises()

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }))
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBe(false)
  })
})
