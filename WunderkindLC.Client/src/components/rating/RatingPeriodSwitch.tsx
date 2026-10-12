import { useMemo } from 'react'
import type { RatingPeriodKind } from '@/api/services/rating'
import {
  RATING_PERIOD_KINDS,
  defaultRatingKey,
  ratingKeyOptions,
  type RatingPeriodValue,
} from '@/lib/ratingPeriods'

interface Props {
  value: RatingPeriodValue
  onChange: (v: RatingPeriodValue) => void
  /** Tanlov ro'yxati shu kundan orqaga quriladi (standart — bugun). */
  today?: Date
  className?: string
}

/**
 * Davr tanlovi: «Oy» · «3 oy» · «O'quv yili» + oy/yil ro'yxati. Admin panelida ham, o'quvchi kabinetida
 * ham ishlaydi — ranglar umumiy CSS o'zgaruvchilaridan (`--surface`, `--border`, `--muted`), ya'ni
 * kabinetning tungi rejimida ham to'g'ri ko'rinadi.
 */
export function RatingPeriodSwitch({ value, onChange, today, className }: Props) {
  const now = useMemo(() => today ?? new Date(), [today])
  const options = useMemo(() => ratingKeyOptions(value.kind, now), [value.kind, now])

  const pickKind = (kind: RatingPeriodKind) => {
    if (kind === value.kind) return
    onChange({ kind, key: defaultRatingKey(kind, now) })
  }

  return (
    <div className={`flex flex-wrap items-center gap-2 ${className ?? ''}`}>
      <div
        role="tablist"
        aria-label="Davr"
        className="inline-flex rounded-lg p-0.5"
        style={{ background: 'var(--surface2, var(--surface-2, #f1f3f9))', border: '1px solid var(--border, #dbe0e6)' }}
      >
        {RATING_PERIOD_KINDS.map((k) => {
          const on = k.kind === value.kind
          return (
            <button
              key={k.kind}
              type="button"
              role="tab"
              aria-selected={on}
              onClick={() => pickKind(k.kind)}
              className="rounded-md px-3 py-1.5 text-[13px] font-semibold transition-colors"
              style={
                on
                  ? { background: '#6366f1', color: '#fff' }
                  : { background: 'transparent', color: 'var(--muted, #6b7488)' }
              }
            >
              {k.label}
            </button>
          )
        })}
      </div>
      <select
        aria-label="Davrni tanlang"
        value={value.key}
        onChange={(e) => onChange({ kind: value.kind, key: e.target.value })}
        className="rounded-lg px-2.5 py-1.5 text-[13px] font-semibold"
        style={{
          background: 'var(--surface, #fff)',
          color: 'var(--text, var(--fg, #333))',
          border: '1px solid var(--border, #dbe0e6)',
          // iOS: 16px dan kichik shriftda Safari maydonni kattalashtirib yuboradi.
          fontSize: 'max(13px, min(16px, 3.6vw))',
        }}
      >
        {options.map((o) => (
          <option key={o.key} value={o.key}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  )
}
