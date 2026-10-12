import { useState } from 'react'
import { ChevronRight } from 'lucide-react'
import type { AttendanceFine, AttendanceFineList } from '@/api/services/attendanceFines'
import { apiErrorMessage, cn, formatDate, formatMoney } from '@/lib/utils'

/**
 * «Davomat jarimasi: −X so'm (N ta dars)» qatori (CRM Phase 3, U3c) — o'qituvchi profili (Maosh), o'qituvchi
 * portali (Maosh) va `TeacherSalaryDetailModal`. Bosilsa sanalar ro'yxati yuklanadi (`load` berilgan bo'lsa —
 * jarimalar ro'yxati moliya ruxsati yoki o'qituvchining o'zi bilan ochiladi).
 */
export function AttendanceFineLine({
  amount,
  count,
  load,
  className,
}: {
  amount: number
  count: number
  /** Sanalar ro'yxatini yuklash (faqat FAOL jarimalar ko'rsatiladi). Berilmasa qator ochilmaydi. */
  load?: () => Promise<AttendanceFineList>
  className?: string
}) {
  const [open, setOpen] = useState(false)
  const [rows, setRows] = useState<AttendanceFine[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  if (amount <= 0 && count <= 0) return null

  const toggle = async () => {
    if (!load) return
    const next = !open
    setOpen(next)
    if (!next || rows) return
    try {
      const r = await load()
      setRows(r.rows.filter((f) => f.status === 'active'))
      setError(null)
    } catch (err) {
      setError(apiErrorMessage(err, "Jarimalarni yuklab bo'lmadi"))
    }
  }

  return (
    <div className={className}>
      <button
        type="button"
        onClick={toggle}
        disabled={!load}
        className="flex w-full items-center justify-between rounded-lg bg-red-50 px-3 py-2.5 text-left text-xs text-red-800 disabled:cursor-default"
      >
        <span className="flex items-center gap-1.5">
          {load && <ChevronRight className={cn('h-3.5 w-3.5 transition-transform', open && 'rotate-90')} />}
          Davomat jarimasi: −{formatMoney(amount)} ({count} ta dars)
        </span>
        {load && <span className="text-red-600">sanalar</span>}
      </button>
      {open && (
        <div className="mt-2 space-y-1.5">
          {error && <p className="text-xs text-red-600">{error}</p>}
          {!rows && !error && <p className="text-xs text-slate-500">Yuklanmoqda...</p>}
          {rows?.map((f) => (
            <div key={f.id} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-xs">
              <span className="text-slate-700">
                <span className="font-mono">{formatDate(f.lessonDate)}</span> · {f.lessonStart} · {f.groupName}
              </span>
              <span className="font-mono font-semibold text-red-600">−{formatMoney(f.amount)}</span>
            </div>
          ))}
          {rows && rows.length === 0 && <p className="text-xs text-slate-500">Faol jarima yo'q.</p>}
        </div>
      )}
    </div>
  )
}
