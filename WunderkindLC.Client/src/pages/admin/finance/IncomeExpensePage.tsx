import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Cell, Pie, PieChart, ResponsiveContainer } from 'recharts'
import { getFinanceSummary } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { FilterInput } from '@/components/ui/list/FilterGrid'
import { useAsync } from '@/hooks/useAsync'
import { financeCategoryLabel } from '@/config/constants'
import { formatMoney, cn } from '@/lib/utils'

/** Diagramma ranglari — edutizimdagidek yorqin, ketma-ket takrorlanadi. */
const COLORS = ['#00D66F', '#3D68FF', '#FBC400', '#DE4141', '#9747FF', '#019EF7', '#1D1D1D', '#BDBDBD']

/** Oyning birinchi kuni "yyyy-MM-dd" (standart davr — joriy oy). */
function monthStart(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}

function today(): string {
  return new Date().toISOString().slice(0, 10)
}

/**
 * Moliya → «Kirim chiqim» (edutizim `/analytics/finance`): chapda TOIFALAR aylanasi (o'rtasida
 * jami), o'ngda "Turlari / Summa" jadvali. Tepada "Kirim | Chiqim" tugmalari va davr.
 *
 * ⚠️ Ma'lumot MAVJUD endpointdan (`GET /admin/finance/summary`) — bu KO'RINISH qatlami, yangi
 * hisob-kitob emas (`.claude/rules/billing.md`).
 */
export function IncomeExpensePage() {
  const [params, setParams] = useSearchParams()
  const type = params.get('type') === 'expense' ? 'expense' : 'income'
  const from = params.get('from') || monthStart()
  const to = params.get('to') || today()
  const set = (patch: Record<string, string>) => {
    const next = new URLSearchParams(params)
    for (const [k, v] of Object.entries(patch)) next.set(k, v)
    setParams(next, { replace: true })
  }

  const { data, loading, error } = useAsync(() => getFinanceSummary(from, to), [from, to])
  const [active, setActive] = useState<string | null>(null)

  const rows = useMemo(() => {
    const items = (type === 'income' ? data?.incomeByCategory : data?.expenseByCategory) ?? []
    return [...items].sort((a, b) => b.amount - a.amount)
  }, [data, type])
  const total = rows.reduce((s, r) => s + r.amount, 0)

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="tabs" role="tablist">
          {(['income', 'expense'] as const).map((t) => (
            <button
              key={t}
              type="button"
              role="tab"
              aria-selected={type === t}
              onClick={() => set({ type: t })}
              className={cn('tab', type === t && 'active')}
            >
              {t === 'income' ? 'Kirim' : 'Chiqim'}
            </button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          <FilterInput type="date" value={from} onChange={(e) => set({ from: e.target.value })} className="w-[150px]" />
          <span className="text-[#6b7280]">—</span>
          <FilterInput type="date" value={to} onChange={(e) => set({ to: e.target.value })} className="w-[150px]" />
        </div>
      </div>

      <div className="grid gap-2.5 lg:grid-cols-2">
        <div className="relative rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          {rows.length === 0 ? (
            <p className="py-24 text-center text-[13px] text-[#9e9e9e]">Bu davrda yozuv yo'q</p>
          ) : (
            <>
              <ResponsiveContainer width="100%" height={320}>
                <PieChart>
                  <Pie
                    data={rows}
                    dataKey="amount"
                    nameKey="category"
                    innerRadius={90}
                    outerRadius={140}
                    paddingAngle={1}
                    stroke="none"
                    onMouseEnter={(_, i) => setActive(rows[i]?.category ?? null)}
                    onMouseLeave={() => setActive(null)}
                  >
                    {rows.map((r, i) => (
                      <Cell
                        key={r.category}
                        fill={COLORS[i % COLORS.length]}
                        opacity={active && active !== r.category ? 0.45 : 1}
                      />
                    ))}
                  </Pie>
                </PieChart>
              </ResponsiveContainer>
              {/* Aylana o'rtasidagi JAMI — edutizimdagidek */}
              <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
                <span className="text-[18px] font-semibold text-black">{formatMoney(total)}</span>
                <span className="text-[12px] text-[#6b7280]">
                  {type === 'income' ? 'Jami kirim' : 'Jami chiqim'}
                </span>
              </div>
            </>
          )}
        </div>

        <div className="overflow-hidden rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          <table className="w-full border-collapse text-[13px] font-medium">
            <thead>
              <tr>
                <th className="h-14 border-b border-[#e0e0e0] px-4 text-left text-[12px] font-semibold uppercase text-black">
                  Turlari
                </th>
                <th className="h-14 border-b border-[#e0e0e0] px-4 text-right text-[12px] font-semibold uppercase text-black">
                  Summa
                </th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r, i) => (
                <tr
                  key={r.category}
                  onMouseEnter={() => setActive(r.category)}
                  onMouseLeave={() => setActive(null)}
                  className="transition-colors hover:bg-black/[0.04]"
                >
                  <td
                    className="h-[52px] border-b border-[#e0e0e0] px-4"
                    style={{ color: COLORS[i % COLORS.length] }}
                  >
                    {financeCategoryLabel(r.category, type)}
                  </td>
                  <td
                    className="h-[52px] border-b border-[#e0e0e0] px-4 text-right"
                    style={{ color: COLORS[i % COLORS.length] }}
                  >
                    {formatMoney(r.amount)}
                  </td>
                </tr>
              ))}
              <tr>
                <td className="h-[52px] px-4 font-bold text-black">Jami</td>
                <td className="h-[52px] px-4 text-right font-bold text-black">{formatMoney(total)}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}
