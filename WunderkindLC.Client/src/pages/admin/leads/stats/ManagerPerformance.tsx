import {
  Bar,
  BarChart,
  CartesianGrid,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { LeadManagerRow } from '@/api/services/leads'
import { Card } from '@/components/ui/Card'
import { axisTick, barCursor, CATEGORICAL, gridStroke } from './palette'
import { cn, formatMoney } from '@/lib/utils'

/**
 * SOTUV BO'LIMI — menejerlar reytingi: kim nechta lid bilan ishladi, nechtasini o'quvchiga
 * aylantirdi va nechtasi haqiqatan PUL to'ladi.
 *
 * NEGA GORIZONTAL bar: menejerlar nominal ro'yxat va ismlar uzun. Vertikal ustunda ism
 * "Ali A." holiga qisqartirilardi — KPI jadvalida odamning ismi to'liq turishi kerak.
 *
 * Uchala seriya ham BIR XIL o'lchov birligida (lid soni), shuning uchun ular BITTA o'qda
 * turadi — ikki y-o'q (dual axis) hech qachon ishlatilmaydi. PUL boshqa birlik, shuning
 * uchun u grafikka QO'SHILMAYDI: pastdagi jadvalda va tooltipda beriladi.
 *
 * RANG: kategorial palitraning 1-, 2- va 3-sloti QAT'IY tartibda; menejerlar EMAS, SERIYALAR
 * ranglanadi (aks holda rang bar uzunligi allaqachon ko'rsatgan narsani takrorlagan bo'lardi).
 */

const CHART_LIMIT = 10
/** Bitta menejer qatorining balandligi (uchta bar + oraliq) — grafik shunga qarab cho'ziladi. */
const ROW_HEIGHT = 46

const SERIES = [
  { key: 'Lidlar', color: CATEGORICAL[0] },
  { key: 'Aylantirdi', color: CATEGORICAL[1] },
  { key: "To'ladi", color: CATEGORICAL[2] },
] as const

interface ChartRow {
  userId: string
  name: string
  moves: number
  revenue: number
  Lidlar: number
  Aylantirdi: number
  "To'ladi": number
}

interface TooltipProps {
  active?: boolean
  payload?: readonly { payload?: unknown }[]
}

function ManagerTooltip({ active, payload }: TooltipProps) {
  if (!active || !payload?.length) return null
  const r = payload[0]?.payload as ChartRow | undefined
  if (!r) return null
  const rate = r.Lidlar > 0 ? (r.Aylantirdi / r.Lidlar) * 100 : null
  const payRate = r.Lidlar > 0 ? (r["To'ladi"] / r.Lidlar) * 100 : null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="font-semibold text-slate-800">{r.name}</p>
      <p className="mt-1 text-slate-700">
        <span className="font-mono font-semibold">{r.Lidlar.toLocaleString()}</span> ta lid
      </p>
      <p className="text-slate-700">
        <span className="font-mono font-semibold">{r.Aylantirdi.toLocaleString()}</span> ta
        aylantirdi
        {rate != null && <span className="text-slate-400"> · {rate.toFixed(1)}%</span>}
      </p>
      <p className="text-slate-700">
        <span className="font-mono font-semibold">{r["To'ladi"].toLocaleString()}</span> ta to'ladi
        {payRate != null && <span className="text-slate-400"> · {payRate.toFixed(1)}%</span>}
      </p>
      {r.revenue > 0 && (
        <p className="mt-0.5 font-mono text-teal-700">{formatMoney(r.revenue)} so'm</p>
      )}
      <p className="mt-0.5 text-slate-400">{r.moves.toLocaleString()} ta harakat</p>
    </div>
  )
}

export function ManagerPerformance({ managers }: { managers: LeadManagerRow[] }) {
  // Server allaqachon tushum bo'yicha tartiblab beradi; grafik esa "kim ko'p ishladi" ni
  // ko'rsatgani uchun lidlar bo'yicha tartiblanadi (ikkalasi har xil savolga javob).
  const byLeads = [...managers].sort((a, b) => b.leads - a.leads)
  const chartRows: ChartRow[] = byLeads.slice(0, CHART_LIMIT).map((m) => ({
    userId: m.userId,
    name: m.name || m.userId,
    moves: m.moves,
    revenue: m.revenue,
    Lidlar: m.leads,
    Aylantirdi: m.won,
    "To'ladi": m.paid,
  }))
  const anyRevenue = managers.some((m) => m.revenue > 0)

  return (
    <Card
      title="Sotuv bo'limi — menejerlar"
      sub="Kim nechta lid bilan ishladi, nechtasini aylantirdi va nechtasi haqiqatan PUL to'ladi"
    >
      {byLeads.length === 0 ? (
        <div className="py-12 text-center">
          <p className="text-sm text-slate-500">Menejerlar bo'yicha ma'lumot yo'q</p>
          <p className="mx-auto mt-1 max-w-md text-xs text-slate-400">
            Bu xato emas: tanlangan davrda lidlar bo'yicha xodim harakati qayd etilmagan
            (yoki lidlar hech kimga biriktirilmagan). Davrni kengaytirib ko'ring.
          </p>
        </div>
      ) : (
        <>
          {/*
            Legend — uch seriya bo'lgani uchun MAJBURIY. Recharts `Legend` o'rniga oddiy HTML:
            tartibi barlar tartibiga aynan mos bo'lishi kafolatlansin (Recharts v3 da legend
            tartibini boshqarib bo'lmaydi). Matn — matn rangida, identity esa rangli kvadratdan.
          */}
          <ul className="mb-3 flex flex-wrap items-center gap-x-5 gap-y-1 text-[13px]">
            {SERIES.map((s) => (
              <li key={s.key} className="flex items-center gap-2 text-slate-600">
                <span
                  className="h-2.5 w-2.5 rounded-sm"
                  style={{ backgroundColor: s.color }}
                  aria-hidden
                />
                {s.key}
              </li>
            ))}
          </ul>

          <ResponsiveContainer width="100%" height={Math.max(160, chartRows.length * ROW_HEIGHT)}>
            <BarChart
              layout="vertical"
              data={chartRows}
              margin={{ top: 4, right: 16, left: 0, bottom: 0 }}
              barGap={2}
              barCategoryGap="30%"
            >
              <CartesianGrid horizontal={false} stroke={gridStroke} />
              <XAxis type="number" tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
              <YAxis
                type="category"
                dataKey="name"
                tickLine={false}
                axisLine={false}
                tick={axisTick}
                width={140}
                interval={0}
              />
              <Tooltip cursor={barCursor} content={ManagerTooltip} />
              {/* "O'sib chiqish" animatsiyasi yo'q — dashboardda raqam darrov ko'rinsin. */}
              {SERIES.map((s) => (
                <Bar
                  key={s.key}
                  dataKey={s.key}
                  fill={s.color}
                  radius={[0, 4, 4, 0]}
                  maxBarSize={12}
                  isAnimationActive={false}
                />
              ))}
            </BarChart>
          </ResponsiveContainer>

          {byLeads.length > chartRows.length && (
            <p className="mt-1 text-xs text-slate-400">
              Diagrammada eng faol {chartRows.length} ta menejer — qolgani jadvalda.
            </p>
          )}

          {/* Jadval — grafikning "table view" juftligi: har bir qiymat hover'siz ham o'qiladi.
              Tartib SERVERDAN keladi: tushum → aylantirgan → harakat (natija faollikdan ustun). */}
          <div className="mt-4 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="py-2 pr-3 font-medium">Menejer</th>
                  <th className="py-2 pr-3 text-right font-medium">Kiritdi</th>
                  <th className="py-2 pr-3 text-right font-medium">Harakat</th>
                  <th className="py-2 pr-3 text-right font-medium">Lidlar</th>
                  <th className="py-2 pr-3 text-right font-medium">Aylantirdi</th>
                  <th className="py-2 pr-3 text-right font-medium">Konversiya</th>
                  <th className="py-2 pr-3 text-right font-medium">To'ladi</th>
                  <th className="py-2 pr-3 text-right font-medium">Sotuv %</th>
                  <th className="py-2 pr-3 text-right font-medium">Tushum</th>
                  <th className="py-2 text-right font-medium">O'rtacha chek</th>
                </tr>
              </thead>
              <tbody>
                {managers.map((m) => {
                  const rate = m.leads > 0 ? (m.won / m.leads) * 100 : null
                  const payRate = m.leads > 0 ? (m.paid / m.leads) * 100 : null
                  const avg = m.paid > 0 ? m.revenue / m.paid : 0
                  return (
                    <tr key={m.userId} className="border-b border-slate-50 last:border-0">
                      <td className="py-2 pr-3 font-medium text-slate-700">{m.name || m.userId}</td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-500">
                        {m.created.toLocaleString()}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-500">
                        {m.moves.toLocaleString()}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-700">
                        {m.leads.toLocaleString()}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-emerald-600">
                        {m.won.toLocaleString()}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-500">
                        {rate != null ? `${rate.toFixed(1)}%` : '—'}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-teal-700">
                        {m.paid.toLocaleString()}
                      </td>
                      <td
                        className={cn(
                          'py-2 pr-3 text-right font-mono tabular-nums font-semibold',
                          payRate != null && payRate >= 20 ? 'text-teal-700' : 'text-slate-500',
                        )}
                      >
                        {payRate != null ? `${payRate.toFixed(1)}%` : '—'}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-teal-700">
                        {m.revenue > 0 ? formatMoney(m.revenue) : <span className="text-slate-300">—</span>}
                      </td>
                      <td className="py-2 text-right font-mono tabular-nums text-slate-500">
                        {avg > 0 ? formatMoney(avg) : <span className="text-slate-300">—</span>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <p className="mt-3 text-xs text-slate-400">
            «Aylantirdi», «To'ladi» va «Tushum» — lidni O'QUVCHIGA AYLANTIRGAN xodimga yoziladi,
            shuning uchun bir lidning puli ikki menejerga qo'shilmaydi. Kim qaysi bosqichda
            yordam bergani quyidagi jadvalda ko'rinadi.
            {!anyRevenue && ' Tushum ustuni bo\'sh: aylantirilgan lidlardan hali to\'lov kelmagan.'}
          </p>
        </>
      )}
    </Card>
  )
}
