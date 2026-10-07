import { useCallback, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { Users, UserX, UserCheck } from 'lucide-react'
import {
  getLeadLossReport,
  type LeadLossGroup,
  type LeadLossItem,
  type LeadLossMonth,
  type LeadLossReason,
} from '@/api/services/leadLoss'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import { formatNumber } from '@/lib/numberFormat'
import { monthShortNames } from '@/config/constants'
import { todayIso } from '@/lib/month'
import { axisTick, barCursor, CATEGORICAL, gridStroke, MAX_SLICES, OTHER_GRAY } from './stats/palette'

/**
 * «YO'QOTILGAN LIDLAR» hisoboti (`GET /api/admin/leads/lost-report`, ruxsat `leads.stats`).
 * Qoidalar: `.claude/rules/crm-leads.md` «YO'QOTILGAN LIDLAR».
 *
 * DAVR — lid KELGAN sana bo'yicha (CRM statistikasidagi bilan bir xil): «shu davrda kelgan
 * lidlarning qanchasini va nega yo'qotdik». Barcha foizlar shu to'plamdan — 100% dan oshmaydi.
 *
 * RANGLAR (dataviz validatoridan o'tgan, yangi tus O'YLAB TOPILMAGAN):
 * - "Jami lid" `#0284c7` + "Yo'qotilgan" `#e11d48` — kurs analitikasidagi kelgan/ketgan juftligi
 *   (deuteranopiyada ΔE 20.9). Yashil/qizil ATAYIN olinmadi (`course-analytics.md` §6).
 * - Sabablar donut'i — CRM palitrasi (`stats/palette.ts`), 6-chidan boshlab «Boshqa» kulrangi.
 * - Ikki o'lchov bitta grafikda IKKI Y-O'Q bilan ko'rsatilmaydi: foiz — tooltip va jadvalda.
 */

const TOTAL_COLOR = '#0284c7'
const LOST_COLOR = '#e11d48'
/** Kesim grafiklarida nechta qator chiziladi (qolgani jadvalda). */
const TOP_BARS = 10

const monthLabel = (m: string) =>
  `${monthShortNames[Number(m.slice(5, 7)) - 1] ?? m} '${m.slice(2, 4)}`

/** "yyyy-MM-dd" — bugundan `days` kun orqaga (bugun ham kiradi). Sana LOKAL (`todayIso` bilan bir xil). */
function daysBack(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() - days + 1)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

const QUICK = [
  { label: '30 kun', days: 30 },
  { label: '90 kun', days: 90 },
  { label: '12 oy', days: 365 },
]

export function LostLeadsReportPage() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  const fetchReport = useCallback(
    () =>
      getLeadLossReport(from || undefined, to || undefined).catch((err: unknown) => {
        throw new Error(apiErrorMessage(err, "Hisobotni yuklab bo'lmadi"))
      }),
    [from, to],
  )
  const { data, loading, error } = useAsync(fetchReport, [fetchReport])

  const isQuick = (days: number) => to === todayIso() && from === daysBack(days)

  return (
    <div className="space-y-6">
      <PageHeader
        title="Yo'qotilgan lidlar"
        sub="Qaysi lidlarni yo'qotdik va nega — sabab, manba va mas'ul xodim kesimida"
      />

      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <Input label="Sanadan" type="date" className="w-auto" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input label="Sanagacha" type="date" className="w-auto" value={to} onChange={(e) => setTo(e.target.value)} />
          <div className="tabs">
            {QUICK.map((q) => (
              <button
                key={q.days}
                type="button"
                className={cn('tab', isQuick(q.days) && 'active')}
                onClick={() => {
                  setFrom(daysBack(q.days))
                  setTo(todayIso())
                }}
              >
                {q.label}
              </button>
            ))}
            <button
              type="button"
              className={cn('tab', !from && !to && 'active')}
              onClick={() => {
                setFrom('')
                setTo('')
              }}
            >
              Butun davr
            </button>
          </div>
          <p className="basis-full text-xs text-slate-400">
            Davr — lid <b>kelgan</b> sana bo'yicha: shu davrda kelgan lidlarning hozir nechtasi yo'qotilgan.
          </p>
        </div>
      </Card>

      {error ? (
        <p className="rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      ) : loading && !data ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        data && (
          <div className={cn('space-y-6', loading && 'opacity-60 transition-opacity')}>
            {/* KPI: yo'qotish ULUSHI — sahifaning asosiy raqami (yagona "hero"). */}
            <div className="grid gap-4 lg:grid-cols-3">
              <div className="flex flex-col justify-between gap-4 rounded-xl border border-slate-200 bg-white p-5 shadow-[var(--shadow-1)]">
                <span className="text-xs font-semibold uppercase tracking-wide text-slate-400">Yo'qotish ulushi</span>
                <div>
                  <div className="text-[52px] font-semibold leading-none tracking-tight text-slate-900">
                    {data.lostRate.toFixed(1)}
                    <span className="ml-1 text-2xl font-medium text-slate-400">%</span>
                  </div>
                  <p className="mt-2 text-xs text-slate-500">
                    {formatNumber(data.total)} ta liddan {formatNumber(data.lost)} tasi yo'qotilgan
                  </p>
                </div>
              </div>
              <div className="grid gap-4 sm:grid-cols-3 lg:col-span-2 lg:grid-cols-3">
                <StatCard label="Kelgan lidlar" value={formatNumber(data.total)} icon={Users} />
                <StatCard
                  label="Yo'qotilgan"
                  value={formatNumber(data.lost)}
                  icon={UserX}
                  iconBg="bg-rose-50"
                  iconColor="text-rose-600"
                />
                <StatCard
                  label="O'quvchiga aylandi"
                  value={formatNumber(data.converted)}
                  icon={UserCheck}
                  iconBg="bg-sky-50"
                  iconColor="text-sky-700"
                />
              </div>
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
              <ReasonsDonut reasons={data.byReason} lost={data.lost} />
              <MonthlyBars months={data.monthly} />
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
              <GroupBars
                title="Manba bo'yicha"
                sub="Qaysi kanaldan kelgan lidlar ko'proq yo'qotiladi"
                firstCol="Manba"
                rows={data.bySource}
              />
              <GroupBars
                title="Mas'ul xodim bo'yicha"
                sub="Lidni ishlagan (mas'ul) xodim kesimida"
                firstCol="Xodim"
                rows={data.byManager}
              />
            </div>

            <RecentTable data={data.recent} truncated={data.recentTruncated} />
          </div>
        )
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ sabablar */

interface Slice extends LeadLossReason {
  color: string
}

/**
 * SABABLAR — donut (qism-butun savoli: "yo'qotilganlarning qaysi ulushi qaysi sababdan").
 * Rang sabab NOMIGA bog'lanadi (reytingiga emas) — davr o'zgarganda omon qolgan sabablar rangini
 * almashtirmaydi. Qiymatlar yonidagi ro'yxatda MATN bilan ham bor — rang yagona kanal emas.
 */
function buildSlices(reasons: LeadLossReason[]): Slice[] {
  const top = reasons.slice(0, MAX_SLICES)
  const rest = reasons.slice(MAX_SLICES)
  const colorOrder = [...top].sort((a, b) => a.label.localeCompare(b.label))
  const slices: Slice[] = top.map((r) => ({
    ...r,
    color: CATEGORICAL[colorOrder.findIndex((c) => c.label === r.label)] ?? OTHER_GRAY,
  }))
  if (rest.length > 0)
    slices.push({
      label: `Boshqa (${rest.length} ta sabab)`,
      count: rest.reduce((s, r) => s + r.count, 0),
      share: Math.round(rest.reduce((s, r) => s + r.share, 0) * 10) / 10,
      color: OTHER_GRAY,
    })
  return slices
}

function ReasonsDonut({ reasons, lost }: { reasons: LeadLossReason[]; lost: number }) {
  const slices = buildSlices(reasons)
  return (
    <Card title="Yo'qotish sabablari" sub="Yo'qotilgan lidlar qaysi sabab bilan ketdi — ulush bo'yicha">
      {slices.length === 0 ? (
        <Empty>Tanlangan davrda yo'qotilgan lid yo'q</Empty>
      ) : (
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
                    <Cell key={s.label} fill={s.color} />
                  ))}
                </Pie>
                <Tooltip content={ReasonTooltip} />
              </PieChart>
            </ResponsiveContainer>
            <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
              <span className="font-mono text-2xl font-semibold leading-none text-slate-800">{formatNumber(lost)}</span>
              <span className="mt-1 text-xs text-slate-400">yo'qotilgan</span>
            </div>
          </div>
          <ul className="w-full space-y-2 sm:w-1/2">
            {slices.map((s) => (
              <li key={s.label} className="flex items-center gap-2 text-sm">
                <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ backgroundColor: s.color }} aria-hidden />
                <span className="min-w-0 flex-1 truncate text-slate-600" title={s.label}>
                  {s.label}
                </span>
                <span className="font-mono tabular-nums text-slate-800">{formatNumber(s.count)}</span>
                <span className="w-12 text-right font-mono text-xs tabular-nums text-slate-500">{s.share.toFixed(1)}%</span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  )
}

interface TipProps {
  active?: boolean
  payload?: readonly { payload?: unknown }[]
}

function ReasonTooltip({ active, payload }: TipProps): ReactNode {
  const s = active ? (payload?.[0]?.payload as Slice | undefined) : undefined
  if (!s) return null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="text-slate-700">
        <span className="font-mono font-semibold">{formatNumber(s.count)}</span> ta lid
        <span className="text-slate-400"> · {s.share.toFixed(1)}%</span>
      </p>
      <p className="mt-0.5 text-slate-500">{s.label}</p>
    </div>
  )
}

/* ------------------------------------------------------------------ oylik oqim */

interface CountRow {
  name: string
  total: number
  lost: number
  rate: number
}

/** Ikki seriya tooltip'i + yo'qotish ULUSHI (foiz alohida o'q bilan chizilmaydi — faqat matn). */
function CountTooltip({ active, payload }: TipProps): ReactNode {
  const r = active ? (payload?.[0]?.payload as CountRow | undefined) : undefined
  if (!r) return null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="mb-1 font-medium text-slate-700">{r.name}</p>
      <p className="flex items-center gap-1.5 text-slate-600">
        <span className="h-2 w-2 rounded-sm" style={{ background: TOTAL_COLOR }} aria-hidden /> Kelgan:{' '}
        <b className="font-mono">{formatNumber(r.total)}</b>
      </p>
      <p className="flex items-center gap-1.5 text-slate-600">
        <span className="h-2 w-2 rounded-sm" style={{ background: LOST_COLOR }} aria-hidden /> Yo'qotilgan:{' '}
        <b className="font-mono">{formatNumber(r.lost)}</b>
        <span className="text-slate-400">· {r.rate.toFixed(1)}%</span>
      </p>
    </div>
  )
}

function MonthlyBars({ months }: { months: LeadLossMonth[] }) {
  const rows: CountRow[] = months.map((m) => ({ name: monthLabel(m.month), total: m.total, lost: m.lost, rate: m.rate }))
  return (
    <Card title="Oylik oqim" sub="Har oyda kelgan lidlar va ulardan hozir yo'qotilganlari">
      {rows.length === 0 ? (
        <Empty>Ma'lumot yo'q</Empty>
      ) : (
        <ResponsiveContainer width="100%" height={280}>
          <BarChart data={rows} margin={{ top: 10, right: 10, left: 0, bottom: 0 }} barGap={2}>
            <CartesianGrid vertical={false} stroke={gridStroke} />
            <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
            <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
            <Tooltip cursor={barCursor} content={CountTooltip} />
            <Legend wrapperStyle={{ fontSize: 13 }} />
            <Bar dataKey="total" name="Kelgan" fill={TOTAL_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
            <Bar dataKey="lost" name="Yo'qotilgan" fill={LOST_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
          </BarChart>
        </ResponsiveContainer>
      )}
    </Card>
  )
}

/* ------------------------------------------------------------------ manba / xodim */

function GroupBars({
  title,
  sub,
  firstCol,
  rows,
}: {
  title: string
  sub: string
  firstCol: string
  rows: LeadLossGroup[]
}) {
  const chart: CountRow[] = rows
    .slice(0, TOP_BARS)
    .map((r) => ({ name: r.label, total: r.total, lost: r.lost, rate: r.rate }))
  return (
    <Card title={title} sub={sub}>
      {rows.length === 0 ? (
        <Empty>Ma'lumot yo'q</Empty>
      ) : (
        <>
          <ResponsiveContainer width="100%" height={Math.max(180, chart.length * 44 + 40)}>
            <BarChart data={chart} layout="vertical" margin={{ top: 4, right: 24, left: 0, bottom: 0 }} barGap={2}>
              <CartesianGrid horizontal={false} stroke={gridStroke} />
              <XAxis type="number" tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
              <YAxis type="category" dataKey="name" width={140} tickLine={false} axisLine={false} tick={axisTick} />
              <Tooltip cursor={barCursor} content={CountTooltip} />
              <Legend wrapperStyle={{ fontSize: 13 }} />
              <Bar dataKey="total" name="Kelgan" fill={TOTAL_COLOR} radius={[0, 4, 4, 0]} maxBarSize={12} />
              <Bar dataKey="lost" name="Yo'qotilgan" fill={LOST_COLOR} radius={[0, 4, 4, 0]} maxBarSize={12} />
            </BarChart>
          </ResponsiveContainer>
          {rows.length > chart.length && (
            <p className="mt-1 text-xs text-slate-400">Grafikda eng ko'p {chart.length} tasi — qolgani jadvalda.</p>
          )}
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="py-2 pr-3 font-medium">{firstCol}</th>
                  <th className="py-2 pr-3 text-right font-medium">Kelgan</th>
                  <th className="py-2 pr-3 text-right font-medium">Yo'qotilgan</th>
                  <th className="py-2 text-right font-medium">Ulush</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.key || r.label} className="border-b border-slate-50 last:border-0">
                    <td className="py-2 pr-3 text-slate-700">{r.label}</td>
                    <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-700">{formatNumber(r.total)}</td>
                    <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-700">{formatNumber(r.lost)}</td>
                    <td className="py-2 text-right font-mono tabular-nums text-slate-500">{r.rate.toFixed(1)}%</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </Card>
  )
}

/* ------------------------------------------------------------------ ro'yxat */

function RecentTable({ data, truncated }: { data: LeadLossItem[]; truncated: number }) {
  return (
    <Card title="Yo'qotilgan lidlar ro'yxati" sub="Eng so'nggi yo'qotilganlari tepada — ism bosilsa lid sahifasi ochiladi" tight>
      {data.length === 0 ? (
        <Empty>Tanlangan davrda yo'qotilgan lid yo'q</Empty>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-left text-sm">
            <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-2 font-medium">Lid</th>
                <th className="px-3 py-2 font-medium">Yo'qotilgan</th>
                <th className="px-3 py-2 font-medium">Sabab</th>
                <th className="px-3 py-2 font-medium">Izoh</th>
                <th className="px-3 py-2 font-medium">Manba</th>
                <th className="px-3 py-2 font-medium">Mas'ul</th>
              </tr>
            </thead>
            <tbody>
              {data.map((r) => (
                <tr key={r.id} className="border-b border-slate-50 last:border-0">
                  <td className="px-4 py-2">
                    <Link to={`/admin/leads/${r.id}`} className="font-medium text-slate-800 hover:text-brand-600 hover:underline">
                      {r.fullName || '—'}
                    </Link>
                    <span className="block text-xs text-slate-400">kelgan: {formatDateTime(r.createdAt)}</span>
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 text-slate-600">
                    {formatDateTime(r.lostAt)}
                    {r.lostBy && <span className="block text-xs text-slate-400">{r.lostBy}</span>}
                  </td>
                  <td className="px-3 py-2 text-slate-700">{r.reason}</td>
                  <td className="max-w-[260px] px-3 py-2 text-slate-500">
                    <span className="line-clamp-2" title={r.comment || undefined}>
                      {r.comment || '—'}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-slate-600">{r.source}</td>
                  <td className="px-3 py-2 text-slate-600">{r.manager}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {truncated > 0 && (
            <p className="px-4 py-2 text-xs text-slate-400">
              Yana {formatNumber(truncated)} ta yo'qotilgan lid ro'yxatga sig'madi — davrni toraytiring.
            </p>
          )}
        </div>
      )}
    </Card>
  )
}

function Empty({ children }: { children: ReactNode }) {
  return <p className="py-12 text-center text-sm text-slate-400">{children}</p>
}
