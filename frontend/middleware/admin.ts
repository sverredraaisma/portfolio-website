import { useAuthStore } from '~/stores/auth'

export default defineNuxtRouteMiddleware((to) => {
  if (import.meta.server) return
  const auth = useAuthStore()
  if (!auth.isAuthenticated) {
    const redirect = to.fullPath !== '/login' ? to.fullPath : undefined
    return navigateTo({ path: '/login', query: redirect ? { redirect } : {} })
  }
  if (!auth.user?.isAdmin) return navigateTo('/')
})
