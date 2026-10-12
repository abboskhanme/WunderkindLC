import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Trophy } from 'lucide-react'
import { getRatingGroup, type RatingGroupTable } from '@/api/services/rating'
import { defaultRatingPeriod, fmtScore, scoreFormulaText, type RatingPeriodValue } from '@/lib/ratingPeriods'
import { apiErrorMessage, cn } from '@/lib/utils'
import { RatingPeriodSwitch } from './RatingPeriodSwitch'

const medal = (rank: number | null) =>
  rank === 1 ? 'bg-amber-100 text-amber-700' : rank === 2 ? 'bg-slate-200 text-slate-700' : rank === 3 ? 'bg-orange-100 text-orange-700' : 'bg-slate-100 text-slate-500'

/**
 * Guruhning UMUMIY reytingi (testlar + ball) — `GET /admin/rating/groups/{id}`.
 * Guruh sahifasining «Reyting» tabida (tepada) va «Hisobotlar → Reyting → Guruhlar» da.
 * O'rinsiz qatorlar — shu davrda testi ham, bali ham yo'q a'zolar (pastda, «—» bilan).
 */
export function GroupRatingSection({ groupId, title = 'Umumiy reyting (testlar + ball)', linkStudents = true }: {
  groupId: string
  title?: string
  /** O'quvchi ismi profilga havola bo'lsinmi. */
  linkStudents?: boolean
}) {
  const [period, setPeriod] = useState<RatingPeriodValue>(() => defaultRatingPeriod(new Date()))
  const [res, setRes] = useState<{ key: string; data: RatingGroupTable | null; error: string | null } | null>(null)
  const key = `${groupId}|${period.kind}:${period.key}`

  useEffect(() => {
    let alive = true
    const k = `${groupId}|${period.kind}:${period.key}`
    getRatingGroup(groupId, { period: period.kind, key: period.key })
      .then((data) => alive && setRes({ key: k, data, error: null }))
      .catch((e) => alive && setRes((prev) => ({ key: k, data: prev?.data ?? null, error: apiErrorMessage(e, "Guruh reytingini yuklab bo'lmadi") })))
    return () => {
      alive = false
    }
  }, [groupId, period.kind, period.key])

  const data = res?.data?.group.id === groupId ? res.data : null
  const loading = res?.key !== key
  const error = res?.key === key ? res.error : null

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2 font-semibold text-slate-800">
            <Trophy className="h-5 w-5 text-indigo-500" /> {title}
          </div>
          {data && (
            <div className="mt-0.5 text-xs text-slate-500">
              {data.period.label} · testlar {data.testWeight}% + ball {100 - data.testWeight}% · o'rin olganlar: {data.ranked}
            </div>
          )}
        </div>
        <RatingPeriodSwitch value={period} onChange={setPeriod} />
      </div>

      {error && <div className="mx-4 mt-3 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
      {loading && !data && <div className="px-4 py-8 text-center text-sm text-slate-400">Reyting yuklanmoqda...</div>}

      {data && (
        <div className={cn('overflow-x-auto transition-opacity', loading && 'opacity-60')}>
          {data.items.length === 0 ? (
            <div className="px-4 py-8 text-center text-sm text-slate-400">Bu guruhda o'quvchi yo'q.</div>
          ) : (
            <table className="w-full min-w-[560px] text-sm">
              <thead>
                <tr className="text-left text-xs uppercase tracking-wide text-slate-500">
                  <th className="px-4 py-2 font-semibold">O'rin</th>
                  <th className="px-2 py-2 font-semibold">F.I.Sh</th>
                  <th className="px-2 py-2 text-right font-semibold">Testlar</th>
                  <th className="px-2 py-2 text-right font-semibold">Ball</th>
                  <th className="px-4 py-2 text-right font-semibold">Reyting bali</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((r) => (
                  <tr key={r.studentId} className="border-t border-slate-100">
                    <td className="px-4 py-2">
                      <span className={cn('inline-flex h-7 min-w-7 items-center justify-center rounded-lg px-1.5 text-xs font-bold', medal(r.rank))}>
                        {r.rank ?? '—'}
                      </span>
                    </td>
                    <td className="px-2 py-2 font-medium text-slate-800">
                      {linkStudents ? (
                        <Link to={`/admin/students/${r.studentId}`} className="hover:text-indigo-600 hover:underline">
                          {r.fullName}
                        </Link>
                      ) : (
                        r.fullName
                      )}
                    </td>
                    <td className="whitespace-nowrap px-2 py-2 text-right">
                      {r.testAvg != null ? (
                        <>
                          <b>{fmtScore(r.testAvg)}%</b> <span className="text-xs text-slate-400">· {r.testCount} ta</span>
                        </>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="whitespace-nowrap px-2 py-2 text-right">
                      {r.ball}
                      {r.ballNorm != null && <span className="text-xs text-slate-400"> · {fmtScore(r.ballNorm)}</span>}
                    </td>
                    <td
                      className="whitespace-nowrap px-4 py-2 text-right font-bold text-indigo-600"
                      title={scoreFormulaText(r.testAvg, r.ballNorm, data.testWeight)}
                    >
                      {fmtScore(r.score)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}
    </div>
  )
}
