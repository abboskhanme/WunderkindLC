import type { ReactNode } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  LabelList,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import type { LeadFunnelStage } from '@/api/services/leads'
import { Card } from '@/components/ui/Card'
import { axisTick, barCursor, formatDwell, gridStroke, stageRamp } from './palette'

/**
 * KONVERSIYA VORONKASI — gorizontal bar.
 *
 * NEGA gorizontal bar (konus shaklidagi "funnel" EMAS): voronkaning eni trapetsiya
 * bo'lganda ko'z uzunlikni emas, YUZANI o'lchaydi va farq bo'rttirib ko'rsatiladi.
 * Gorizontal barda hamma bosqich bitta bazadan o'sadi — taqqoslash aniq, uzun bosqich
 * nomlari esa o'q bo'ylab bemalol joylashadi.
 *
 * RANG: bosqichlar ORDINAL (tartibini almashtirsang ma'no o'zgaradi), shuning uchun
 * bitta tusning ochiqdan quyuqqa rampi ishlatiladi — kategorial ranglar tarqatilmaydi.
 * Serverdan keladigan `stage.color` (kanban ustuni rangi) grafikda ATAYIN ishlatilmaydi:
 * u tekshiruvdan o'tmagan ixtiyoriy rang va palitrani buzadi.
 *
 * Bitta o'lchov — bitta seriya, shuning uchun legend kerak emas (sarlavha uni nomlaydi).
 */

interface FunnelRow extends LeadFunnelStage {
  /** O'rtacha turish vaqti matni; `null` — o'lchov yo'q (0 EMAS). */
  dwell: string | null
  /** Bar uchidagi yorliq — qiymat hover'siz ham o'qilsin. */
  tipLabel: string
}

/** Y o'qi yorlig'i: bosqich nomi + ostida bosqichda o'rtacha turish vaqti. */
interface TickProps {
  x: number | string
  y: number | string
  index: number
}

function StageTick(rows: FunnelRow[]) {
  return function tick({ x, y, index }: TickProps): ReactNode {
    const row = rows[index]
    if (!row) return null
    return (
      <g transform={`translate(${Number(x)},${Number(y)})`}>
        <text x={-8} y={-3} textAnchor="end" className="fill-slate-600 text-xs font-medium">
          {row.title}
        </text>
        <text x={-8} y={12} textAnchor="end" className="fill-slate-400 text-[11px]">
          {row.dwell ? `o'rtacha ${row.dwell}` : "vaqt — ma'lumot yetarli emas"}
        </text>
      </g>
    )
  }
}

interface TooltipProps {
  active?: boolean
  payload?: readonly { payload?: unknown }[]
}

function FunnelTooltip({ active, payload }: TooltipProps): ReactNode {
  if (!active || !payload?.length) return null
  const row = payload[0]?.payload as FunnelRow | undefined
  if (!row) return null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="font-semibold text-slate-800">{row.title}</p>
      <p className="mt-1 text-slate-700">
        <span className="font-mono font-semibold">{row.reached.toLocaleString()}</span> ta lid
        <span className="text-slate-400"> · {row.pct.toFixed(1)}%</span>
      </p>
      {row.dwell ? (
        <p className="mt-1 text-slate-500">
          O'rtacha turish: <span className="font-mono text-slate-700">{row.dwell}</span>
          <span className="text-slate-400"> ({row.samples} ta o'lchov asosida)</span>
        </p>
      ) : (
        <p className="mt-1 text-slate-400">
          O'rtacha turish vaqti — o'lchash uchun ma'lumot yetarli emas
        </p>
      )}
    </div>
  )
}

export function ConversionFunnel({ funnel }: { funnel: LeadFunnelStage[] }) {
  const rows: FunnelRow[] = [...funnel]
    .sort((a, b) => a.order - b.order)
    .map((s) => ({
      ...s,
      dwell: formatDwell(s.avgHours),
      tipLabel: `${s.reached.toLocaleString()} · ${s.pct.toFixed(0)}%`,
    }))

  const ramp = stageRamp(rows.length)
  // Nechta bosqichda vaqt umuman o'lchanmagani — halollik uchun izohda aytiladi.
  const missing = rows.filter((r) => r.avgHours == null).length

  return (
    <Card>
      <div className="mb-4">
        <h2 className="font-semibold text-slate-800">Konversiya voronkasi</h2>
        <p className="mt-0.5 text-xs text-slate-400">
          Har bosqichga yetib kelgan lidlar soni; bosqich nomi ostida — bosqichda o'rtacha
          turish vaqti
        </p>
      </div>

      {rows.length === 0 ? (
        <p className="py-12 text-center text-sm text-slate-400">
          Tanlangan davrda voronka uchun ma'lumot yo'q
        </p>
      ) : (
        <>
          <ResponsiveContainer width="100%" height={rows.length * 54 + 32}>
            <BarChart
              data={rows}
              layout="vertical"
              /* O'ngdagi bo'shliq — bar uchidagi yorliq ("1 284 · 100%") ikki qatorga
                 tushib ketmasligi uchun. */
              margin={{ top: 4, right: 104, left: 0, bottom: 0 }}
            >
              <CartesianGrid horizontal={false} stroke={gridStroke} />
              <XAxis
                type="number"
                tickLine={false}
                axisLine={false}
                tick={axisTick}
                allowDecimals={false}
              />
              <YAxis
                type="category"
                /* Kategoriya kaliti — bosqich nomi TAKRORLANIB qolsa ham qatorlar birlashib
                   ketmasin (yorliqni `StageTick` `rows[index]` dan chizadi). */
                dataKey="stageId"
                width={168}
                tickLine={false}
                axisLine={false}
                tick={StageTick(rows)}
              />
              <Tooltip cursor={barCursor} content={FunnelTooltip} />
              {/* "O'sib chiqish" animatsiyasi yo'q — dashboardda raqam darrov ko'rinsin. */}
              <Bar
                dataKey="reached"
                name="Lidlar"
                radius={[0, 4, 4, 0]}
                maxBarSize={24}
                isAnimationActive={false}
              >
                {rows.map((r, i) => (
                  <Cell key={r.stageId} fill={ramp[i]} />
                ))}
                {/* Bar uchidagi qiymat — o'qi bo'yicha o'qish shart bo'lmasin. */}
                <LabelList
                  dataKey="tipLabel"
                  position="right"
                  className="fill-slate-600 text-xs font-medium"
                />
              </Bar>
            </BarChart>
          </ResponsiveContainer>

          {missing > 0 && (
            <p className="mt-2 text-xs text-slate-400">
              {missing} ta bosqichda o'rtacha turish vaqti hisoblanmadi — bosqichlar tarixi
              yaqinda yozila boshlangan, eski lidlar bo'yicha o'lchov yo'q. Bu "0 soat" degani
              emas.
            </p>
          )}
        </>
      )}
    </Card>
  )
}
