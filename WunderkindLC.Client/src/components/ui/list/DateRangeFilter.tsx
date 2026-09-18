import { IconCalendar, IconX } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * edutizimdagi "Oraliqni tanlang" filtri: kalendar ikonkasi + ikki sana (dan — gacha) bitta
 * ramkada, 34px. Qiymat bo'lsa o'ngda "×" — tozalash. Sanalar ISO ("yyyy-MM-dd").
 */
export function DateRangeFilter({
  from,
  to,
  onChange,
  className,
}: {
  from: string
  to: string
  onChange: (from: string, to: string) => void
  className?: string
}) {
  const input =
    'h-full min-w-0 flex-1 bg-transparent text-[13px] text-black outline-none [color-scheme:light]'
  return (
    <div
      className={cn(
        'flex h-[34px] items-center gap-1.5 rounded-lg border border-black/25 bg-white pl-2.5 pr-1.5 transition-colors focus-within:border-brand-600 hover:border-black/60',
        className,
      )}
      title="Oraliqni tanlang"
    >
      <IconCalendar className="h-4 w-4 shrink-0 text-[#6b7280]" />
      <input
        type="date"
        aria-label="Boshlanish sanasi"
        value={from}
        max={to || undefined}
        onChange={(e) => onChange(e.target.value, to)}
        className={input}
      />
      <span className="text-[#9e9e9e]">—</span>
      <input
        type="date"
        aria-label="Tugash sanasi"
        value={to}
        min={from || undefined}
        onChange={(e) => onChange(from, e.target.value)}
        className={input}
      />
      {(from || to) && (
        <button
          type="button"
          aria-label="Tozalash"
          onClick={() => onChange('', '')}
          className="rounded p-0.5 text-[#757575] hover:bg-black/[0.06]"
        >
          <IconX className="h-3.5 w-3.5" />
        </button>
      )}
    </div>
  )
}
