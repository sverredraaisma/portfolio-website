// Toast queue. State lives in a Nuxt useState ref so any component can push
// from anywhere; a single <Toaster> mounted in the default layout renders the
// stack. Auto-dismiss after a per-toast duration; click-to-dismiss too.

export type ToastKind = 'success' | 'error' | 'info'

export type Toast = {
  id: number
  kind: ToastKind
  message: string
  // When we dropped past this timestamp, the toast self-removes. Stored as
  // an absolute time rather than a setTimeout id so SSR hydration is a no-op.
  expiresAt: number
}

let nextId = 1
const DEFAULT_TTL: Record<ToastKind, number> = {
  success: 3000,
  info: 3500,
  error: 6000
}

export function useToast() {
  const toasts = useState<Toast[]>('toasts', () => [])

  function push(kind: ToastKind, message: string, ttl?: number) {
    const t: Toast = {
      id: nextId++,
      kind,
      message,
      expiresAt: Date.now() + (ttl ?? DEFAULT_TTL[kind])
    }
    toasts.value = [...toasts.value, t]
    if (typeof window !== 'undefined') {
      // Schedule the cleanup. Using a real timer keeps the auto-dismiss
      // independent of any rendering loop.
      window.setTimeout(() => dismiss(t.id), t.expiresAt - Date.now())
    }
    return t.id
  }

  function dismiss(id: number) {
    toasts.value = toasts.value.filter(t => t.id !== id)
  }

  return {
    toasts,
    success: (msg: string, ttl?: number) => push('success', msg, ttl),
    error:   (msg: string, ttl?: number) => push('error', msg, ttl),
    info:    (msg: string, ttl?: number) => push('info', msg, ttl),
    dismiss
  }
}
