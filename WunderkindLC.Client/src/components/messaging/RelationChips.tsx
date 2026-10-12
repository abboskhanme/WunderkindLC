import {
  PARENT_RELATIONS,
  RELATIONS,
  STUDENT_RELATIONS,
  isPreset,
  relationChipLabel,
  relationIcon,
  toggleRelation,
  type Relation,
} from '@/lib/relations'
import { cn } from '@/lib/utils'

/**
 * Qarindoshlik chiplari (kimning Telegramiga): «Otasi» «Onasi» «Ota-onasi» «O'quvchining o'zi» «Boshqa»
 * + presetlar «Barcha ota-onalar» va «O'quvchilar» (SPEC P2.8). Bo'sh tanlov ruxsat etilmaydi — oxirgi
 * chipni olib bo'lmaydi (aks holda server standarti — ota-onalar — jimgina qo'llanardi).
 */
export function RelationChips({
  value,
  onChange,
  disabled,
}: {
  value: Relation[]
  onChange: (next: Relation[]) => void
  disabled?: boolean
}) {
  const toggle = (r: Relation) => {
    const next = toggleRelation(value, r)
    if (next.length > 0) onChange(next)
  }
  return (
    <div className="space-y-1.5">
      <div className="flex flex-wrap items-center gap-1.5">
        <PresetChip active={isPreset(value, PARENT_RELATIONS)} disabled={disabled} onClick={() => onChange([...PARENT_RELATIONS])}>
          Barcha ota-onalar
        </PresetChip>
        <PresetChip active={isPreset(value, STUDENT_RELATIONS)} disabled={disabled} onClick={() => onChange([...STUDENT_RELATIONS])}>
          O'quvchilar
        </PresetChip>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        {RELATIONS.map((r) => {
          const on = value.includes(r)
          return (
            <button
              key={r}
              type="button"
              disabled={disabled}
              onClick={() => toggle(r)}
              aria-pressed={on}
              className={cn(
                'inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-medium transition-colors disabled:opacity-50',
                on ? 'border-brand-400 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-500 hover:bg-slate-50',
              )}
            >
              <span aria-hidden>{relationIcon(r)}</span>
              {relationChipLabel(r)}
            </button>
          )
        })}
      </div>
    </div>
  )
}

function PresetChip({
  active,
  disabled,
  onClick,
  children,
}: {
  active: boolean
  disabled?: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'rounded-lg border px-2.5 py-1 text-xs font-semibold transition-colors disabled:opacity-50',
        active ? 'border-brand-500 bg-brand-600 text-white' : 'border-slate-200 text-slate-600 hover:bg-slate-50',
      )}
    >
      {children}
    </button>
  )
}
