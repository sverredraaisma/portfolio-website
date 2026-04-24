import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { setMockRpc } from '~/tests/nuxtAutoImports'

// vi.mock can't catch Nuxt's auto-imports — `useRpc` is referenced as a
// global, not via an import statement, in the compiled .vue. The shim in
// tests/nuxtAutoImports.ts installs a configurable global useRpc; setMockRpc
// reroutes the .call(...) handler per test.

const TerminalComments = (await import('~/components/TerminalComments.vue')).default
const { useAuthStore } = await import('~/stores/auth')

describe('<TerminalComments>', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  afterEach(() => {
    setMockRpc(null)
    document.body.innerHTML = ''
  })

  function authAs(username: string, isAdmin = false) {
    const auth = useAuthStore()
    auth.setSession('access', 'refresh', { id: 'u-1', username, email: 'u@x', isAdmin })
  }

  function rpc(handler: (method: string, params?: any) => any) {
    setMockRpc(async (method, params) => handler(method, params))
  }

  it('renders the friendly empty state when there are no comments', async () => {
    rpc(() => ({ items: [], page: 1, pageSize: 50, hasMore: false }))
    const w = mount(TerminalComments, { props: { postId: 'p1' } })

    await flushPromises()

    expect(w.text()).toContain('no comments. be the first.')
  })

  it('renders one row per loaded comment with the author label', async () => {
    rpc(() => ({
      items: [
        { id: 'c1', body: 'hello', createdAt: '2026-04-25T10:00:00Z', author: 'alice', authorIsAdmin: false },
        { id: 'c2', body: 'world', createdAt: '2026-04-25T10:01:00Z', author: 'bob',   authorIsAdmin: false }
      ],
      page: 1, pageSize: 50, hasMore: false
    }))
    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    expect(w.text()).toContain('hello')
    expect(w.text()).toContain('world')
    expect(w.text()).toContain('alice@portfolio')
    expect(w.text()).toContain('bob@portfolio')
  })

  it('marks admin authors as root(<name>) for the visual privilege cue', async () => {
    rpc(() => ({
      items: [{ id: 'c1', body: 'hi', createdAt: '2026-04-25T10:00:00Z', author: 'admin', authorIsAdmin: true }],
      page: 1, pageSize: 50, hasMore: false
    }))
    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    expect(w.text()).toContain('root(admin)@portfolio')
  })

  it('blocks send when not signed in and shows a hint', async () => {
    const calls: string[] = []
    rpc((m) => {
      calls.push(m)
      return { items: [], page: 1, pageSize: 50, hasMore: false }
    })
    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    await w.find('input').setValue('hello')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(w.text()).toContain('log in to comment')
    // No create RPC was called — only the initial list.
    expect(calls).toEqual(['comments.list'])
  })

  it('posts a new comment via comments.create and appends it to the list', async () => {
    authAs('alice')
    const calls: { m: string; p?: any }[] = []
    rpc((m, p) => {
      calls.push({ m, p })
      if (m === 'comments.list') return { items: [], page: 1, pageSize: 50, hasMore: false }
      if (m === 'comments.create') return {
        id: 'c1', body: p.body, createdAt: '2026-04-25T10:00:00Z',
        author: 'alice', authorIsAdmin: false
      }
      throw new Error('unexpected ' + m)
    })

    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    await w.find('input').setValue('first comment')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(calls.map(c => c.m)).toEqual(['comments.list', 'comments.create'])
    expect(calls[1].p).toEqual({ postId: 'p1', body: 'first comment' })
    expect(w.text()).toContain('first comment')
  })

  it('shows the [sudo] root prompt for admins', async () => {
    authAs('admin', true)
    rpc(() => ({ items: [], page: 1, pageSize: 50, hasMore: false }))

    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    expect(w.text()).toContain('[sudo]')
    expect(w.text()).toContain('admin@portfolio:~#')
  })

  it('shows the regular $ prompt for non-admin users', async () => {
    authAs('alice', false)
    rpc(() => ({ items: [], page: 1, pageSize: 50, hasMore: false }))

    const w = mount(TerminalComments, { props: { postId: 'p1' } })
    await flushPromises()

    expect(w.text()).not.toContain('[sudo]')
    expect(w.text()).toContain('alice@portfolio:~$')
  })
})
