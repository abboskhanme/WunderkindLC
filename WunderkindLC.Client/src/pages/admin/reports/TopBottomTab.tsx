import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, ThumbsUp, AlertCircle } from 'lucide-react'
import { getRatingTopBottom, type RatingTopBottomScope, type TopBottom, type TopBottomRow } from '@/api/services/rating'
import { fmtScore, monthKeyOf, monthLabel, shiftMonth } from '@/lib/ratingPeriods'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * HISOBOTLAR → REYTING → «TOP/BOTTOM» (CRM U4d, docs/SPEC-crm-upgrade.md P4.5). Oy (standart — O'TGAN oy, avto-xabar
 * aynan shu oy uchun ketadi) testlari o'rtachasi bo'yicha har guruhda (yoki butun markazda) k = min(N, ⌊n/2⌋) eng
 * yaxshi va eng past natija. Ro'yxat serverdagi yuboruvchi bilan BITTA funksiyadan (`TopBottomService`) — ko'ringan
 * = yuboriladigan. Ranglar sky/rose (yashil/qizil EMAS — `course-analytics.md` §6).
 */
export function TopBottomTab() {
  const thisMonth = monthKeyOf(new Date())
  const [month, setMonth] = useState(() => shiftMonth(thisMonth, -1))
  const [scope, setScope] = useState<RatingTopBottomScope | ''>('')
  const key = `${month}|${scope}`
  const [res, setRes] = useState<{ key: string; data: TopBottom | null; error: string | null } | null>(null)

  useEffect(() => {
    let alive = true
    const k = `${month}|${scope}`
    getRatingTopBottom({ month, scope })
      .then((data) => alive && setRes({ key: k, data, error: null }))
      .catch((e) => alive && setRes({ key: k, data: null, error: apiErrorMessage(e, "TOP/BOTTOM ro'yxatini yuklab bo'lmadi") }))
    return () => {
      alive = false
    }
  }, [month, scope])

  const data = res?.data ?? null
  const loading = res?.key !== key
  const error = res?.key === key ? res.error : null

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <div className="inline-flex items-center rounded-lg border border-slate-200 bg-white">
          <button
            type="button"
            onClick={() => setMonth((m) => shiftMonth(m, -1))}
            className="flex h-8 w-8 items-center justify-center text-slate-500 hover:text-slate-800"
            aria-label="Oldingi oy"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="min-w-[120px] text-center text-sm font-semibold text-slate-800">{monthLabel(month)}</span>
          <button
            type="button"
            onClick={() => setMonth((m) => shiftMonth(m, 1))}
            disabled={month >= thisMonth}
            className="flex h-8 w-8 items-center justify-center text-slate-500 hover:text-slate-800 disabled:opacity-30"
            aria-label="Keyingi oy"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
        <label className="flex items-center gap-2 text-sm text-slate-600">
          Qamrov:
          <select
            value={scope}
            onChange={(e) => setScope(e.target.value as RatingTopBottomScope | '')}
            className="rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-[16px] sm:text-sm"
          >
            <option value="">Sozlamadagi</option>
            <option value="group">Har guruh</option>
            <option value="centre">Butun markaz</option>
          </select>
        </label>
      </div>

      {data && (
        <p className="text-xs text-slate-500">
          {data.monthLabel} testlari o'rtachasi bo'yicha · {data.scope === 'centre' ? 'butun markaz' : 'har guruh'} · TOP/BOTTOM
          soni: {data.count} (kichik guruhda — o'quvchilar sonining yarmigacha). Xabarlar «Oylik TOP — tashakkurnoma» va
          «Oylik quyi natija — e'tibor» avto-xabar qoidalari kunida ketadi (Xabarlar → Avto xabarlar).
        </p>
      )}
      {error && <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
      {loading && !data && <div className="py-10 text-center text-sm text-slate-400">Yuklanmoqda...</div>}

      {data && data.groups.length === 0 && !loading && (
        <div className="rounded-xl border border-slate-200 bg-white px-4 py-10 text-center text-sm text-slate-400">
          Bu oyda kamida 2 o'quvchisining test natijasi bor guruh yo'q.
        </div>
      )}

      {data && data.groups.length > 0 && (
        <div className={cn('grid grid-cols-1 gap-3 lg:grid-cols-2', loading && 'opacity-60')}>
          {data.groups.map((g) => (
            <div key={g.groupId || 'centre'} className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
              <div className="flex items-center justify-between border-b border-slate-100 px-4 py-2.5">
                <p className="font-semibold text-slate-800">{g.groupName}</p>
                <span className="text-xs text-slate-500">{g.scored} o'quvchi</span>
              </div>
              <div className="grid grid-cols-1 gap-3 p-3 sm:grid-cols-2">
                <ListBox title="TOP" icon={<ThumbsUp className="h-3.5 w-3.5" />} tone="sky" rows={g.top} scored={g.scored} />
                <ListBox title="BOTTOM" icon={<AlertCircle className="h-3.5 w-3.5" />} tone="rose" rows={g.bottom} scored={g.scored} />
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function ListBox({
  title,
  icon,
  tone,
  rows,
  scored,
}: {
  title: string
  icon: React.ReactNode
  tone: 'sky' | 'rose'
  rows: TopBottomRow[]
  scored: number
}) {
  return (
    <div>
      <p
        className={cn(
          'mb-1.5 inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-bold',
          tone === 'sky' ? 'bg-sky-50 text-sky-700' : 'bg-rose-50 text-rose-700',
        )}
      >
        {icon} {title}
      </p>
      {rows.length === 0 ? (
        <p className="text-xs text-slate-400">—</p>
      ) : (
        <ul className="space-y-1">
          {rows.map((r) => (
            <li key={r.studentId} className="flex items-center gap-2 text-sm">
              <span className="w-10 shrink-0 text-xs text-slate-400">
                {r.rank}/{scored}
              </span>
              <Link to={`/admin/students/${r.studentId}`} className="min-w-0 flex-1 truncate text-slate-700 hover:text-indigo-600 hover:underline">
                {r.fullName}
              </Link>
              <b className={cn('shrink-0', tone === 'sky' ? 'text-sky-700' : 'text-rose-700')}>{fmtScore(r.testAvg)}%</b>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
