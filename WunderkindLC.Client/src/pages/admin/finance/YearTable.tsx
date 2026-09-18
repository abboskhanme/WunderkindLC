import type { ReactNode } from 'react'
import { IconFileExport } from '@tabler/icons-react'
import { cn, exportToCsv, formatMoney } from '@/lib/utils'

/** Oy nomlari — edutizim jadvalidagi ustun sarlavhalari. */
export const YEAR_MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
] as const

export interface YearRow {
  label: string
  months: number[]
  /** Jami/bo'lim qatori — qalin va rangli fon bilan. */
  tone?: 'income' | 'expense' | 'net' | 'section'
  /** Ichki (kategoriya) qatori — chapdan surilgan, oddiy shrift. */
  indent?: boolean
}

/**
 * "Kategoriya × oy" jadvali — edutizimdagi «Moliya hisobotlari (P&L)» va «Pul oqimi hisoboti»
 * sahifalari AYNAN shu ko'rinishda (`docs/edutizim/INVENTORY.md` §7): chap ustun yopishqoq,
 * oylar gorizontal aylanadi, jam qatorlari rangli fon bilan.
 */
export function YearTable({
  title,
  year,
  years,
  onYear,
  rows,
  actions,
}: {
  title: string
  year: number
  years: number[]
  onYear: (y: number) => void
  rows: YearRow[]
  actions?: ReactNode
}) {
  const toneCls = (t?: YearRow['tone']) =>
    t === 'income'
      ? 'bg-[#e8f8ee] font-bold'
      : t === 'expense'
        ? 'bg-[#fdeceb] font-bold'
        : t === 'net'
          ? 'bg-[#fff6e5] font-bold'
          : t === 'section'
            ? 'bg-white font-bold'
            : ''

  const onExport = () => {
    exportToCsv(
      `${title}-${year}.csv`,
      ['Kategoriya', ...YEAR_MONTHS],
      rows.map((r) => [r.label, ...r.months.map((m) => String(m))]),
    )
  }

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">{title}</h1>
        <div className="flex items-center gap-2">
          {actions}
          <select
            value={year}
            onChange={(e) => onYear(Number(e.target.value))}
            className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] text-black outline-none focus:border-brand-600"
            aria-label="Yil"
          >
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={onExport}
            className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-1 text-xs font-medium text-white transition-colors hover:bg-brand-700"
          >
            <IconFileExport className="h-5 w-5" />
            Eksport
          </button>
        </div>
      </div>

      <div className="overflow-auto rounded-xl border border-[#dbe0e6] bg-white">
        <table className="w-full border-collapse text-[13px] font-medium text-black">
          <thead>
            <tr>
              <th className="sticky left-0 z-10 h-12 min-w-[240px] border-b border-[#e0e0e0] bg-[#f5f6f7] px-4 text-left text-[12px] font-semibold uppercase">
                Kategoriya
              </th>
              {YEAR_MONTHS.map((m) => (
                <th
                  key={m}
                  className="h-12 min-w-[120px] border-b border-[#e0e0e0] bg-white px-3 text-right text-[12px] font-semibold uppercase"
                >
                  {m}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={`${r.label}-${i}`} className={toneCls(r.tone)}>
                <td
                  className={cn(
                    'sticky left-0 z-10 h-11 whitespace-nowrap border-b border-[#e0e0e0] px-4',
                    toneCls(r.tone) || 'bg-white',
                    r.indent && 'pl-9 font-normal text-[#555]',
                  )}
                >
                  {r.label}
                </td>
                {r.months.map((v, mi) => (
                  <td key={mi} className="h-11 whitespace-nowrap border-b border-[#e0e0e0] px-3 text-right">
                    {v === 0 ? '0' : formatMoney(v)}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

/** Oxirgi N yil (joriy yildan orqaga) — yil tanlash ro'yxati uchun. */
export function recentYears(count = 5): number[] {
  const now = new Date().getFullYear()
  return Array.from({ length: count }, (_, i) => now - i)
}
