import { useAuthStore } from '~/stores/auth'

export type RpcError = { code: string; message: string }

type RpcEnvelope<T> = { result?: T; error?: RpcError }
type RefreshResult = { accessToken: string; refreshToken: string; user: unknown }

const NO_REFRESH_METHODS = new Set(['auth.refresh', 'auth.login', 'auth.register'])

// Module-level coalescing: if a refresh is already in flight, every concurrent
// 401 awaits the same promise instead of stampeding the refresh endpoint.
let inFlightRefresh: Promise<void> | null = null

/**
 * Single RPC client. All backend calls go through here.
 * Usage: const { data } = await useRpc().call('posts.list')
 *
 * On the server (SSR) we prefer apiBaseInternal so the request stays on
 * the docker network. In the browser we use the public apiBase.
 */
export function useRpc() {
  const config = useRuntimeConfig()
  const auth = useAuthStore()

  const base = import.meta.server && config.apiBaseInternal
    ? config.apiBaseInternal
    : config.public.apiBase

  async function refreshOnce(): Promise<void> {
    const refreshToken = auth.refreshToken
    if (!refreshToken) throw new Error('unauthorized: no refresh token')

    const res = await fetch(`${base}/rpc`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ method: 'auth.refresh', params: { refreshToken } })
    })
    const body = (await res.json().catch(() => ({}))) as RpcEnvelope<RefreshResult>
    if (!res.ok || body.error || !body.result) {
      throw new Error('unauthorized: refresh failed')
    }
    auth.setTokens(body.result.accessToken, body.result.refreshToken)
  }

  function ensureRefresh(): Promise<void> {
    if (!inFlightRefresh) {
      inFlightRefresh = refreshOnce().finally(() => {
        inFlightRefresh = null
      })
    }
    return inFlightRefresh
  }

  async function doFetch<T>(method: string, params?: unknown): Promise<{ status: number; body: RpcEnvelope<T> }> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' }
    if (auth.accessToken) headers['Authorization'] = `Bearer ${auth.accessToken}`

    const res = await fetch(`${base}/rpc`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ method, params })
    })
    const body = (await res.json().catch(() => ({}))) as RpcEnvelope<T>
    return { status: res.status, body }
  }

  async function call<T = unknown>(method: string, params?: unknown): Promise<T> {
    const { status, body } = await doFetch<T>(method, params)

    const isUnauthorized = status === 401 || body.error?.code === 'unauthorized'
    const canRefresh = isUnauthorized
      && !NO_REFRESH_METHODS.has(method)
      && !!auth.refreshToken

    if (canRefresh) {
      try {
        await ensureRefresh()
      } catch {
        auth.logout()
        const original = body.error ?? { code: 'unauthorized', message: 'Unauthorized' }
        throw new Error(`${original.code}: ${original.message}`)
      }
      const retry = await doFetch<T>(method, params)
      if (retry.status >= 200 && retry.status < 300 && !retry.body.error && retry.body.result !== undefined) {
        return retry.body.result
      }
      const err = retry.body.error ?? { code: 'http', message: String(retry.status) }
      throw new Error(`${err.code}: ${err.message}`)
    }

    if (status < 200 || status >= 300 || body.error) {
      const err = body.error ?? { code: 'http', message: String(status) }
      throw new Error(`${err.code}: ${err.message}`)
    }
    return body.result as T
  }

  return { call }
}
