import { cn } from '@/lib/utils'

/**
 * edutizimdagi (MUI) kalit: yorliq + kichik "switch". Yoqilganda ko'k, o'chiqda kulrang.
 * Masalan guruh sahifasidagi "Arxiv o'quvchilar", "Guruh o'quvchilari"dagi "Muzlatilgan".
 */
export function Switch({
  checked,
  onChange,
  label,
  className,
  labelFirst = true,
}: {
  checked: boolean
  onChange: (next: boolean) => void
  label?: string
  className?: string
  /** Yorliq kalitdan OLDIN (edutizim guruh sahifasida shunday) yoki keyin. */
  labelFirst?: boolean
}) {
  const text = label ? <span className="text-[13px] font-medium text-[#333]">{label}</span> : null
  return (
    <label className={cn('inline-flex cursor-pointer select-none items-center gap-2', className)}>
      {labelFirst && text}
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors',
          checked ? 'bg-brand-600' : 'bg-[#bdbdbd]',
        )}
      >
        <span
          className={cn(
            'inline-block h-4 w-4 rounded-full bg-white shadow transition-transform',
            checked ? 'translate-x-[18px]' : 'translate-x-0.5',
          )}
        />
      </button>
      {!labelFirst && text}
    </label>
  )
}
