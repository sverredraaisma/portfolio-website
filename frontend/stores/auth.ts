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
      if (process.client) {
        localStorage.setItem('auth', JSON.stringify({ accessToken, refreshToken, user }))
      }
    },
    hydrate() {
      if (!process.client) return
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
      if (process.client) localStorage.removeItem('auth')
    }
  }
})
