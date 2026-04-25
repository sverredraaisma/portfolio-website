import { useAuthStore } from '~/stores/auth'

export default defineNuxtRouteMiddleware((to) => {
  if (import.meta.server) return
  const auth = useAuthStore()
  if (!auth.isAuthenticated) {
    // Preserve where the user was trying to go so the login page can
    // bounce them back. fullPath includes query + hash. /login itself
    // is excluded so we don't loop redirect=/login → /login → ...
    const redirect = to.fullPath !== '/login' ? to.fullPath : undefined
    return navigateTo({ path: '/login', query: redirect ? { redirect } : {} })
  }
})
