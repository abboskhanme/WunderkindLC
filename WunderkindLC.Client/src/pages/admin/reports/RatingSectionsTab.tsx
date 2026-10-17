import { useEffect, useState } from 'react'
import { getRatingSections, type RatingSectionRow, type RatingSections } from '@/api/services/rating'
import { RatingPeriodSwitch } from '@/components/rating/RatingPeriodSwitch'
import { defaultRatingPeriod, fmtScore, type RatingPeriodValue } from '@/lib/ratingPeriods'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * Reyting → «Guruhlar kesimi» (SPEC-backlog F4 A2, TZ 2.2): har guruh va har maktab sinfi bo'yicha o'rtacha reyting
 * bali, test %, ball %, uy vazifasi va davomat. Raqamlar markaz/guruh jadvallari bilan BIR davr reytingidan, davomat —
 * yagona ta'rifdan (server `RatingSections`).
 */
export function RatingSectionsTab() {
  const [period, setPeriod] = useState<RatingPeriodValue>(() => defaultRatingPeriod(new Date()))
  const [res, setRes] = useState<{ key: string; data: RatingSections | null; error: string | null } | null>(null)
  const key = `${period.kind}:${period.key}`

  useEffect(() => {
    let alive = true
    const k = `${period.kind}:${period.key}`
    getRatingSections({ period: period.kind, key: period.key })
      .then((data) => alive && setRes({ key: k, data, error: null }))
      .catch((e) => alive && setRes({ key: k, data: null, error: apiErrorMessage(e, "Kesimni yuklab bo'lmadi") }))
    return () => {
      alive = false
    }
  }, [period.kind, period.key])

  const data = res?.key === key ? res.data : null
  const error = res?.key === key ? res.error : null
  const loading = res?.key !== key

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <RatingPeriodSwitch value={period} onChange={setPeriod} />
        {loading && <span className="text-sm text-slate-400">Yuklanmoqda…</span>}
      </div>
      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {data && (
        <>
          <SectionTable title="Guruhlar" rows={data.groups} showHomework={data.homeworkWeight > 0} groups />
          <SectionTable title="Maktab sinflari" rows={data.grades} showHomework={data.homeworkWeight > 0} />
          <p className="text-xs text-slate-400">
            O'rtacha — shu davrda reytingga kirgan o'quvchilar bo'yicha (bal, testlar va ball 0..100). Sinf — o'quvchining joriy
            maktab sinfi, sinf qatorida o'quvchi bir marta (markaz bali). Davomat — davr kunlaridagi barcha darslar.
          </p>
        </>
      )}
    </div>
  )
}

function SectionTable({ title, rows, showHomework, groups }: { title: string; rows: RatingSectionRow[]; showHomework: boolean; groups?: boolean }) {
  const pct = (n: number | null) => (n == null ? '—' : `${fmtScore(n)}%`)
  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="border-b border-slate-100 px-4 py-3 text-sm font-bold text-slate-700">
        {title} <span className="font-normal text-slate-400">· {rows.length}</span>
      </div>
      {rows.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">Bu davrda ma'lumot yo'q</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-slate-100 text-left text-xs uppercase text-slate-400">
                <th className="px-4 py-2 font-medium">{groups ? 'Guruh' : 'Sinf'}</th>
                <th className="px-4 py-2 text-right font-medium">O'quvchi</th>
                <th className="px-4 py-2 text-right font-medium">Reyting bali</th>
                <th className="px-4 py-2 text-right font-medium">Testlar</th>
                <th className="px-4 py-2 text-right font-medium">Ball</th>
                {showHomework && <th className="px-4 py-2 text-right font-medium">Uy vazifasi</th>}
                <th className="px-4 py-2 text-right font-medium">Davomat</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.key} className="border-b border-slate-50 last:border-0">
                  <td className="px-4 py-2">
                    <div className="font-medium text-slate-700">{r.label}</div>
                    {groups && (r.course || r.teacher) && (
                      <div className="text-xs text-slate-400">{[r.course, r.teacher].filter(Boolean).join(' · ')}</div>
                    )}
                  </td>
                  <td className="px-4 py-2 text-right tabular-nums">{r.students}</td>
                  <td className="px-4 py-2 text-right font-semibold tabular-nums">{fmtScore(r.avgScore)}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{pct(r.avgTest)}</td>
                  <td className="px-4 py-2 text-right tabular-nums">{pct(r.avgBall)}</td>
                  {showHomework && <td className="px-4 py-2 text-right tabular-nums">{pct(r.avgHomework)}</td>}
                  <td
                    className={cn('px-4 py-2 text-right tabular-nums', r.attendancePct != null && r.attendancePct < 75 && 'text-amber-600')}
                    title={r.attendanceMarks ? `${r.attendanceMarks} ta davomat belgisi (o'quvchi × dars)` : undefined}
                  >
                    {r.attendancePct == null ? '—' : `${r.attendancePct}%`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
