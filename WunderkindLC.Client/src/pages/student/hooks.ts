import { useCallback, useEffect, useState, useSyncExternalStore } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiErrorMessage } from '@/lib/utils'

/* ============================================================
   Student cabinet — small shared hooks.
   ============================================================ */

/**
 * One GET request with loading / error / reload. `fn` must be STABLE (a module-level function or
 * useCallback) — it is an effect dependency. State is set only from the promise callbacks.
 * On a failed reload the previous data stays visible next to the error.
 */
export function useApi<T>(fn: () => Promise<T>) {
  const [tick, setTick] = useState(0)
  const [res, setRes] = useState<{ tick: number; data: T | null; error: string | null } | null>(null)
  useEffect(() => {
    let on = true
    fn().then(
      (data) => {
        if (on) setRes({ tick, data, error: null })
      },
      (e) => {
        if (on) setRes((prev) => ({ tick, data: prev?.data ?? null, error: apiErrorMessage(e, "Ma'lumotni yuklab bo'lmadi") }))
      },
    )
    return () => {
      on = false
    }
  }, [fn, tick])
  const reload = useCallback(() => setTick((t) => t + 1), [])
  return {
    data: res?.data ?? null,
    error: res && res.tick === tick ? res.error : null,
    loading: !res || res.tick !== tick,
    reload,
  }
}

/**
 * "Back" for sub-pages: real history when the user came from inside the app, otherwise the
 * parent screen (the page may be opened straight from a Telegram bot link — `navigate(-1)` would
 * then leave the cabinet).
 */
export function useBack(fallback: string) {
  const navigate = useNavigate()
  return useCallback(() => {
    const idx = (window.history.state as { idx?: number } | null)?.idx
    if (typeof idx === 'number' && idx > 0) navigate(-1)
    else navigate(fallback, { replace: true })
  }, [navigate, fallback])
}

/* ---- a minute-resolution clock (no impure Date calls during render) ---- */
let clock = new Date()
const listeners = new Set<() => void>()
let timer: number | undefined

function subscribeClock(cb: () => void) {
  listeners.add(cb)
  clock = new Date()
  if (timer === undefined) {
    timer = window.setInterval(() => {
      clock = new Date()
      listeners.forEach((l) => l())
    }, 30_000)
  }
  return () => {
    listeners.delete(cb)
    if (!listeners.size && timer !== undefined) {
      window.clearInterval(timer)
      timer = undefined
    }
  }
}

export function useNow(): Date {
  return useSyncExternalStore(subscribeClock, () => clock, () => clock)
}

/** Short-lived toast text (auto-hides). */
export function useToast(ms = 2600) {
  const [text, setText] = useState<string | null>(null)
  useEffect(() => {
    if (!text) return
    const t = window.setTimeout(() => setText(null), ms)
    return () => window.clearTimeout(t)
  }, [text, ms])
  return [text, setText] as const
}
