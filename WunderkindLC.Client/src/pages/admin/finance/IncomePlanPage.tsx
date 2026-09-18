import { useState } from 'react'
import { getIncomePlan } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { FilterInput } from '@/components/ui/list/FilterGrid'
import { useAsync } from '@/hooks/useAsync'
import { formatMoney } from '@/lib/utils'

/** "yyyy-MM-dd" → "yyyy-MM" (oy kaliti). */
function monthOf(day: string): string {
  return day.slice(0, 7)
}

/**
 * Moliya → «Tushum rejasi» (edutizim `/analytics/income-plan`): oyda qancha pul kutilayotgani —
 * reja, eski oydan ko'chgan qarz/avans, shu oyda to'langan va qolgan kutilayotgan tushum.
 *
 * Hisob SERVERDA (`IncomePlan`), reja `SubscriptionRisk` bilan bitta manbadan — ikki sahifa
 * ikki xil son ko'rsatmasin.
 */
export function IncomePlanPage() {
  const [day, setDay] = useState(() => new Date().toISOString().slice(0, 10))
  const { data, loading, error } = useAsync(() => getIncomePlan(monthOf(day)), [day])

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  const month = data?.month ?? monthOf(day)
  const period = `01.${month.slice(5)}.${month.slice(0, 4)} - 01.${String(Number(month.slice(5)) + 1).padStart(2, '0')}.${month.slice(0, 4)}`

  return (
    <div className="space-y-2.5">
      <div>
        <div className="w-[170px]">
          <FilterInput type="date" value={day} onChange={(e) => setDay(e.target.value)} />
        </div>
        <p className="mt-1 text-[12px] text-[#6b7280]">{period}</p>
      </div>

      <div className="overflow-hidden rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
        <table className="w-full border-collapse text-[13px] font-medium text-black">
          <thead>
            <tr>
              <th className="h-14 border-b border-[#e0e0e0] px-4 text-left text-[12px] font-semibold uppercase">
                Tushum rejasi
              </th>
              <th className="h-14 border-b border-[#e0e0e0] px-4 text-left text-[12px] font-semibold uppercase">
                O'quvchi soni
              </th>
              <th className="h-14 border-b border-[#e0e0e0] px-4 text-left text-[12px] font-semibold uppercase">
                Umumiy kutilayotgan summa
              </th>
            </tr>
          </thead>
          <tbody>
            {(data?.rows ?? []).map((r) => (
              <tr key={r.label} className="transition-colors hover:bg-black/[0.04]">
                <td className="h-[52px] border-b border-[#e0e0e0] px-4">{r.label}</td>
                <td className="h-[52px] border-b border-[#e0e0e0] px-4 text-[#333]">
                  {r.students ?? ''}
                </td>
                <td className="h-[52px] border-b border-[#e0e0e0] px-4">{formatMoney(r.amount)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
