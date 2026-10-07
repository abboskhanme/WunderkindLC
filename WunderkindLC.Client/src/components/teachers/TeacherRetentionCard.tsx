import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getRetentionLoss, type RetentionLossReport } from '@/api/services/retentionLoss'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage } from '@/lib/utils'

/** Ixcham ko'rinish davri (oy) — hisobot sahifasidagi standart bilan bir xil. */
const MONTHS = 6

function monthsAgo(n: number): string {
  const d = new Date()
  const x = new Date(d.getFullYear(), d.getMonth() - n, 1)
  return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}`
}

/**
 * O'qituvchi sahifasi → «Samaradorlik» tabidagi IXCHAM «Ketish sabablari» (oxirgi 6 oy).
 * Manba — «Hisobotlar → Ketish sabablari» bilan BITTA endpoint (`GET /admin/retention-loss`,
 * `teacherId` filtri bilan), ya'ni raqamlar hisobot sahifasidagi qator bilan AYNAN bir xil.
 * Ruxsat (`teachers.retention`) bo'lmasa chaqiruvchi bu kartani umuman chizmaydi.
 */
export function TeacherRetentionCard({ teacherId }: { teacherId: string }) {
  const [data, setData] = useState<RetentionLossReport | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    let alive = true
    getRetentionLoss({ from: monthsAgo(MONTHS - 1), to: monthsAgo(0), teacherId, kind: 'all' })
      .then((d) => { if (alive) { setData(d); setError('') } })
      .catch((e) => { if (alive) setError(apiErrorMessage(e, "Ketish sabablarini yuklab bo'lmadi")) })
    return () => { alive = false }
  }, [teacherId])

  const me = data?.teachers.find((t) => t.teacherId === teacherId)
  const max = Math.max(1, ...(data?.reasons ?? []).map((r) => r.count))

  return (
    <Card
      title={`Ketish sabablari — oxirgi ${MONTHS} oy`}
      sub="Kurs ichida guruh almashtirish va sertifikat bilan tugatish ketish emas"
      actions={
        <Link
          to={`/admin/hisobotlar/ketish-sabablari?teacher=${encodeURIComponent(teacherId)}`}
          className="text-sm font-medium text-brand-600 no-underline hover:underline"
        >
          Batafsil →
        </Link>
      }
    >
      {error ? (
        <p className="text-sm text-red-600">{error}</p>
      ) : !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-3 gap-3 text-center">
            <div className="rounded-xl bg-slate-50 p-3">
              <div className="font-mono text-xl font-semibold text-slate-800">{me?.leftActive ?? 0}</div>
              <div className="text-xs text-slate-500">aktivdan ketgan</div>
            </div>
            <div className="rounded-xl bg-slate-50 p-3">
              <div className="font-mono text-xl font-semibold text-slate-800">{me?.leftTrial ?? 0}</div>
              <div className="text-xs text-slate-500">sinovdan ketgan</div>
            </div>
            <div className="rounded-xl bg-slate-50 p-3">
              <div className="font-mono text-xl font-semibold text-slate-800">
                {me?.lossRate == null ? '—' : `${Number(me.lossRate.toFixed(1))}%`}
              </div>
              <div className="text-xs text-slate-500">ketish foizi ({me?.activeBase ?? 0} aktivdan)</div>
            </div>
          </div>

          {data.reasons.length === 0 ? (
            <p className="py-4 text-center text-sm text-slate-400">Bu davrda ketish yo'q</p>
          ) : (
            <ul className="space-y-2">
              {data.reasons.slice(0, 6).map((r) => (
                <li key={r.label} className="flex items-center gap-3 text-sm">
                  <span className="w-40 shrink-0 truncate text-slate-600" title={r.label}>{r.label}</span>
                  <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                    {/* Yakka seriya — kurslar analitikasidagi "ketgan" rangi (`course-analytics.md` §6). */}
                    <div className="h-full rounded-full" style={{ width: `${(r.count / max) * 100}%`, backgroundColor: '#e11d48' }} />
                  </div>
                  <span className="w-8 text-right font-mono tabular-nums text-slate-800">{r.count}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Card>
  )
}
