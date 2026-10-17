import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, ChevronLeft, ChevronRight, Percent, RefreshCw, Search, Wallet } from 'lucide-react'
import { getTariffReport, type TariffGroup, type TariffReport } from '@/api/services/financeReports'
import { getTeachers } from '@/api/services/teachers'
import type { Teacher } from '@/types'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { formatMonth } from '@/config/constants'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { matchesSearch, pctLabel } from '@/lib/financeReports'

/**
 * GURUHLAR VA TARIFLAR (TZ 1.2) — bitta oy: har guruhda qaysi narxda nechta o'quvchi, rejadagi tushum,
 * yig'ilgan pul, qoldiq qarz; o'qituvchilar kesimida to'lov intizomi sust guruhlar.
 *
 * Hisob SERVERDA (`GET /admin/reports/tariffs`). O'qituvchi — shu oyning egasi (o'qituvchi tarixi bo'yicha).
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

const money = (n: number) => (n ? formatMoney(n) : '—')

/** Yig'ilish foizi rangi: sust (< chegara) → rose, 90% gacha → amber, aks holda → sky. */
const pctTone = (pct: number, weakPct: number) =>
  pct < weakPct ? 'text-rose-600' : pct < 90 ? 'text-amber-600' : 'text-sky-600'

type View = 'groups' | 'teachers'

export function TariffReportPage() {
  const cur = currentMonth()
  const [month, setMonth] = useState(cur)
  const [teacherId, setTeacherId] = useState('')
  const [view, setView] = useState<View>('groups')
  const [onlyWeak, setOnlyWeak] = useState(false)
  const [search, setSearch] = useState('')
  const [tick, setTick] = useState(0)
  const [data, setData] = useState<TariffReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [teachers, setTeachers] = useState<Teacher[]>([])

  useEffect(() => {
    // Filtr ro'yxati — ikkilamchi o'qish: xato bo'lsa filtr bo'sh qoladi, hisobot ishlayveradi.
    getTeachers().then(setTeachers).catch(() => {})
  }, [])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oy/o'qituvchi o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getTariffReport(month, teacherId)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [month, teacherId, tick])

  const groups = useMemo(
    () => (data?.groups ?? []).filter((g: TariffGroup) =>
      (!onlyWeak || g.weak) && matchesSearch(search, [g.groupName, g.courseName, g.teacherName])),
    [data, onlyWeak, search],
  )

  const t = data?.totals
  const weakCount = data?.groups.filter((g) => g.weak).length ?? 0

  return (
    <div>
      <PageHeader
        title="Guruhlar va tariflar"
        sub="Har guruhda qaysi narxda nechta o'quvchi, rejadagi va amaldagi tushum, qoldiq qarz"
      />

      <div className="toolbar mb-4 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, -1))} aria-label="Oldingi oy">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <input type="month" value={month} max={shiftMonth(cur, 3)} onChange={(e) => e.target.value && setMonth(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Oy" />
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, 1))} aria-label="Keyingi oy">
          <ChevronRight className="h-4 w-4" />
        </Button>
        <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={control} aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {teachers.map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
        </select>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading || !data || !t ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard label="Rejadagi tushum" value={formatMoney(t.charged)} icon={Wallet}
              hint={`Chegirma: ${formatMoney(t.discount)} · sof: ${formatMoney(t.net)}`} />
            <StatCard label="Yig'ilgan" value={formatMoney(data.groups.reduce((s, g) => s + g.collected, 0))} icon={Wallet}
              iconBg="bg-sky-50" iconColor="text-sky-600" hint={formatMonth(data.month)} />
            <StatCard label="Qoldiq qarz" value={formatMoney(t.debt)} icon={AlertTriangle}
              iconBg="bg-rose-50" iconColor="text-rose-600" hint={`${t.debtors} ta qarzdor o'quvchi`} />
            <StatCard label="Yig'ilish foizi" value={pctLabel(t.collectionPct)} icon={Percent}
              iconBg="bg-amber-50" iconColor="text-amber-600"
              hint={`${weakCount} ta guruhda ${pctLabel(data.weakPct)} dan past`} />
          </div>

          <div className="toolbar flex-wrap">
            <div className="flex gap-1">
              {([['groups', 'Guruhlar'], ['teachers', "O'qituvchilar"]] as Array<[View, string]>).map(([k, label]) => (
                <button key={k} type="button" onClick={() => setView(k)}
                  className={cn('rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                    view === k ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50')}>
                  {label}
                </button>
              ))}
            </div>
            {view === 'groups' && (
              <>
                <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-600">
                  <input type="checkbox" checked={onlyWeak} onChange={(e) => setOnlyWeak(e.target.checked)}
                    className="h-4 w-4 accent-brand-600" />
                  Faqat to'lov intizomi sust guruhlar
                </label>
                <div className="relative ml-auto w-full max-w-xs">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                  <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Guruh, kurs, o'qituvchi..."
                    className={cn(control, 'w-full pl-9')} />
                </div>
              </>
            )}
          </div>

          {view === 'groups' ? (
            <Card title="Guruhlar" sub={`${groups.length} ta guruh · ${formatMonth(data.month)}`} tight>
              {groups.length === 0 ? (
                <p className="py-10 text-center text-sm text-slate-400">Bu oyda hisob yozilgan guruh yo'q</p>
              ) : (
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Guruh</th>
                        <th>Tariflar</th>
                        <th className="num">Reja</th>
                        <th className="num">Chegirma</th>
                        <th className="num">Yig'ilgan</th>
                        <th className="num">Qarz</th>
                        <th className="num">Yig'ilish</th>
                      </tr>
                    </thead>
                    <tbody>
                      {groups.map((g) => (
                        <tr key={g.groupId || 'nogroup'} className={cn(g.weak && 'bg-rose-50/40')}>
                          <td>
                            {g.groupId ? (
                              <Link to={`/admin/classes/${g.groupId}`} className="font-medium text-slate-700 hover:text-brand-600">{g.groupName}</Link>
                            ) : <span className="font-medium text-slate-700">{g.groupName}</span>}
                            <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs text-slate-400">
                              <span>{[g.courseName, g.teacherName].filter(Boolean).join(' · ')}</span>
                              {g.isArchived && <Badge tone="default">Arxiv</Badge>}
                              {g.weak && <Badge tone="red">Sust</Badge>}
                            </div>
                          </td>
                          <td className="text-sm text-slate-600">
                            {g.prices.map((p) => (
                              <div key={p.price} className="tabular-nums">
                                <span className="font-semibold text-slate-700">{p.students} ta</span> × {formatMoney(p.price)}
                                {p.partial > 0 && <span className="text-xs text-slate-400"> ({p.partial} tasi qisman oy)</span>}
                              </div>
                            ))}
                          </td>
                          <td className="num tabular-nums">{money(g.planned)}</td>
                          <td className="num tabular-nums text-slate-500">{money(g.discount)}</td>
                          <td className="num tabular-nums">{money(g.collected)}</td>
                          <td className="num tabular-nums font-semibold text-slate-800">
                            {money(g.debt)}
                            {g.debtors > 0 && <div className="text-xs font-normal text-slate-400">{g.debtors} ta o'quvchi</div>}
                          </td>
                          <td className={cn('num font-semibold tabular-nums', pctTone(g.collectionPct, data.weakPct))}>
                            {pctLabel(g.collectionPct)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          ) : (
            <Card title="O'qituvchilar" sub="Eng past yig'ilish tepada — to'lov intizomi sust guruhlari bor o'qituvchilar" tight>
              {data.teachers.length === 0 ? (
                <p className="py-10 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
              ) : (
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>O'qituvchi</th>
                        <th className="num">Guruhlar</th>
                        <th className="num">O'quvchilar</th>
                        <th className="num">Reja</th>
                        <th className="num">Yig'ilgan</th>
                        <th className="num">Qarz</th>
                        <th className="num">Yig'ilish</th>
                        <th className="num">Sust guruhlar</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.teachers.map((x) => (
                        <tr key={x.teacherId || 'none'}>
                          <td className="font-medium text-slate-700">{x.teacherName}</td>
                          <td className="num">{x.groups}</td>
                          <td className="num">{x.students}</td>
                          <td className="num tabular-nums">{money(x.planned)}</td>
                          <td className="num tabular-nums">{money(x.collected)}</td>
                          <td className="num tabular-nums font-semibold">{money(x.debt)}</td>
                          <td className={cn('num font-semibold tabular-nums', pctTone(x.collectionPct, data.weakPct))}>{pctLabel(x.collectionPct)}</td>
                          <td className={cn('num', x.weakGroups ? 'font-semibold text-rose-600' : 'text-slate-400')}>{x.weakGroups || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}
          <p className="text-xs text-slate-400">
            Tarif — o'quvchining shu oydagi to'liq narxi (sinf oralig'i yoki guruh narxi), chegirma alohida.
            «Qisman oy» — oy o'rtasida aktivlashgan, muzlagan yoki chiqqan o'quvchi.
          </p>
        </div>
      )}
    </div>
  )
}
