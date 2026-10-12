import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { monthLabel, shiftMonth } from '@/lib/ratingPeriods'
import { TEST_KINDS } from '@/lib/testScoreGrid'

/**
 * Testlar ro'yxati filtri (CRM U4a): «Oy» (strelkalar bilan; standart — joriy oy) yoki «Hammasi» + tur chiplari
 * («Hammasi» · «Oylik» · «Oraliq»). `month = ''` — barcha oylar, `kind = ''` — barcha turlar.
 * Admin ham, o'qituvchi ham shu komponentni ishlatadi.
 */
export function TestMonthFilter({
  month,
  kind,
  onMonth,
  onKind,
  currentMonth,
  showKind = true,
}: {
  month: string
  kind?: string
  onMonth: (m: string) => void
  onKind?: (k: string) => void
  /** Joriy oy ("yyyy-MM") — «Hammasi» dan qaytishda va kelajakka o'tmaslik uchun. */
  currentMonth: string
  showKind?: boolean
}) {
  const chip = (active: boolean) =>
    cn(
      'rounded-full border px-2.5 py-1 text-[12px] font-semibold transition-colors',
      active ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-line bg-white text-mute hover:text-ink',
    )
  const all = month === ''
  return (
    <div className="flex flex-wrap items-center gap-2">
      <div className="inline-flex items-center rounded-lg border border-line bg-white">
        <button
          type="button"
          onClick={() => onMonth(shiftMonth(all ? currentMonth : month, all ? 0 : -1))}
          className="flex h-8 w-8 items-center justify-center text-mute hover:text-ink"
          aria-label="Oldingi oy"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[112px] px-1 text-center text-[13px] font-semibold text-ink">
          {all ? 'Barcha oylar' : monthLabel(month)}
        </span>
        <button
          type="button"
          onClick={() => onMonth(all ? currentMonth : shiftMonth(month, 1))}
          disabled={!all && month >= currentMonth}
          className="flex h-8 w-8 items-center justify-center text-mute hover:text-ink disabled:opacity-30"
          aria-label="Keyingi oy"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>
      <button type="button" className={chip(all)} onClick={() => onMonth(all ? currentMonth : '')}>
        Hammasi
      </button>
      {showKind && onKind && (
        <div className="flex flex-wrap items-center gap-1.5">
          <span className="text-[12px] text-faint">Turi:</span>
          <button type="button" className={chip(!kind)} onClick={() => onKind('')}>
            Hammasi
          </button>
          {TEST_KINDS.map((k) => (
            <button key={k.kind} type="button" className={chip(kind === k.kind)} onClick={() => onKind(k.kind)}>
              {k.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
