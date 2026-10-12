import { useEffect, useState } from 'react'
import { CalendarX } from 'lucide-react'
import { getStudentAbsences, type AbsenceKind, type StudentAbsences } from '@/api/services/absences'
import { absencePeriodRange, type AbsencePeriod } from '@/lib/attendance'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'

const PERIODS: { key: AbsencePeriod; label: string }[] = [
  { key: 'month', label: 'Shu oy' },
  { key: '3m', label: '3 oy' },
  { key: '6m', label: '6 oy' },
  { key: '1y', label: '1 yil' },
]

const KIND: Record<AbsenceKind, { label: string; cls: string }> = {
  late: { label: 'Kech', cls: 'bg-amber-50 text-amber-800' },
  absent_excused: { label: 'Sababli', cls: 'bg-sky-50 text-sky-700' },
  absent_unexcused: { label: 'Sababsiz', cls: 'bg-red-50 text-red-700' },
}

/**
 * O'quvchi profili → «Baholar va davomat» → «Qoldirilgan darslar» (CRM Phase 3, U3d — SPEC P3.5): tanlangan
 * davrdagi HAR qoldirilgan/kech qolingan dars (sana, guruh, sabab) va sanoqlar. Sanoqlar ro'yxat bilan bir xil
 * manbadan (server `StudentAbsences`), foiz — yagona ta'rif (kech = keldi, belgilanmagan dars kirmaydi).
 */
export function MissedLessonsSection({ studentId }: { studentId: string }) {
  const [period, setPeriod] = useState<AbsencePeriod>('3m')
  // Natija O'Z kaliti bilan saqlanadi: kalit mos kelmasa — yuklanmoqda (effektda sinxron setState yo'q).
  const [result, setResult] = useState<{ key: string; data?: StudentAbsences; error?: string } | null>(null)
  const key = `${studentId}|${period}`

  useEffect(() => {
    let alive = true
    const { from, to } = absencePeriodRange(period, new Date())
    getStudentAbsences(studentId, from, to)
      .then((data) => alive && setResult({ key, data }))
      .catch((err) => alive && setResult({ key, error: apiErrorMessage(err, "Qoldirilgan darslarni yuklab bo'lmadi") }))
    return () => {
      alive = false
    }
  }, [studentId, period, key])

  const current = result?.key === key ? result : null
  const data = current?.data
  const c = data?.counts

  const cells: { label: string; value: string; cls?: string }[] = c
    ? [
        { label: 'Darslar', value: String(c.held) },
        { label: 'Keldi', value: String(c.present), cls: 'text-emerald-700' },
        { label: 'Kech', value: String(c.late), cls: 'text-amber-700' },
        { label: 'Sababli', value: String(c.absentExcused), cls: 'text-sky-700' },
        { label: 'Sababsiz', value: String(c.absentUnexcused), cls: 'text-red-700' },
        { label: 'Davomat', value: c.percent == null ? '—' : `${c.percent}%` },
      ]
    : []

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <h3 className="flex items-center gap-2 text-sm font-semibold text-slate-800">
          <CalendarX className="h-4 w-4 text-red-500" /> Qoldirilgan darslar
        </h3>
        <div className="flex rounded-lg border border-slate-200 bg-white p-1">
          {PERIODS.map((p) => (
            <button
              key={p.key}
              type="button"
              onClick={() => setPeriod(p.key)}
              className={cn(
                'h-7 rounded-md px-2.5 text-xs font-medium transition-colors',
                period === p.key ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
              )}
            >
              {p.label}
            </button>
          ))}
        </div>
      </div>

      {current?.error ? (
        <p className="text-sm text-red-600">{current.error}</p>
      ) : !data ? (
        <p className="text-sm text-slate-500">Yuklanmoqda...</p>
      ) : (
        <>
          <div className="mb-3 grid grid-cols-3 gap-2 sm:grid-cols-6">
            {cells.map((x) => (
              <div key={x.label} className="rounded-lg bg-slate-50 px-2 py-2 text-center">
                <div className={cn('text-lg font-bold text-slate-800', x.cls)}>{x.value}</div>
                <div className="text-[11px] text-slate-500">{x.label}</div>
              </div>
            ))}
          </div>
          {data.items.length === 0 ? (
            <p className="text-sm text-slate-500">Bu davrda qoldirilgan dars yo'q.</p>
          ) : (
            <ul className="max-h-72 divide-y divide-slate-100 overflow-y-auto">
              {data.items.map((i) => (
                <li key={`${i.date}|${i.groupId}`} className="flex flex-wrap items-center gap-2 py-1.5 text-sm">
                  <span className="w-24 font-mono text-xs text-slate-600">{formatDate(i.date)}</span>
                  <span className="min-w-0 flex-1 truncate text-slate-700">
                    {i.groupName}
                    {i.courseName ? <span className="text-slate-400"> · {i.courseName}</span> : null}
                  </span>
                  <span className={cn('rounded-md px-1.5 py-0.5 text-xs font-medium', KIND[i.kind].cls)}>
                    {i.reasonName || KIND[i.kind].label}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  )
}
