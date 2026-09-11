import type { ReactNode } from 'react'
import { Cell, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts'
import type { LeadSourceSlice } from '@/api/services/leads'
import { Card } from '@/components/ui/Card'
import { CATEGORICAL, MAX_SLICES, OTHER_GRAY } from './palette'

/**
 * LID MANBALARI — donut.
 *
 * NEGA donut: bu qism-butun (part-to-whole) savoli — "lidlarning qaysi ulushi qayerdan
 * keladi". Kesmalar soni 6 tadan oshmaydi, shuning uchun donut bir qarashda o'qiladi.
 * Aniq taqqoslash uchun esa har kesmaning soni va foizi yonidagi ro'yxatda MATN bilan
 * beriladi — rang yagona kanal bo'lib qolmaydi.
 *
 * RANG: kategorial palitra qat'iy tartibda. 6-chi va undan keyingi manbalar «Boshqa» ga
 * yig'iladi — yangi tus generatsiya QILINMAYDI.
 */

interface Slice extends LeadSourceSlice {
  color: string
}

interface TooltipProps {
  active?: boolean
  payload?: readonly { payload?: unknown }[]
}

function SourceTooltip({ active, payload }: TooltipProps): ReactNode {
  if (!active || !payload?.length) return null
  const s = payload[0]?.payload as Slice | undefined
  if (!s) return null
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-[var(--shadow-1)]">
      <p className="text-slate-700">
        <span className="font-mono font-semibold">{s.count.toLocaleString()}</span> ta lid
        <span className="text-slate-400"> · {s.pct.toFixed(1)}%</span>
      </p>
      <p className="mt-0.5 text-slate-500">{s.label}</p>
    </div>
  )
}

/**
 * Kesmalarni tayyorlaydi: eng ko'pi bo'yicha 5 tasi qoladi, qolgani «Boshqa».
 * Rang manba NOMIGA bog'lanadi (reytingiga emas) — davr filtri o'zgarganda omon qolgan
 * manbalar rangini almashtirib yubormasligi uchun.
 */
function buildSlices(sources: LeadSourceSlice[]): Slice[] {
  const sorted = [...sources].sort((a, b) => b.count - a.count)
  const top = sorted.slice(0, MAX_SLICES)
  const rest = sorted.slice(MAX_SLICES)
  const colorOrder = [...top].sort((a, b) => a.source.localeCompare(b.source))

  const slices: Slice[] = top.map((s) => ({
    ...s,
    color: CATEGORICAL[colorOrder.findIndex((c) => c.source === s.source)] ?? OTHER_GRAY,
  }))

  if (rest.length > 0) {
    slices.push({
      source: '__other__',
      label: `Boshqa (${rest.length} ta manba)`,
      count: rest.reduce((sum, s) => sum + s.count, 0),
      pct: rest.reduce((sum, s) => sum + s.pct, 0),
      color: OTHER_GRAY,
    })
  }
  return slices
}

export function SourcesDonut({ sources }: { sources: LeadSourceSlice[] }) {
  const slices = buildSlices(sources)
  const total = slices.reduce((sum, s) => sum + s.count, 0)

  return (
    <Card>
      <div className="mb-4">
        <h2 className="font-semibold text-slate-800">Lid manbalari</h2>
        <p className="mt-0.5 text-xs text-slate-400">Lidlar qaysi kanaldan kelgani — ulush bo'yicha</p>
      </div>

      {slices.length === 0 ? (
        <p className="py-12 text-center text-sm text-slate-400">
          Tanlangan davrda manba bo'yicha ma'lumot yo'q
        </p>
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
                  /* Kesmalar orasidagi ajratgich — fon rangidagi 2px bo'shliq (ramka EMAS). */
                  stroke="#ffffff"
                  strokeWidth={2}
                  isAnimationActive={false}
                >
                  {slices.map((s) => (
                    <Cell key={s.source} fill={s.color} />
                  ))}
                </Pie>
                <Tooltip content={SourceTooltip} />
              </PieChart>
            </ResponsiveContainer>
            {/* Donut markazi — jami. Matn har doim matn rangida. */}
            <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
              <span className="font-mono text-2xl font-semibold leading-none text-slate-800">
                {total.toLocaleString()}
              </span>
              <span className="mt-1 text-xs text-slate-400">jami lid</span>
            </div>
          </div>

          {/* Legend — ≥2 kesma bo'lgani uchun majburiy; qiymatlar shu yerda matn bilan ham bor. */}
          <ul className="w-full space-y-2 sm:w-1/2">
            {slices.map((s) => (
              <li key={s.source} className="flex items-center gap-2 text-sm">
                <span
                  className="h-2.5 w-2.5 shrink-0 rounded-sm"
                  style={{ backgroundColor: s.color }}
                  aria-hidden
                />
                <span className="min-w-0 flex-1 truncate text-slate-600">{s.label}</span>
                <span className="font-mono tabular-nums text-slate-800">
                  {s.count.toLocaleString()}
                </span>
                <span className="w-12 text-right font-mono tabular-nums text-xs text-slate-500">
                  {s.pct.toFixed(1)}%
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  )
}
