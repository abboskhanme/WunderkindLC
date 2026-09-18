import { IconCalendar } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * edutizim filtr to'ridagi SANA ORALIG'I ("Sana", "Oraliqni tanlang") — bitta 34px katakda ikki
 * sana: "dan — gacha". Chegaralar bir-birini cheklaydi (dan ≤ gacha). Bo'sh qiymat = chegara yo'q.
 */
export function FilterDateRange({
  from,
  to,
  onChange,
  className,
  title,
}: {
  from: string
  to: string
  onChange: (from: string, to: string) => void
  className?: string
  title?: string
}) {
  const empty = !from && !to
  return (
    <div
      title={title}
      className={cn(
        'flex h-[34px] w-full items-center gap-1 rounded-lg border border-black/25 bg-white pl-2 pr-1 text-[13px] transition-colors hover:border-black/60 focus-within:border-brand-600 focus-within:ring-1 focus-within:ring-brand-600',
        empty ? 'text-[#6b7280]' : 'text-black',
        className,
      )}
    >
      <IconCalendar className="h-4 w-4 shrink-0 text-[#6b7280]" />
      <input
        type="date"
        value={from}
        max={to || undefined}
        onChange={(e) => onChange(e.target.value, to)}
        aria-label="Dan"
        className="min-w-0 flex-1 bg-transparent outline-none"
      />
      <span className="text-[#9ca3af]">—</span>
      <input
        type="date"
        value={to}
        min={from || undefined}
        onChange={(e) => onChange(from, e.target.value)}
        aria-label="Gacha"
        className="min-w-0 flex-1 bg-transparent outline-none"
      />
    </div>
  )
}
