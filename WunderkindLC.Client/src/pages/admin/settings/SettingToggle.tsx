import type { ComponentType } from 'react'
import { cn } from '@/lib/utils'

/** Sozlama oynalaridagi yoqish/o'chirish qatori (JournalPolicyModal'dagi `ToggleRow` uslubi). */
export function SettingToggle({
  icon: Icon,
  title,
  desc,
  checked,
  onChange,
}: {
  icon: ComponentType<{ className?: string }>
  title: string
  desc: string
  checked: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      onClick={() => onChange(!checked)}
      className={cn(
        'flex w-full items-start gap-3 rounded-xl border p-3 text-left transition-all',
        checked ? 'border-brand-400 bg-brand-50' : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
      )}
    >
      <Icon className={cn('mt-0.5 h-4 w-4 shrink-0', checked ? 'text-brand-600' : 'text-slate-400')} />
      <div className="min-w-0 flex-1">
        <div className={cn('text-sm font-semibold', checked ? 'text-brand-700' : 'text-slate-700')}>{title}</div>
        <p className="mt-0.5 text-xs leading-relaxed text-slate-500">{desc}</p>
      </div>
      <span
        className={cn(
          'relative mt-0.5 inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors',
          checked ? 'bg-brand-500' : 'bg-slate-200',
        )}
      >
        <span
          className={cn(
            'inline-block h-3.5 w-3.5 rounded-full bg-white shadow transition-transform',
            checked ? 'translate-x-[18px]' : 'translate-x-[3px]',
          )}
        />
      </span>
    </button>
  )
}
