import { useEffect, useState } from 'react'
import { CheckCircle2, Info, X } from 'lucide-react'
import { dismissToast, subscribeToasts, type ToastItem } from '@/lib/toast'
import { cn } from '@/lib/utils'

/** Global bildirishnomalar — ilovaning ildizida BIR MARTA chiziladi (`App.tsx`). */
export function Toaster() {
  const [items, setItems] = useState<ToastItem[]>([])
  useEffect(() => subscribeToasts(setItems), [])

  if (items.length === 0) return null
  return (
    <div
      aria-live="polite"
      className="pointer-events-none fixed inset-x-4 bottom-4 z-[1000] flex flex-col items-center gap-2 sm:inset-x-auto sm:right-4 sm:items-end"
    >
      {items.map((t) => (
        <div
          key={t.id}
          role="status"
          className={cn(
            'pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-xl border bg-white px-4 py-3 shadow-lg',
            t.kind === 'success' ? 'border-emerald-200' : 'border-slate-200',
          )}
        >
          {t.kind === 'success' ? (
            <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-emerald-600" />
          ) : (
            <Info className="mt-0.5 h-5 w-5 shrink-0 text-sky-600" />
          )}
          <div className="min-w-0 flex-1">
            <p className="text-sm font-semibold text-slate-800">{t.title}</p>
            {t.description && <p className="mt-0.5 text-[13px] text-slate-500">{t.description}</p>}
          </div>
          <button
            type="button"
            onClick={() => dismissToast(t.id)}
            className="shrink-0 rounded p-0.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
            aria-label="Yopish"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      ))}
    </div>
  )
}
