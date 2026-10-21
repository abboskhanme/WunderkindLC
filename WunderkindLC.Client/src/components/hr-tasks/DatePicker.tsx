import { DateInput } from '@/components/ui/DateInput'
import { cn } from '@/lib/utils'

const base =
  'w-full rounded-lg border bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100 disabled:bg-slate-50 disabled:text-slate-400'

/**
 * The LMS `DatePicker` API (`onChange(value)` with an ISO `yyyy-MM-dd`) over LC's `DateInput`
 * (always «kun.oy.yil», `.claude/rules/number-inputs.md` §2.5) — the HR pages keep their
 * call sites unchanged.
 */
export function DatePicker({
  value,
  onChange,
  min,
  max,
  disabled,
  invalid,
  ariaLabel,
  className,
}: {
  value: string
  onChange: (value: string) => void
  min?: string
  max?: string
  disabled?: boolean
  invalid?: boolean
  ariaLabel?: string
  className?: string
}) {
  return (
    <DateInput
      value={value}
      onChange={(e) => onChange(e.target.value)}
      min={min}
      max={max}
      disabled={disabled}
      aria-label={ariaLabel}
      className={cn(base, invalid ? 'border-red-300' : 'border-slate-200', className ?? 'w-40')}
    />
  )
}
