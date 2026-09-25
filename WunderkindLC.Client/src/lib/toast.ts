/**
 * Global BILDIRISHNOMA (toast) — "amal bajarildi" xabari. Kichik pub/sub: komponent tashqarisidan
 * ham chaqirsa bo'ladi (`toast.success(...)`), chizish esa `components/ui/Toaster.tsx` da.
 *
 * ⚠️ Komponent va funksiya bir faylda ARALASHMAYDI (eslint `react-refresh/only-export-components`).
 * Xatolar uchun ishlatilmaydi — ular forma ICHIDA ko'rsatiladi (`.claude/rules/error-visibility.md`).
 */
export type ToastKind = 'success' | 'info'

export interface ToastItem {
  id: number
  kind: ToastKind
  title: string
  description?: string
}

type Listener = (items: ToastItem[]) => void

/** Bildirishnoma ekranda turadigan vaqt (ms). */
export const TOAST_DURATION_MS = 4500

let items: ToastItem[] = []
let nextId = 1
const listeners = new Set<Listener>()

const emit = () => listeners.forEach((l) => l(items))

export function dismissToast(id: number) {
  items = items.filter((t) => t.id !== id)
  emit()
}

function push(kind: ToastKind, title: string, description?: string) {
  const id = nextId++
  // Ko'pi bilan 4 ta — eskisi tushib ketadi (ekranni to'ldirib yubormasin).
  items = [...items, { id, kind, title, description }].slice(-4)
  emit()
  setTimeout(() => dismissToast(id), TOAST_DURATION_MS)
}

export const toast = {
  success: (title: string, description?: string) => push('success', title, description),
  info: (title: string, description?: string) => push('info', title, description),
}

export function subscribeToasts(l: Listener): () => void {
  listeners.add(l)
  l(items)
  return () => {
    listeners.delete(l)
  }
}

