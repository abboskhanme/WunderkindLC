import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Search, Settings2 } from 'lucide-react'
import type { Group } from '@/types'
import { getClasses } from '@/api/services/classes'
import { getRatingCentre, type RatingCentre } from '@/api/services/rating'
import { PageHeader } from '@/components/ui/PageHeader'
import { Button } from '@/components/ui/Button'
import { RatingPeriodSwitch } from '@/components/rating/RatingPeriodSwitch'
import { GroupRatingSection } from '@/components/rating/GroupRatingSection'
import { RatingSettingsModal } from '@/components/rating/RatingSettingsModal'
import { defaultRatingPeriod, fmtScore, type RatingPeriodValue } from '@/lib/ratingPeriods'
import { usePerm } from '@/lib/permissions'
import { tabFromUrl } from '@/lib/tabParam'
import { apiErrorMessage, cn } from '@/lib/utils'

type Tab = 'markaz' | 'guruhlar'
const TABS = ['markaz', 'guruhlar'] as const
const PAGE_SIZE = 50

/**
 * HISOBOTLAR → REYTING (`/admin/hisobotlar/reyting`, ruxsat `classes.rating`).
 * «Markaz» — butun markaz jadvali (qidiruv, sahifalash), «Guruhlar» — tanlangan guruh jadvali.
 * Reyting bali = testlar o'rtachasi × ulush + ball (guruhdagi eng yaxshiga nisbatan) × qolgani;
 * hammasi serverda (`.claude/rules/rating.md`). CSV yo'q (SPEC).
 */
export function RatingReportPage() {
  const { can } = usePerm()
  const [tab, setTab] = useState<Tab>(() => tabFromUrl(TABS, 'markaz'))
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [reloadTick, setReloadTick] = useState(0)

  return (
    <div>
      <PageHeader
        title="Reyting"
        sub="O'quvchilar test natijalari va jurnal bali bo'yicha qaysi o'rinda — oy, 3 oy yoki o'quv yili kesimida."
        actions={
          can('classes.rating', 'edit') ? (
            <Button variant="secondary" onClick={() => setSettingsOpen(true)}>
              <Settings2 className="h-4 w-4" /> Sozlamalar
            </Button>
          ) : undefined
        }
      />

      <div className="mb-4 flex gap-2" role="tablist">
        <button type="button" className={cn('tab', tab === 'markaz' && 'active')} onClick={() => setTab('markaz')}>
          Markaz
        </button>
        <button type="button" className={cn('tab', tab === 'guruhlar' && 'active')} onClick={() => setTab('guruhlar')}>
          Guruhlar
        </button>
      </div>

      {tab === 'markaz' ? <CentreTab key={reloadTick} /> : <GroupsTab key={reloadTick} />}

      {settingsOpen && (
        <RatingSettingsModal onClose={() => setSettingsOpen(false)} onSaved={() => setReloadTick((t) => t + 1)} />
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ Markaz */

function CentreTab() {
  const [period, setPeriod] = useState<RatingPeriodValue>(() => defaultRatingPeriod(new Date()))
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [res, setRes] = useState<{ key: string; data: RatingCentre | null; error: string | null } | null>(null)
  const key = `${period.kind}:${period.key}|${query}|${page}`

  // Qidiruv — yozib bo'lgach 300 ms dan keyin (har harfga so'rov ketmasin).
  useEffect(() => {
    const t = setTimeout(() => {
      setQuery(search.trim())
      setPage(1)
    }, 300)
    return () => clearTimeout(t)
  }, [search])

  useEffect(() => {
    let alive = true
    const k = `${period.kind}:${period.key}|${query}|${page}`
    getRatingCentre({ period: period.kind, key: period.key, q: query, page, pageSize: PAGE_SIZE })
      .then((data) => alive && setRes({ key: k, data, error: null }))
      .catch((e) => alive && setRes((prev) => ({ key: k, data: prev?.data ?? null, error: apiErrorMessage(e, "Reytingni yuklab bo'lmadi") })))
    return () => {
      alive = false
    }
  }, [period.kind, period.key, query, page])

  const data = res?.data ?? null
  const loading = res?.key !== key
  const error = res?.key === key ? res.error : null
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  const changePeriod = (p: RatingPeriodValue) => {
    setPeriod(p)
    setPage(1)
  }

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <RatingPeriodSwitch value={period} onChange={changePeriod} />
        <label className="relative block w-full sm:w-64">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="O'quvchini qidirish"
            className="w-full rounded-lg border border-slate-200 py-1.5 pl-8 pr-3 text-[16px] sm:text-sm"
          />
        </label>
      </div>

      {data && (
        <div className="px-4 pt-3 text-xs text-slate-500">
          {data.period.label} · testlar {data.testWeight}% + ball {100 - data.testWeight}% · o'rin olganlar: {data.total}
        </div>
      )}
      {error && <div className="mx-4 mt-3 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
      {loading && !data && <div className="px-4 py-10 text-center text-sm text-slate-400">Reyting yuklanmoqda...</div>}

      {data && (
        <div className={cn('transition-opacity', loading && 'opacity-60')}>
          {data.items.length === 0 ? (
            <div className="px-4 py-10 text-center text-sm text-slate-400">
              {query ? 'Qidiruv bo\'yicha hech kim topilmadi.' : 'Bu davrda test ham, ball ham yo\'q.'}
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px] text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-slate-500">
                    <th className="px-4 py-2 font-semibold">O'rin</th>
                    <th className="px-2 py-2 font-semibold">F.I.Sh</th>
                    <th className="px-2 py-2 font-semibold">Guruhlar</th>
                    <th className="px-2 py-2 text-right font-semibold">Testlar</th>
                    <th className="px-2 py-2 text-right font-semibold">Ball</th>
                    <th className="px-4 py-2 text-right font-semibold">Reyting bali</th>
                  </tr>
                </thead>
                <tbody>
                  {data.items.map((r) => (
                    <tr key={r.studentId} className="border-t border-slate-100">
                      <td className="px-4 py-2">
                        <span
                          className={cn(
                            'inline-flex h-7 min-w-7 items-center justify-center rounded-lg px-1.5 text-xs font-bold',
                            r.rank === 1 ? 'bg-amber-100 text-amber-700' : r.rank === 2 ? 'bg-slate-200 text-slate-700' : r.rank === 3 ? 'bg-orange-100 text-orange-700' : 'bg-slate-100 text-slate-500',
                          )}
                        >
                          {r.rank}
                        </span>
                      </td>
                      <td className="px-2 py-2 font-medium text-slate-800">
                        <Link to={`/admin/students/${r.studentId}`} className="hover:text-indigo-600 hover:underline">
                          {r.fullName}
                        </Link>
                      </td>
                      <td className="px-2 py-2">
                        <div className="flex flex-wrap gap-1">
                          {r.groups.map((g) => (
                            <span key={g.id} className="rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600" title={g.score != null ? `Guruh bali: ${fmtScore(g.score)}` : undefined}>
                              {g.name}
                              {g.rank != null && <b className="ml-1 text-indigo-600">{g.rank}</b>}
                            </span>
                          ))}
                        </div>
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
                      <td className="whitespace-nowrap px-2 py-2 text-right">{r.ball}</td>
                      <td className="whitespace-nowrap px-4 py-2 text-right font-bold text-indigo-600">{fmtScore(r.score)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 border-t border-slate-100 px-4 py-2 text-sm text-slate-600">
              <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))} aria-label="Oldingi sahifa">
                <ChevronLeft className="h-4 w-4" />
              </Button>
              <span>
                {page} / {pages}
              </span>
              <Button variant="secondary" disabled={page >= pages} onClick={() => setPage((p) => p + 1)} aria-label="Keyingi sahifa">
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ Guruhlar */

function GroupsTab() {
  const [groups, setGroups] = useState<Group[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [picked, setPicked] = useState('')

  useEffect(() => {
    let alive = true
    getClasses()
      .then((list) => alive && setGroups(list))
      .catch((e) => alive && setError(apiErrorMessage(e, "Guruhlar ro'yxatini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [])

  const sorted = useMemo(
    () => (groups ?? []).slice().sort((a, b) => a.name.localeCompare(b.name)),
    [groups],
  )
  const groupId = picked || sorted[0]?.id || ''

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-slate-600">Guruh:</span>
        <select
          value={groupId}
          onChange={(e) => setPicked(e.target.value)}
          className="min-w-[220px] rounded-lg border border-slate-200 bg-white px-2.5 py-1.5 text-[16px] sm:text-sm"
          disabled={!groups}
        >
          {sorted.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </select>
      </div>
      {error && <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
      {!groups && !error && <div className="py-8 text-center text-sm text-slate-400">Guruhlar yuklanmoqda...</div>}
      {groups && sorted.length === 0 && <div className="py-8 text-center text-sm text-slate-400">Faol guruh yo'q.</div>}
      {groupId && <GroupRatingSection key={groupId} groupId={groupId} title="Guruh reytingi" />}
    </div>
  )
}
