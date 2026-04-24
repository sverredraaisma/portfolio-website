import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import Spinner from '~/components/Spinner.vue'

describe('<Spinner>', () => {
  it('renders the CSS-only spinner with an aria-live status role', () => {
    const w = mount(Spinner)

    expect(w.attributes('role')).toBe('status')
    expect(w.find('.ui-spinner').exists()).toBe(true)
  })

  it('exposes a screen-reader label that defaults to "loading"', () => {
    const w = mount(Spinner)

    // .sr-only span carries the visually hidden label.
    expect(w.find('.sr-only').text()).toBe('loading')
  })

  it('honours a custom label prop', () => {
    const w = mount(Spinner, { props: { label: 'saving' } })

    expect(w.find('.sr-only').text()).toBe('saving')
  })

  it('applies the size-mapped text class', () => {
    expect(mount(Spinner, { props: { size: 'xs' } }).classes()).toContain('text-xs')
    expect(mount(Spinner, { props: { size: 'md' } }).classes()).toContain('text-base')
  })
})
