import { defineStore } from 'pinia'

type User = { id: string; username: string; email: string; emailVerified?: boolean }

export const useAuthStore = defineStore('auth', {
  state: () => ({
    accessToken: '' as string,
    refreshToken: '' as string,
    user: null as User | null
  }),
  getters: {
    isAuthenticated: (s) => !!s.accessToken && !!s.user
  },
  actions: {
    setSession(accessToken: string, refreshToken: string, user: User) {
      this.accessToken = accessToken
      this.refreshToken = refreshToken
      this.user = user
      if (import.meta.client) {
        localStorage.setItem('auth', JSON.stringify({ accessToken, refreshToken, user }))
      }
    },
    setTokens(access: string, refresh: string) {
      this.accessToken = access
      this.refreshToken = refresh
      if (import.meta.client) {
        localStorage.setItem('auth', JSON.stringify({
          accessToken: access,
          refreshToken: refresh,
          user: this.user
        }))
      }
    },
    hydrate() {
      if (!import.meta.client) return
      const raw = localStorage.getItem('auth')
      if (!raw) return
      try {
        const { accessToken, refreshToken, user } = JSON.parse(raw)
        this.accessToken = accessToken
        this.refreshToken = refreshToken
        this.user = user
      } catch {}
    },
    logout() {
      this.accessToken = ''
      this.refreshToken = ''
      this.user = null
      if (import.meta.client) localStorage.removeItem('auth')
    }
  }
})
