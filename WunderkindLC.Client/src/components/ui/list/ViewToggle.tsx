import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export interface ViewToggleOption<V extends string> {
  value: V
  /** Maslahat matni (`title` / `aria-label`). */
  label: string
  /** Tabler ikonka, 20px. */
  icon: ReactNode
}

/**
 * edutizimdagi ko'rinish almashtirgich (MUI ToggleButtonGroup): oq quti, radius 8, ichki 4px;
 * har tugma 36×28, radius 8; tanlangani — ko'k fon + oq ikonka.
 */
export function ViewToggle<V extends string>({
  value,
  options,
  onChange,
}: {
  value: V
  options: ViewToggleOption<V>[]
  onChange: (v: V) => void
}) {
  return (
    <div role="group" className="inline-flex shrink-0 gap-1 rounded-lg border border-[#dbe0e6] bg-white p-1">
      {options.map((o) => {
        const active = o.value === value
        return (
          <button
            key={o.value}
            type="button"
            title={o.label}
            aria-label={o.label}
            aria-pressed={active}
            onClick={() => onChange(o.value)}
            className={cn(
              'inline-flex h-7 w-9 items-center justify-center rounded-lg transition-colors',
              active ? 'bg-brand-600 text-white' : 'text-brand-600 hover:bg-brand-600/10',
            )}
          >
            {o.icon}
          </button>
        )
      })}
    </div>
  )
}
