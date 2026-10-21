import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { CheckCircle2, X } from 'lucide-react'
import { cn } from '@/lib/utils'

/**
 * In-page green/red message of the HR task pages — ported from LMS `components/ui/Toast`
 * (docs/SPEC-hr-tasks.md). LC's global `lib/toast` has no error tone (errors live inside forms,
 * `.claude/rules/error-visibility.md`); the HR pages report row-action failures here instead,
 * so a failed "Bekor qilish"/"Qaytarish" from a list row is never silent.
 *
 * Phone — top centre; desktop — top right (the save buttons at the bottom stay uncovered).
 */
export interface ToastProps {
  /** Text to show. `null` — closed. */
  message: string | null
  onClose: () => void
  /** Auto-close after this many ms (0 — stays). */
  duration?: number
  tone?: 'success' | 'error'
}

export function Toast({ message, onClose, duration = 3000, tone = 'success' }: ToastProps) {
  useEffect(() => {
    if (!message || duration <= 0) return
    const timer = setTimeout(onClose, duration)
    return () => clearTimeout(timer)
  }, [message, duration, onClose])

  if (!message) return null

  return createPortal(
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      aria-live="polite"
      className={cn(
        'fixed left-1/2 top-4 z-[80] w-[calc(100%-2rem)] max-w-sm -translate-x-1/2',
        'sm:left-auto sm:right-6 sm:top-6 sm:w-auto sm:translate-x-0',
        'flex items-start gap-3 rounded-xl border px-4 py-3 shadow-lg',
        tone === 'success'
          ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
          : 'border-red-200 bg-red-50 text-red-800',
      )}
    >
      <CheckCircle2 className={cn('mt-0.5 h-5 w-5 shrink-0', tone === 'success' ? 'text-emerald-500' : 'text-red-500')} />
      <span className="text-sm font-medium">{message}</span>
      <button
        type="button"
        onClick={onClose}
        aria-label="Yopish"
        className={cn('rounded-md p-0.5 transition-colors', tone === 'success' ? 'hover:bg-emerald-100' : 'hover:bg-red-100')}
      >
        <X className="h-4 w-4" />
      </button>
    </div>,
    document.body,
  )
}
