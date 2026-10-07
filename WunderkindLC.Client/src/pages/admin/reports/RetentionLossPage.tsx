import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  Bar, BarChart, CartesianGrid, Cell, Legend, Pie, PieChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { RefreshCw, Search, ShieldAlert, ShieldCheck, UserMinus, Users } from 'lucide-react'
import {
  getRetentionLoss,
  type RetentionKind,
  type RetentionLossReason,
  type RetentionLossReport,
} from '@/api/services/retentionLoss'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { formatMonth, monthShortNames } from '@/config/constants'
import { apiErrorMessage, cn, exportToCsv, formatDate } from '@/lib/utils'
import {
  axisTick, barCursor, CATEGORICAL, gridStroke, MAX_SLICES, OTHER_GRAY, tooltipStyle,
} from '@/pages/admin/leads/stats/palette'

/**
 * «KETISH SABABLARI» (Retention / Loss) — o'qituvchilar kesimida kim, qachon va NEGA ketdi.
 *
 * Butun hisob SERVERDA (`GET /admin/retention-loss`, `RetentionLossReport`) — bu sahifa faqat
 * chizadi va jadvalda matn qidiruvini qiladi. "Ketdi" ta'rifi bosh sahifadagi «Ketgan
 * o'quvchilar» kartochkasi bilan AYNAN bir xil (`.claude/rules/retention-loss.md`).
 *
 * Ranglar (`.claude/rules/course-analytics.md` §6 + dataviz validatori):
 *  - `#e11d48` — AKTIVDAN ketgan (asosiy yo'qotish), `#d97706` — SINOVDAN ketgan.
 *    Juftlik tekshirilgan: CVD ΔE 9.4 (deutan), oddiy ko'rish ΔE 16.6, fonda ≥ 3:1.
 *  - sabablar donut'i — CRM statistikasidagi tekshirilgan kategorial palitra (`stats/palette.ts`).
 *  - ⚠️ Son va FOIZ bitta grafikda ikki y-o'q bilan KO'RSATILMAYDI — alohida grafiklar.
 */

const C_ACTIVE = '#e11d48'
const C_TRIAL = '#d97706'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Joriy oy ("yyyy-MM"), mahalliy vaqt bo'yicha. */
function thisMonth(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/** "yyyy-MM" ga oy qo'shish/ayirish. */
function addMonths(ym: string, delta: number): string {
  const y = Number(ym.slice(0, 4))
  const m = Number(ym.slice(5, 7))
  const d = new Date(Date.UTC(y, m - 1 + delta, 1))
  return d.toISOString().slice(0, 7)
}

/** "2026-03" → "Mar 26". */
const shortMonth = (m: string) =>
  m.length >= 7 ? `${monthShortNames[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(2, 4)}` : m

const pct = (n: number | null | undefined) => (n == null ? '—' : `${Number(n.toFixed(1))}%`)

const KINDS: Array<[RetentionKind, string]> = [
  ['all', 'Hammasi'],
  ['active', 'Aktivdan'],
  ['trial', 'Sinovdan'],
]

type Tab = 'rows' | 'teachers' | 'reasons'
const TABS: Array<[Tab, string]> = [
  ['rows', "Ketgan o'quvchilar"],
  ['teachers', "O'qituvchilar kesimi"],
  ['reasons', 'Sabablar'],
]

/* ============================== SABABLAR DONUT ============================== */

interface Slice extends RetentionLossReason {
  key: string
  color: string
  /** Jamidagi ulush (%). */
  share: number
}

/**
 * Eng ko'p 5 ta sabab o'z rangini oladi, qolgani «Boshqa» (kulrang). Rang sabab NOMIGA
 * bog'lanadi (reytingiga emas) — davr/filtr o'zgarganda qolgan sabablar rangi almashmasin
 * (`SourcesDonut` dagi qoida).
 */
function buildSlices(reasons: RetentionLossReason[]): Slice[] {
  const total = reasons.reduce((s, r) => s + r.count, 0)
  const share = (n: number) => (total ? (n / total) * 100 : 0)
  const sorted = [...reasons].sort((a, b) => b.count - a.count)
  const top = sorted.slice(0, MAX_SLICES)
  const rest = sorted.slice(MAX_SLICES)
  const colorOrder = [...top].sort((a, b) => a.label.localeCompare(b.label))
  const slices: Slice[] = top.map((r) => ({
    ...r,
    key: r.label,
    color: CATEGORICAL[colorOrder.findIndex((c) => c.label === r.label)] ?? OTHER_GRAY,
    share: share(r.count),
  }))
  if (rest.length > 0)
    slices.push({
      key: '__other__',
      label: `Boshqa (${rest.length} ta sabab)`,
      count: rest.reduce((s, r) => s + r.count, 0),
      outOfControl: false,
      color: OTHER_GRAY,
      share: share(rest.reduce((s, r) => s + r.count, 0)),
    })
  return slices
}

function ReasonTooltip({ active, payload }: {
  active?: boolean
  payload?: readonly { payload?: unknown }[]
}): ReactNode {
  if (!active || !payload?.length) return null
  const s = payload[0]?.payload as Slice | undefined
  if (!s) return null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="text-slate-700">
        <span className="font-mono font-semibold">{s.count}</span> ta ketish
        <span className="text-slate-400"> · {s.share.toFixed(1)}%</span>
      </p>
      <p className="mt-0.5 text-slate-500">{s.label}</p>
    </div>
  )
}

function ReasonsDonut({ reasons }: { reasons: RetentionLossReason[] }) {
  const slices = buildSlices(reasons)
  const total = slices.reduce((s, x) => s + x.count, 0)
  if (slices.length === 0)
    return <p className="py-12 text-center text-sm text-slate-400">Tanlangan davrda ketish yo'q</p>
  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row">
      <div className="relative w-full sm:w-1/2">
        <ResponsiveContainer width="100%" height={240}>
          <PieChart>
            <Pie
              data={slices}
              dataKey="count"
              nameKey="label"
              cx="50%"
              cy="50%"
              innerRadius={62}
              outerRadius={100}
              paddingAngle={1}
              stroke="#ffffff"
              strokeWidth={2}
              isAnimationActive={false}
            >
              {slices.map((s) => (
                <Cell key={s.key} fill={s.color} />
              ))}
            </Pie>
            <Tooltip content={ReasonTooltip} />
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="font-mono text-2xl font-semibold leading-none text-slate-800">{total}</span>
          <span className="mt-1 text-xs text-slate-400">ketish</span>
        </div>
      </div>
      <ul className="w-full space-y-2 sm:w-1/2">
        {slices.map((s) => (
          <li key={s.key} className="flex items-center gap-2 text-sm">
            <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ backgroundColor: s.color }} aria-hidden />
            <span className="min-w-0 flex-1 truncate text-slate-600" title={s.label}>
              {s.label}
              {s.outOfControl && <span className="ml-1 text-xs text-slate-400">(nazoratdan tashqari)</span>}
            </span>
            <span className="font-mono tabular-nums text-slate-800">{s.count}</span>
            <span className="w-12 text-right font-mono text-xs tabular-nums text-slate-500">
              {s.share.toFixed(1)}%
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

/* ============================== SAHIFA ============================== */

export function RetentionLossPage() {
  const [sp] = useSearchParams()
  const cur = thisMonth()
  const [from, setFrom] = useState(() => addMonths(cur, -5))
  const [to, setTo] = useState(cur)
  // O'qituvchi sahifasidan havola: `?teacher=<id>` — boshlang'ich filtr (keyin manzil o'zgarmaydi).
  const [teacherId, setTeacherId] = useState(() => sp.get('teacher') ?? '')
  const [courseId, setCourseId] = useState('')
  const [kind, setKind] = useState<RetentionKind>('all')
  const [data, setData] = useState<RetentionLossReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  /** «Yangilash» — effekt ichida setState o'rniga hisoblagich. */
  const [tick, setTick] = useState(0)
  const [tab, setTab] = useState<Tab>('rows')
  const [search, setSearch] = useState('')

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getRetentionLoss({ from, to, teacherId, courseId, kind })
      .then((d) => { if (alive) { setData(d); setError('') } })
      .catch((e) => { if (alive) setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
  }, [from, to, teacherId, courseId, kind, tick])

  const rangeIs = (n: number) => to === cur && from === addMonths(cur, -(n - 1))
  const setRange = (n: number) => { setFrom(addMonths(cur, -(n - 1))); setTo(cur) }

  const showActive = kind !== 'trial'
  const showTrial = kind !== 'active'

  const monthChart = useMemo(
    () => (data?.months ?? []).map((m) => ({ ...m, name: shortMonth(m.month) })),
    [data],
  )
  const teacherChart = useMemo(
    () => (data?.teachers ?? []).filter((t) => t.left > 0).slice(0, 15),
    [data],
  )
  const rateChart = useMemo(
    () => (data?.teachers ?? [])
      .filter((t) => t.lossRate != null && t.activeBase > 0)
      .sort((a, b) => (b.lossRate ?? 0) - (a.lossRate ?? 0))
      .slice(0, 15),
    [data],
  )

  const rows = useMemo(() => {
    const q = search.trim().toLowerCase()
    const all = data?.rows ?? []
    if (!q) return all
    return all.filter((r) =>
      [r.studentName, r.groupName, r.teacherName, r.courseName, r.reason]
        .some((x) => x.toLowerCase().includes(q)))
  }, [data, search])
  const pg = usePagination(rows)

  const handleExport = () => {
    exportToCsv(
      `ketish_sabablari_${from}_${to}.csv`,
      ["O'quvchi", 'Guruh', 'Kurs', "O'qituvchi", 'Sana', 'Turi', 'Sabab', 'Nazoratdan tashqari'],
      rows.map((r) => [
        r.studentName, r.groupName, r.courseName, r.teacherName, r.date,
        r.fromActive ? 'Aktivdan' : 'Sinovdan', r.reason || "Sabab ko'rsatilmagan",
        r.outOfControl ? 'ha' : '',
      ]),
    )
  }

  const t = data?.totals
  const chartHeight = (n: number) => Math.max(160, n * 34 + 40)

  return (
    <div>
      <PageHeader
        title="Ketish sabablari"
        sub="O'qituvchilar kesimida kim, qachon va nega ketdi — ushlab qolish (retention) va yo'qotish"
        actions={
          <Button variant="secondary" onClick={handleExport} disabled={rows.length === 0}>
            CSV
          </Button>
        }
      />

      {/* ==================== FILTRLAR — bitta qatorda ==================== */}
      <div className="toolbar mb-4 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <input type="month" value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr boshi" />
        <span className="text-slate-400">—</span>
        <input type="month" value={to} min={from} onChange={(e) => e.target.value && setTo(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr oxiri" />
        <div className="flex gap-1">
          {[1, 3, 6, 12].map((n) => (
            <button
              key={n}
              type="button"
              onClick={() => setRange(n)}
              className={cn(
                'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                rangeIs(n)
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              {n === 1 ? 'Joriy oy' : `${n} oy`}
            </button>
          ))}
        </div>
        <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={control}
          aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {(data?.teacherOptions ?? []).map((o) => (
            <option key={o.id} value={o.id}>{o.name}</option>
          ))}
          {/* Havoladan kelgan, lekin ro'yxatda yo'q (arxivdagi) o'qituvchi — tanlov yo'qolmasin. */}
          {teacherId && data && !data.teacherOptions.some((o) => o.id === teacherId) && (
            <option value={teacherId}>Tanlangan o'qituvchi</option>
          )}
        </select>
        <select value={courseId} onChange={(e) => setCourseId(e.target.value)} className={control} aria-label="Kurs">
          <option value="">Barcha kurslar</option>
          {(data?.courseOptions ?? []).map((o) => (
            <option key={o.id} value={o.id}>{o.name}</option>
          ))}
        </select>
        <div className="tabs">
          {KINDS.map(([k, label]) => (
            <button key={k} type="button" onClick={() => setKind(k)} className={cn('tab', kind === k && 'active')}>
              {label}
            </button>
          ))}
        </div>
        <Button variant="secondary" onClick={() => setTick((x) => x + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data || !t ? null : (
        <div className={cn('space-y-4', loading && 'opacity-60')}>
          {/* ==================== KPI ==================== */}
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard
              label="Ketganlar"
              value={t.left}
              icon={UserMinus}
              iconBg="bg-rose-50"
              iconColor="text-rose-600"
              hint={`Aktivdan ${t.leftActive} · sinovdan ${t.leftTrial} · ${t.students} ta o'quvchi`}
            />
            <StatCard
              label="Ushlab qolish"
              value={pct(t.retention)}
              icon={ShieldCheck}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint={`Ketish ${pct(t.lossRate)} — ${t.activeBase} ta aktiv o'quvchidan`}
            />
            <StatCard
              label="Nazoratdan tashqari"
              value={t.outOfControl}
              icon={ShieldAlert}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint="Ko'chib ketish, sog'liq va h.k. (Sabablar sozlamasidagi belgi)"
            />
            <StatCard
              label="Sababi yozilmagan"
              value={t.noReason}
              icon={Users}
              iconBg="bg-slate-100"
              iconColor="text-slate-600"
              hint="Chiqarishda sabab tanlanmagan"
            />
          </div>

          {/* ==================== GRAFIKLAR ==================== */}
          <div className="grid gap-4 xl:grid-cols-2">
            <Card title="Ketish sabablari" sub="Tanlangan davrdagi ketishlar — sabab bo'yicha ulush">
              <ReasonsDonut reasons={data.reasons} />
            </Card>

            <Card title="Oylik dinamika" sub="Har oyda nechta ketish bo'lgan (aktivdan / sinovdan)">
              <ResponsiveContainer width="100%" height={260}>
                <BarChart data={monthChart} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke={gridStroke} />
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                  <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={32} />
                  <Tooltip contentStyle={tooltipStyle} cursor={barCursor} />
                  <Legend wrapperStyle={{ fontSize: 12 }} />
                  {showActive && (
                    <Bar dataKey="leftActive" name="Aktivdan" stackId="m" fill={C_ACTIVE}
                      stroke="#ffffff" strokeWidth={2} maxBarSize={28}
                      radius={showTrial ? [0, 0, 0, 0] : [4, 4, 0, 0]} />
                  )}
                  {showTrial && (
                    <Bar dataKey="leftTrial" name="Sinovdan" stackId="m" fill={C_TRIAL}
                      stroke="#ffffff" strokeWidth={2} maxBarSize={28} radius={[4, 4, 0, 0]} />
                  )}
                </BarChart>
              </ResponsiveContainer>
            </Card>
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <Card title="O'qituvchilar bo'yicha ketganlar" sub="Ketish o'sha paytda guruhni O'QITGAN o'qituvchiga yoziladi">
              {teacherChart.length === 0 ? (
                <p className="py-12 text-center text-sm text-slate-400">Ketish yo'q</p>
              ) : (
                <ResponsiveContainer width="100%" height={chartHeight(teacherChart.length)}>
                  <BarChart data={teacherChart} layout="vertical" margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
                    <CartesianGrid horizontal={false} stroke={gridStroke} />
                    <XAxis type="number" tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
                    <YAxis type="category" dataKey="teacherName" tickLine={false} axisLine={false} tick={axisTick} width={130} />
                    <Tooltip contentStyle={tooltipStyle} cursor={barCursor} />
                    <Legend wrapperStyle={{ fontSize: 12 }} />
                    {showActive && (
                      <Bar dataKey="leftActive" name="Aktivdan" stackId="t" fill={C_ACTIVE}
                        stroke="#ffffff" strokeWidth={2} maxBarSize={22}
                        radius={showTrial ? [0, 0, 0, 0] : [0, 4, 4, 0]} />
                    )}
                    {showTrial && (
                      <Bar dataKey="leftTrial" name="Sinovdan" stackId="t" fill={C_TRIAL}
                        stroke="#ffffff" strokeWidth={2} maxBarSize={22} radius={[0, 4, 4, 0]} />
                    )}
                  </BarChart>
                </ResponsiveContainer>
              )}
            </Card>

            <Card
              title="Ketish foizi (aktivdan)"
              sub="Aktivdan ketganlar ÷ davrda o'qituvchining aktiv o'quvchilari"
            >
              {rateChart.length === 0 ? (
                <p className="py-12 text-center text-sm text-slate-400">Aktiv o'quvchi yo'q</p>
              ) : (
                <ResponsiveContainer width="100%" height={chartHeight(rateChart.length)}>
                  <BarChart data={rateChart} layout="vertical" margin={{ top: 0, right: 16, left: 0, bottom: 0 }}>
                    <CartesianGrid horizontal={false} stroke={gridStroke} />
                    <XAxis type="number" domain={[0, 100]} tickLine={false} axisLine={false} tick={axisTick}
                      tickFormatter={(v: number) => `${v}%`} />
                    <YAxis type="category" dataKey="teacherName" tickLine={false} axisLine={false} tick={axisTick} width={130} />
                    <Tooltip
                      contentStyle={tooltipStyle}
                      cursor={barCursor}
                      formatter={(v, _n, item) => {
                        const row = item?.payload as { leftActive?: number; activeBase?: number } | undefined
                        return [`${pct(Number(v))} (${row?.leftActive ?? 0} / ${row?.activeBase ?? 0})`, 'Ketish foizi']
                      }}
                    />
                    <Bar dataKey="lossRate" name="Ketish foizi" fill={C_ACTIVE} maxBarSize={22} radius={[0, 4, 4, 0]} />
                  </BarChart>
                </ResponsiveContainer>
              )}
            </Card>
          </div>

          {/* ==================== JADVALLAR ==================== */}
          <div className="flex flex-wrap gap-1 border-b border-slate-200">
            {TABS.map(([key, label]) => (
              <button key={key} type="button" className={cn('tab', tab === key && 'active')} onClick={() => setTab(key)}>
                {label}
              </button>
            ))}
          </div>

          {tab === 'rows' && (
            <Card
              title="Ketgan o'quvchilar"
              sub={`${formatMonth(data.from)} — ${formatMonth(data.to)}. Kurs ichida guruh almashtirish va sertifikat bilan tugatish bu yerga KIRMAYDI.`}
              tight
              actions={
                <div className="search-inline">
                  <Search className="h-4 w-4 shrink-0 text-slate-400" />
                  <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Ism, guruh, sabab..." />
                </div>
              }
            >
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>O'quvchi</th>
                      <th>Guruh / kurs</th>
                      <th>O'qituvchi</th>
                      <th>Sana</th>
                      <th>Turi</th>
                      <th>Sabab</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pg.paged.map((r) => (
                      <tr key={`${r.studentId}-${r.groupId}-${r.date}`}>
                        <td>
                          <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-800 no-underline hover:underline">
                            {r.studentName}
                          </Link>
                          {r.stillStudying && (
                            <div className="text-xs text-sky-600">Boshqa guruhda o'qiyapti</div>
                          )}
                        </td>
                        <td>
                          <div className="text-slate-700">{r.groupName || '—'}</div>
                          <div className="text-xs text-slate-400">{r.courseName}</div>
                        </td>
                        <td className="text-slate-600">{r.teacherName}</td>
                        <td className="whitespace-nowrap font-mono text-slate-600">{formatDate(r.date)}</td>
                        <td>
                          <Badge tone={r.fromActive ? 'red' : 'amber'}>{r.fromActive ? 'Aktivdan' : 'Sinovdan'}</Badge>
                        </td>
                        <td>
                          {r.reason ? (
                            <span className="text-slate-700">{r.reason}</span>
                          ) : (
                            <span className="text-slate-400">Sabab ko'rsatilmagan</span>
                          )}
                          {r.outOfControl && (
                            <div className="mt-0.5"><Badge tone="default">Nazoratdan tashqari</Badge></div>
                          )}
                        </td>
                      </tr>
                    ))}
                    {rows.length === 0 && (
                      <tr>
                        <td colSpan={6} className="px-4 py-12 text-center text-slate-400">
                          {search ? 'Qidiruvga mos qator topilmadi' : "Tanlangan davrda ketish yo'q"}
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
              <TablePagination {...pg} />
            </Card>
          )}

          {tab === 'teachers' && (
            <Card title="O'qituvchilar kesimi" sub="Foiz — faqat AKTIVDAN ketganlar, davrda aktiv bo'lgan o'quvchilariga nisbatan" tight>
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>O'qituvchi</th>
                      <th className="num">Aktiv o'quvchilar</th>
                      <th className="num">Aktivdan ketgan</th>
                      <th className="num">Sinovdan ketgan</th>
                      <th className="num">Ketish foizi</th>
                      <th className="num">Ushlab qolish</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.teachers.map((x) => (
                      <tr key={x.teacherId || 'none'}>
                        <td className="font-medium text-slate-700">
                          {x.teacherId ? (
                            <button type="button" className="hover:underline" onClick={() => setTeacherId(x.teacherId)}>
                              {x.teacherName}
                            </button>
                          ) : x.teacherName}
                        </td>
                        <td className="num">{x.activeBase || '—'}</td>
                        <td className="num font-semibold text-rose-600">{x.leftActive || '—'}</td>
                        <td className="num text-amber-700">{x.leftTrial || '—'}</td>
                        <td className="num">{pct(x.lossRate)}</td>
                        <td className="num">{x.lossRate == null ? '—' : pct(100 - x.lossRate)}</td>
                      </tr>
                    ))}
                    {data.teachers.length === 0 && (
                      <tr><td colSpan={6} className="px-4 py-12 text-center text-slate-400">Ma'lumot yo'q</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          )}

          {tab === 'reasons' && (
            <Card title="Sabablar" sub="Barcha sabablar — tanlangan tur bo'yicha" tight>
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Sabab</th>
                      <th className="num">Ketishlar</th>
                      <th className="num">Ulushi</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.reasons.map((r) => (
                      <tr key={r.label}>
                        <td className="text-slate-700">
                          {r.label}
                          {r.outOfControl && <Badge tone="default" className="ml-2">Nazoratdan tashqari</Badge>}
                        </td>
                        <td className="num font-semibold">{r.count}</td>
                        <td className="num">{t.left ? pct((r.count / t.left) * 100) : '—'}</td>
                      </tr>
                    ))}
                    {data.reasons.length === 0 && (
                      <tr><td colSpan={3} className="px-4 py-12 text-center text-slate-400">Tanlangan davrda ketish yo'q</td></tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </div>
      )}
    </div>
  )
}
