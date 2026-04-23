import { useAuthStore } from '~/stores/auth'

export type RpcError = { code: string; message: string }

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

  const base = process.server && config.apiBaseInternal
    ? config.apiBaseInternal
    : config.public.apiBase

  async function call<T = unknown>(method: string, params?: unknown): Promise<T> {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' }
    if (auth.accessToken) headers['Authorization'] = `Bearer ${auth.accessToken}`

    const res = await fetch(`${base}/rpc`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ method, params })
    })

    const body = await res.json().catch(() => ({}))
    if (!res.ok || body.error) {
      const err = (body.error ?? { code: 'http', message: res.statusText }) as RpcError
      throw new Error(`${err.code}: ${err.message}`)
    }
    return body.result as T
  }

  return { call }
}
