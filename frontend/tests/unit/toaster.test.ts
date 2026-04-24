import { beforeEach, describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { resetUseState } from '~/tests/useStateShim'

const { useToast } = await import('~/composables/useToast')
const Toaster = (await import('~/components/Toaster.vue')).default

// The Toaster teleports to document.body so we look there to assert on the
// rendered DOM. attachTo is required to make Teleport reach a real element.

describe('<Toaster>', () => {
  beforeEach(() => {
    resetUseState('toasts')
    document.body.innerHTML = ''
  })

  it('renders nothing when the queue is empty', async () => {
    mount(Toaster, { attachTo: document.body })
    await nextTick()

    expect(document.body.querySelector('button')).toBeNull()
  })

  it('renders one button per queued toast', async () => {
    const t = useToast()
    mount(Toaster, { attachTo: document.body })
    t.success('one')
    t.error('two')
    await nextTick()

    const buttons = Array.from(document.body.querySelectorAll('button'))
    expect(buttons.map(b => b.textContent?.trim())).toEqual(['one', 'two'])
  })

  it('clicking a toast removes it from the queue', async () => {
    const t = useToast()
    mount(Toaster, { attachTo: document.body })
    t.info('hi')
    await nextTick()

    document.body.querySelector('button')!.click()
    await nextTick()

    expect(t.toasts.value).toHaveLength(0)
  })
})
