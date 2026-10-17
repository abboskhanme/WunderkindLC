import { Fragment, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, Download, Percent, RefreshCw, Search, Users, Wallet, Archive } from 'lucide-react'
import { getDebtReport, type DebtReport, type DebtRow } from '@/api/services/financeReports'
import { getTeachers } from '@/api/services/teachers'
import { getClasses } from '@/api/services/classes'
import type { Group, Teacher } from '@/types'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { formatMonth, monthShortNames } from '@/config/constants'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, exportToCsv, formatMoney } from '@/lib/utils'
import { formatPhone } from '@/lib/numberFormat'
import {
  ageDaysOf, ageTone, bucketTone, matchesSearch, memberStatusLabel, pctLabel,
} from '@/lib/financeReports'

/**
 * QARZDORLIK TAHLILI (TZ 1.1) — oylar × o'quvchi matritsasi, qarz yoshi (7 / 30 / 60+ kun), yig'ilish foizi
 * va arxivdagi qarzdorlar belgisi.
 *
 * Hisob SERVERDA (`GET /admin/reports/debts`, `DebtLedger`): kassadagi to'lov oynasi qoidasi — har oy
 * mustaqil, keyingi oy avansi o'tgan oy qarzini yopmaydi. Sahifa qidiruv va yosh guruhi bo'yicha
 * filtrni klientda qiladi.
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'
const chip = (on: boolean) =>
  cn(
    'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
    on ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
  )

/** "2026-10" → "Okt 26" (ustun sarlavhasi qisqa bo'lsin). */
const shortMonth = (m: string) =>
  m.length >= 7 ? `${monthShortNames[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(2, 4)}` : m

const money = (n: number) => (n ? formatMoney(n) : '—')

const AGE_FILTERS: Array<[number, string]> = [
  [0, 'Hammasi'],
  [7, '7+ kun'],
  [30, '30+ kun'],
  [60, '60+ kun'],
]

const PERIODS = [1, 3, 6, 12]

export function DebtReportPage() {
  const cur = currentMonth()
  const [from, setFrom] = useState(() => shiftMonth(cur, -5))
  const [to, setTo] = useState(cur)
  const [teacherId, setTeacherId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [minAge, setMinAge] = useState(0)
  const [includeArchived, setIncludeArchived] = useState(false)
  const [search, setSearch] = useState('')
  const [bucket, setBucket] = useState('')
  const [tick, setTick] = useState(0)
  const [data, setData] = useState<DebtReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [groups, setGroups] = useState<Group[]>([])

  useEffect(() => {
    // Filtr ro'yxatlari — ikkilamchi o'qish: xato bo'lsa filtr shunchaki bo'sh qoladi, hisobot ishlayveradi.
    getTeachers().then(setTeachers).catch(() => {})
    getClasses(true).then(setGroups).catch(() => {})
  }, [])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getDebtReport({
      from, to, includeArchived,
      groupId: groupId || undefined,
      teacherId: teacherId || undefined,
      minAge: minAge || undefined,
    })
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [from, to, includeArchived, groupId, teacherId, minAge, tick])

  const rows = useMemo(
    () => (data?.rows ?? []).filter((r) =>
      (!bucket || r.bucket === bucket)
      && matchesSearch(search, [r.studentName, r.groupName, r.courseName, r.teacherName], r.phone)),
    [data, bucket, search],
  )

  /** Guruh bo'yicha bo'limlar (server tartibi saqlanadi: guruh nomi, keyin qarz). */
  const sections = useMemo(() => {
    const map = new Map<string, DebtRow[]>()
    for (const r of rows) {
      const list = map.get(r.groupId)
      if (list) list.push(r)
      else map.set(r.groupId, [r])
    }
    return [...map.values()]
  }, [rows])

  const today = useMemo(() => new Date(), [])
  const months = data?.months ?? []
  const t = data?.total

  const setPeriod = (n: number) => { setFrom(shiftMonth(cur, -(n - 1))); setTo(cur) }
  const periodIs = (n: number) => to === cur && from === shiftMonth(cur, -(n - 1))

  const exportCsv = () => {
    if (!data) return
    exportToCsv(
      `qarzdorlik_${data.from}_${data.to}.csv`,
      ["O'quvchi", 'Telefon', 'Guruh', 'Kurs', "O'qituvchi", ...months.map(formatMonth), 'Jami qarz', 'Eng eski oy', 'Kechikkan kun'],
      rows.map((r) => [
        r.studentName, r.phone, r.groupName, r.courseName, r.teacherName,
        ...months.map((m) => String(r.cells[m] ?? '')),
        String(r.debt), formatMonth(r.oldestMonth), String(Math.max(0, r.ageDays)),
      ]),
    )
  }

  return (
    <div>
      <PageHeader
        title="Qarzdorlik tahlili"
        sub="Kim, qaysi oy uchun va qancha vaqtdan beri qarzdor — oylar kesimida"
        actions={
          <Button variant="secondary" onClick={exportCsv} disabled={!data || rows.length === 0}>
            <Download className="h-4 w-4" /> CSV
          </Button>
        }
      />

      {/* ==================== FILTRLAR ==================== */}
      <div className="toolbar mb-3 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <input type="month" value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr boshi" />
        <span className="text-slate-400">—</span>
        <input type="month" value={to} min={from} max={cur} onChange={(e) => e.target.value && setTo(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr oxiri" />
        <div className="flex gap-1">
          {PERIODS.map((n) => (
            <button key={n} type="button" onClick={() => setPeriod(n)} className={chip(periodIs(n))}>
              {n === 1 ? 'Joriy oy' : `${n} oy`}
            </button>
          ))}
        </div>
        <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={control} aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {teachers.map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
        </select>
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} className={control} aria-label="Guruh">
          <option value="">Barcha guruhlar</option>
          {groups.map((g) => <option key={g.id} value={g.id}>{g.name}{g.isArchived ? ' (arxiv)' : ''}</option>)}
        </select>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>
      <div className="toolbar mb-4 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Qarz yoshi:</span>
        <div className="flex gap-1">
          {AGE_FILTERS.map(([n, label]) => (
            <button key={n} type="button" onClick={() => setMinAge(n)} className={chip(minAge === n)}>{label}</button>
          ))}
        </div>
        <label className="ml-2 inline-flex cursor-pointer items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)}
            className="h-4 w-4 accent-brand-600" />
          Arxivdagi (ketgan) qarzdorlar ham
        </label>
        <div className="relative ml-auto w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="O'quvchi, telefon, guruh..."
            className={cn(control, 'w-full pl-9')} />
        </div>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading || !data || !t ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {/* ==================== KO'RSATKICHLAR ==================== */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <StatCard label="Hisoblangan" value={formatMoney(t.charged)} icon={Wallet}
              hint={`Chegirma: ${formatMoney(t.discount)} · sof: ${formatMoney(t.net)}`} />
            <StatCard label="To'langan" value={formatMoney(t.paid)} icon={Wallet}
              iconBg="bg-sky-50" iconColor="text-sky-600" hint="Shu oylar hisobini yopgan pul" />
            <StatCard label="Qoldiq qarz" value={formatMoney(t.debt)} icon={AlertTriangle}
              iconBg="bg-rose-50" iconColor="text-rose-600" hint={`${t.debtors} ta qarzdor o'quvchi`} />
            <StatCard label="Yig'ilish foizi" value={pctLabel(t.collectionPct)} icon={Percent}
              iconBg="bg-amber-50" iconColor="text-amber-600" hint="To'langan ÷ sof hisoblangan" />
            <StatCard label="Arxivdagi qarz" value={formatMoney(data.archived.debt)} icon={Archive}
              iconBg="bg-slate-100" iconColor="text-slate-600"
              hint={`${data.archived.debtors} ta ketgan o'quvchi · ${includeArchived ? 'hisobga qo\'shilgan' : 'hisobga qo\'shilmagan'}`} />
          </div>

          {/* ==================== QARZ YOSHI ==================== */}
          <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-5">
            {data.buckets.map((b) => (
              <button key={b.key} type="button" onClick={() => setBucket((x) => (x === b.key ? '' : b.key))}
                className={cn('rounded-xl border p-3 text-left transition-shadow', bucketTone(b.key),
                  bucket === b.key ? 'ring-2 ring-brand-400' : 'hover:shadow-sm')}>
                <div className="text-xs font-semibold uppercase tracking-wide opacity-80">{b.label}</div>
                <div className="mt-1 text-base font-semibold tabular-nums">{formatMoney(b.debt)}</div>
                <div className="text-xs opacity-80">{b.students} ta o'quvchi</div>
              </button>
            ))}
          </div>
          <p className="text-xs text-slate-400">
            Qarz yoshi — eng eski to'lanmagan oyning to'lov muddatidan (har oyning {data.dueDay}-sanasi) beri o'tgan kunlar.
            Kartani bosib shu guruhni ajrating.
          </p>

          {/* ==================== MATRITSA ==================== */}
          <Card title="Qarzlar matritsasi" sub={`${rows.length} ta qator · ${formatMonth(data.from)} — ${formatMonth(data.to)}`} tight>
            {rows.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">
                {data.rows.length ? 'Filtrga mos qarzdor topilmadi' : 'Bu davrda qarzdor yo\'q'}
              </p>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>O'quvchi</th>
                      {months.map((m) => <th key={m} className="num">{shortMonth(m)}</th>)}
                      <th className="num">Jami</th>
                      <th className="num">Kechikkan</th>
                    </tr>
                  </thead>
                  <tbody>
                    {sections.map((list) => {
                      const head = list[0]
                      return (
                        <Fragment key={head.groupId || 'nogroup'}>
                          <tr className="bg-slate-50">
                            <td className="font-semibold text-slate-700">
                              {head.groupName}
                              <span className="ml-2 text-xs font-normal text-slate-400">
                                {[head.courseName, head.teacherName].filter(Boolean).join(' · ')}
                              </span>
                            </td>
                            {months.map((m) => (
                              <td key={m} className="num text-xs font-semibold text-slate-500">
                                {money(list.reduce((s, r) => s + (r.cells[m] ?? 0), 0))}
                              </td>
                            ))}
                            <td className="num font-semibold text-slate-700">{money(list.reduce((s, r) => s + r.debt, 0))}</td>
                            <td className="num text-xs text-slate-400">{list.length} ta</td>
                          </tr>
                          {list.map((r) => {
                            const status = memberStatusLabel(r.memberStatus)
                            return (
                              <tr key={`${r.studentId}:${r.groupId}`}>
                                <td>
                                  <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-700 hover:text-brand-600">
                                    {r.studentName}
                                  </Link>
                                  <div className="mt-0.5 flex flex-wrap items-center gap-1.5 text-xs text-slate-400">
                                    {r.phone && <span className="tabular-nums">{formatPhone(r.phone)}</span>}
                                    {r.isArchived && <Badge tone="default">Arxiv</Badge>}
                                    {status && <Badge tone="indigo">{status}</Badge>}
                                  </div>
                                </td>
                                {months.map((m) => {
                                  const v = r.cells[m]
                                  return (
                                    <td key={m} className={cn('num tabular-nums', v ? ageTone(ageDaysOf(m, data.dueDay, today)) : 'text-slate-300')}>
                                      {v ? formatMoney(v) : '·'}
                                    </td>
                                  )
                                })}
                                <td className="num font-semibold tabular-nums text-slate-800">{formatMoney(r.debt)}</td>
                                <td className={cn('num tabular-nums', ageTone(r.ageDays))}>
                                  {r.ageDays < 0 ? 'muddati kelmagan' : `${r.ageDays} kun`}
                                </td>
                              </tr>
                            )
                          })}
                        </Fragment>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
          <p className="flex items-center gap-1.5 text-xs text-slate-400">
            <Users className="h-3.5 w-3.5" />
            Hisob kassadagi to'lov oynasi bilan bir xil: har oy alohida, keyingi oyga to'langan avans o'tgan oy qarzini yopmaydi.
          </p>
        </div>
      )}
    </div>
  )
}
