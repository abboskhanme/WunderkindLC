import { useState } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { DAY_STRIP_AHEAD, DAY_STRIP_MAX_WEEKS_AHEAD, dayOfMonth, diffDays, shortWeekday, stripDays, weekShiftFor } from './myDay'

/**
 * Week strip of the day view (the LMS Mini App `DateStrip`): seven days, paged a week at a time
 * both ways (forward up to `DAY_STRIP_MAX_WEEKS_AHEAD`). When the selected day changes from
 * outside ("Bugunga qaytish", a task added for another day) the strip moves to it — corrected
 * during render, not in an effect (no one-frame jump).
 */
export function DateStrip({
  selected,
  today,
  onSelect,
}: {
  selected: string
  today: string
  onSelect: (date: string) => void
}) {
  const [weekShift, setWeekShift] = useState(() => weekShiftFor(selected, today))
  const [tracked, setTracked] = useState(selected)
  if (tracked !== selected) {
    setTracked(selected)
    const needed = weekShiftFor(selected, today)
    if (needed !== weekShift) setWeekShift(needed)
  }

  const days = stripDays(today, weekShift)
  const navBtn =
    'shrink-0 rounded-full p-1.5 text-slate-500 transition-colors hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-30'

  return (
    <div className="flex items-center gap-0.5">
      <button type="button" aria-label="Oldingi hafta" className={navBtn} onClick={() => setWeekShift((v) => v - 1)}>
        <ChevronLeft className="h-5 w-5" />
      </button>
      <div className="flex min-w-0 flex-1 gap-0.5">
        {days.map((date) => {
          const isToday = date === today
          const isSelected = date === selected
          const delta = diffDays(date, today)
          const muted = !isSelected && (delta < -6 || delta > DAY_STRIP_AHEAD)
          return (
            <button
              key={date}
              type="button"
              aria-pressed={isSelected}
              aria-label={date}
              onClick={() => date !== selected && onSelect(date)}
              className={cn(
                'flex min-w-0 flex-1 flex-col items-center rounded-xl py-1.5 transition-colors',
                isSelected ? 'bg-brand-600 text-white' : 'text-slate-700 hover:bg-slate-100',
                muted && 'opacity-45',
              )}
            >
              <span className={cn('text-[11px] leading-tight', !isSelected && 'text-slate-400')}>{shortWeekday(date)}</span>
              <span className={cn('text-base leading-tight tabular-nums', isToday || isSelected ? 'font-bold' : 'font-medium')}>
                {dayOfMonth(date)}
              </span>
              {/* The dot keeps its space even when absent — the row does not jump. */}
              <span className={cn('mt-0.5 h-1 w-1 rounded-full', isToday ? (isSelected ? 'bg-white' : 'bg-brand-600') : 'bg-transparent')} />
            </button>
          )
        })}
      </div>
      <button
        type="button"
        aria-label="Keyingi hafta"
        className={navBtn}
        disabled={weekShift >= DAY_STRIP_MAX_WEEKS_AHEAD}
        onClick={() => setWeekShift((v) => Math.min(DAY_STRIP_MAX_WEEKS_AHEAD, v + 1))}
      >
        <ChevronRight className="h-5 w-5" />
      </button>
    </div>
  )
}
