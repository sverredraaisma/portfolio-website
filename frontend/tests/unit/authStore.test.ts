import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '~/stores/auth'

// The store gates its localStorage writes behind `import.meta.client`, which
// is only true under Nuxt's build. Vitest doesn't replace it, so the
// persistence side-effects don't run here — these tests cover the in-memory
// behaviour, which is the part most likely to break.

describe('useAuthStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('starts unauthenticated', () => {
    const auth = useAuthStore()
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.user).toBeNull()
  })

  it('setSession populates tokens + user and isAuthenticated flips true', () => {
    const auth = useAuthStore()

    auth.setSession('a', 'r', { id: '1', username: 'alice', email: 'a@x' })

    expect(auth.accessToken).toBe('a')
    expect(auth.refreshToken).toBe('r')
    expect(auth.user?.username).toBe('alice')
    expect(auth.isAuthenticated).toBe(true)
  })

  it('isAuthenticated is false when only the token is set without a user', () => {
    const auth = useAuthStore()
    auth.accessToken = 'orphan-token'
    expect(auth.isAuthenticated).toBe(false)
  })

  it('isAuthenticated is false when only the user is set without a token', () => {
    const auth = useAuthStore()
    auth.user = { id: '1', username: 'alice', email: 'a@x' }
    expect(auth.isAuthenticated).toBe(false)
  })

  it('setTokens preserves the existing user across a refresh-token rotation', () => {
    const auth = useAuthStore()
    auth.setSession('a', 'r', { id: '1', username: 'alice', email: 'a@x' })

    auth.setTokens('a2', 'r2')

    expect(auth.accessToken).toBe('a2')
    expect(auth.refreshToken).toBe('r2')
    // rotation must not log the user out
    expect(auth.user?.username).toBe('alice')
  })

  it('logout() clears every session field', () => {
    const auth = useAuthStore()
    auth.setSession('a', 'r', { id: '1', username: 'alice', email: 'a@x' })

    auth.logout()

    expect(auth.accessToken).toBe('')
    expect(auth.refreshToken).toBe('')
    expect(auth.user).toBeNull()
    expect(auth.isAuthenticated).toBe(false)
  })

  it('hydrate() is a no-op when nothing is stored — state stays at default', () => {
    const auth = useAuthStore()
    auth.hydrate()
    expect(auth.isAuthenticated).toBe(false)
  })
})
