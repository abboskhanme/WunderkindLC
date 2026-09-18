import { useMemo, useState } from 'react'
import {
  CartesianGrid,
  Cell,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { IconTrendingDown, IconTrendingUp } from '@tabler/icons-react'
import { getFinanceSummary, getTransactions } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { FilterInput } from '@/components/ui/list/FilterGrid'
import { useAsync } from '@/hooks/useAsync'
import { financeCategoryLabel, paymentMethodLabel } from '@/config/constants'
import { cn, formatMoney } from '@/lib/utils'

const COLORS = ['#00D66F', '#3D68FF', '#FBC400', '#DE4141', '#9747FF', '#019EF7', '#1D1D1D', '#BDBDBD']

function isoDay(d: Date): string {
  return d.toISOString().slice(0, 10)
}

/** Davrni kunlarga ajratib, oldingi (teng uzunlikdagi) davrni ham qaytaradi — foiz o'zgarish uchun. */
function previousPeriod(from: string, to: string): { from: string; to: string } {
  const a = new Date(from)
  const b = new Date(to)
  const days = Math.max(1, Math.round((b.getTime() - a.getTime()) / 86400000) + 1)
  const prevTo = new Date(a.getTime() - 86400000)
  const prevFrom = new Date(prevTo.getTime() - (days - 1) * 86400000)
  return { from: isoDay(prevFrom), to: isoDay(prevTo) }
}

/**
 * Moliya → «Moliya hisobotlari» (edutizim `/analytics/financial-reports`): uchta KPI kartochkasi
 * (Kirim · Chiqim · Qoldiq, oldingi davr bilan foiz farqi), kunlik grafik va tranzaksiya turlari
 * bo'yicha aylana + pastda ulush qatorlari.
 *
 * ⚠️ Yangi hisob YO'Q: `GET /admin/finance/summary` (davr xulosasi) va mavjud tranzaksiyalar
 * ro'yxati ishlatiladi (`.claude/rules/billing.md`).
 */
export function FinanceReportsPage() {
  const [from, setFrom] = useState(() => {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
  })
  const [to, setTo] = useState(() => isoDay(new Date()))
  const [side, setSide] = useState<'income' | 'expense'>('income')
  const [breakdown, setBreakdown] = useState<'category' | 'method'>('category')

  const summary = useAsync(() => getFinanceSummary(from, to), [from, to])
  const prev = previousPeriod(from, to)
  const before = useAsync(() => getFinanceSummary(prev.from, prev.to), [prev.from, prev.to])
  const tx = useAsync(() => getTransactions({ from, to }), [from, to])

  /** Kunlik kirim/chiqim — grafik uchun (tranzaksiyalardan). */
  const daily = useMemo(() => {
    const map = new Map<string, { date: string; income: number; expense: number }>()
    for (const t of tx.data ?? []) {
      const day = (t.date ?? '').slice(0, 10)
      if (!day) continue
      const row = map.get(day) ?? { date: day, income: 0, expense: 0 }
      if (t.direction === 'expense') row.expense += t.amount
      else row.income += t.amount
      map.set(day, row)
    }
    return [...map.values()].sort((a, b) => a.date.localeCompare(b.date))
  }, [tx.data])

  /** Aylana va ulush qatorlari — tanlangan yo'nalish + kesim (toifa yoki to'lov usuli). */
  const slices = useMemo(() => {
    const items = tx.data ?? []
    const map = new Map<string, number>()
    for (const t of items) {
      const isExpense = t.direction === 'expense'
      if ((side === 'expense') !== isExpense) continue
      const key = breakdown === 'category' ? (t.category ?? 'other') : (t.method ?? '')
      map.set(key, (map.get(key) ?? 0) + t.amount)
    }
    const rows = [...map.entries()].map(([key, amount]) => ({ key, amount })).sort((a, b) => b.amount - a.amount)
    const total = rows.reduce((s, r) => s + r.amount, 0)
    return { rows, total }
  }, [tx.data, side, breakdown])

  const label = (key: string) =>
    breakdown === 'category' ? financeCategoryLabel(key, side) : paymentMethodLabel(key)

  if (summary.loading && !summary.data) return <Loader className="min-h-[240px]" />
  if (summary.error) return <p className="text-[13px] text-red-600">{summary.error}</p>

  const s = summary.data
  const p = before.data
  const delta = (now: number, was: number) => (was === 0 ? (now === 0 ? 0 : 100) : ((now - was) / Math.abs(was)) * 100)

  const kpis = [
    { label: 'Kirim', value: s?.totalIncome ?? 0, change: delta(s?.totalIncome ?? 0, p?.totalIncome ?? 0), good: true, color: '#00D66F' },
    { label: 'Chiqim', value: s?.totalExpense ?? 0, change: delta(s?.totalExpense ?? 0, p?.totalExpense ?? 0), good: false, color: '#DE4141' },
    { label: 'Qoldiq', value: s?.net ?? 0, change: delta(s?.net ?? 0, p?.net ?? 0), good: true, color: '#FBC400' },
  ]

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">Moliya hisobotlari</h1>
        <div className="flex items-center gap-2">
          <FilterInput type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="w-[150px]" />
          <span className="text-[#6b7280]">—</span>
          <FilterInput type="date" value={to} onChange={(e) => setTo(e.target.value)} className="w-[150px]" />
        </div>
      </div>

      <div className="grid gap-2.5 sm:grid-cols-3">
        {kpis.map((k) => {
          const up = k.change >= 0
          const positive = k.good ? up : !up
          return (
            <div
              key={k.label}
              className="flex items-center justify-between rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]"
            >
              <div className="min-w-0">
                <p className="text-[13px] text-[#6b7280]">{k.label}</p>
                <p className="text-[20px] font-bold text-black">{formatMoney(k.value)}</p>
                <p className={cn('mt-0.5 flex items-center gap-1 text-[12px]', positive ? 'text-[#2e7d32]' : 'text-[#de4141]')}>
                  {up ? <IconTrendingUp className="h-4 w-4" /> : <IconTrendingDown className="h-4 w-4" />}
                  {Math.abs(k.change).toFixed(1)}%
                </p>
              </div>
              <span className="h-10 w-10 shrink-0 rounded-full border-4" style={{ borderColor: k.color }} />
            </div>
          )
        })}
      </div>

      <div className="grid gap-2.5 lg:grid-cols-[3fr_2fr]">
        <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          <p className="mb-2 text-[15px] font-semibold text-black">Grafik</p>
          <ResponsiveContainer width="100%" height={280}>
            <LineChart data={daily} margin={{ top: 5, right: 10, left: 0, bottom: 0 }}>
              <CartesianGrid stroke="#eef0f3" vertical={false} />
              <XAxis dataKey="date" tickFormatter={(d: string) => d.slice(8, 10) + '.' + d.slice(5, 7)} tick={{ fontSize: 11, fill: '#6b7280' }} />
              <YAxis tick={{ fontSize: 11, fill: '#6b7280' }} width={70} tickFormatter={(v: number) => (v / 1000).toFixed(0) + 'k'} />
              <Tooltip
                formatter={(v) => formatMoney(Number(v ?? 0))}
                labelFormatter={(d) => String(d)}
              />
              <Line type="monotone" dataKey="income" name="Kirim" stroke="#00D66F" strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="expense" name="Chiqim" stroke="#DE4141" strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        </div>

        <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
            <p className="text-[15px] font-semibold text-black">Tranzaksiya bo'yicha</p>
            <div className="tabs">
              {(['income', 'expense'] as const).map((d) => (
                <button key={d} type="button" onClick={() => setSide(d)} className={cn('tab', side === d && 'active')}>
                  {d === 'income' ? 'Kirim' : 'Chiqim'}
                </button>
              ))}
            </div>
          </div>
          <div className="relative">
            <ResponsiveContainer width="100%" height={240}>
              <PieChart>
                <Pie data={slices.rows} dataKey="amount" nameKey="key" innerRadius={70} outerRadius={110} stroke="none">
                  {slices.rows.map((r, i) => (
                    <Cell key={r.key} fill={COLORS[i % COLORS.length]} />
                  ))}
                </Pie>
              </PieChart>
            </ResponsiveContainer>
            <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
              <span className="text-[16px] font-semibold text-black">{formatMoney(slices.total)}</span>
              <span className="text-[12px] text-[#6b7280]">{slices.rows.length} ta turi</span>
            </div>
          </div>
        </div>
      </div>

      <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <p className="text-[15px] font-semibold text-black">{side === 'income' ? 'Kirim' : 'Chiqim'} — ulushlar</p>
          <div className="tabs">
            {(['category', 'method'] as const).map((b) => (
              <button key={b} type="button" onClick={() => setBreakdown(b)} className={cn('tab', breakdown === b && 'active')}>
                {b === 'category' ? 'Tranzaksiya turi' : "To'lov usuli"}
              </button>
            ))}
          </div>
        </div>
        {slices.rows.length === 0 ? (
          <p className="py-8 text-center text-[13px] text-[#9e9e9e]">Bu davrda yozuv yo'q</p>
        ) : (
          <div className="space-y-2">
            {slices.rows.map((r, i) => {
              const pct = slices.total === 0 ? 0 : (r.amount / slices.total) * 100
              return (
                <div key={r.key} className="flex items-center gap-3 text-[13px]">
                  <span className="w-[180px] shrink-0 truncate text-[#333]">{label(r.key)}</span>
                  <span className="relative h-6 flex-1 overflow-hidden rounded-md bg-[#f0f2f2]">
                    <span
                      className="absolute inset-y-0 left-0 rounded-md opacity-25"
                      style={{ width: `${pct}%`, backgroundColor: COLORS[i % COLORS.length] }}
                    />
                    <span className="absolute inset-0 flex items-center justify-center text-[12px] font-semibold text-[#333]">
                      {pct.toFixed(1)} %
                    </span>
                  </span>
                  <span className="w-[140px] shrink-0 text-right font-semibold text-black">{formatMoney(r.amount)}</span>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
