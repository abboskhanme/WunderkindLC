import { useMemo, useRef, useEffect } from 'react'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'
import { currentMonth, daysOfMonth, shiftMonth, todayIso } from '@/lib/month'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
/** Dushanbadan boshlangan qisqa kun nomlari (JS `getDay()` yakshanbadan boshlanadi). */
const WEEKDAYS = ['Yak', 'Du', 'Se', 'Chor', 'Pay', 'Ju', 'Sha']

/**
 * BIR OYLIK SANALAR CHIZIG'I — oy bo'ylab har kun, ustida shu kunning soni.
 *
 * <p>Ilgari faqat "ish bor" kunlar ko'rsatilardi va ro'yxat sakrab turardi ("05.08, 08.08,
 * 12.08..."). Endi OY TO'LIQ chiqadi: bo'sh kunlar ham o'z o'rnida turadi, ya'ni oy qanday
 * to'lganini bir qarashda ko'rish mumkin.</p>
 *
 * <p>BUGUNGI kun halqa bilan ajratiladi va sahifa ochilganda ko'rinish maydoniga o'ziga
 * sudraladi (uzun oyda 25-sana ekranga sig'masdi).</p>
 */
export function MonthDayStrip({
  month,
  onMonthChange,
  selected,
  onSelect,
  counts,
  marked,
  hint,
  todayCount,
}: {
  /** Ko'rsatilayotgan oy ("yyyy-MM"). */
  month: string
  onMonthChange: (month: string) => void
  /** Tanlangan kun ("yyyy-MM-dd") yoki '' — hech biri. */
  selected: string
  onSelect: (date: string) => void
  /** Kun → son. Berilmagan kun 0 deb hisoblanadi. */
  counts?: Record<string, number>
  /**
   * SON o'rniga oddiy BELGI kerak bo'lganda — "shu kunda ma'lumot bor" kunlar ro'yxati.
   *
   * <p>Manba faqat "bor/yo'q" ni bilganda ishlatiladi (masalan turniket tarixi: serverdan
   * `activeDays` keladi, nechtaligi emas). Har katakka "1" yozib qo'yish ALDAMCHI bo'lardi —
   * o'sha kuni bitta o'tish bo'lgandek ko'rinardi.</p>
   *
   * <p>Berilgan bo'lsa `counts`/`todayCount` chizilmaydi (rejim BITTA bo'ladi).</p>
   */
  marked?: string[]
  /** Chiziq ostidagi tushuntirish. */
  hint?: string
  /** BUGUNGI katak uchun maxsus son (masalan "bugun qilish kerak" — kechikkanlar bilan). */
  todayCount?: number
}) {
  const days = useMemo(() => daysOfMonth(month), [month])
  const markedSet = useMemo(() => (marked ? new Set(marked) : null), [marked])
  const today = todayIso()
  const scrollRef = useRef<HTMLDivElement>(null)
  const todayRef = useRef<HTMLButtonElement>(null)

  // Bugungi kun ko'rinish maydoniga sudraladi (oy o'zgarganda ham).
  useEffect(() => {
    if (!todayRef.current || !scrollRef.current) return
    todayRef.current.scrollIntoView({ block: 'nearest', inline: 'center' })
  }, [month])

  const title = `${MONTHS[Number(month.slice(5, 7)) - 1] ?? month} ${month.slice(0, 4)}`

  return (
    <div>
      <div className="mb-2 flex items-center gap-2">
        <button
          type="button"
          onClick={() => onMonthChange(shiftMonth(month, -1))}
          className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
          title="Oldingi oy"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[130px] text-center text-sm font-semibold text-slate-700">{title}</span>
        <button
          type="button"
          onClick={() => onMonthChange(shiftMonth(month, 1))}
          className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
          title="Keyingi oy"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
        {month !== currentMonth() && (
          <button
            type="button"
            onClick={() => onMonthChange(currentMonth())}
            className="ml-1 text-xs font-medium text-brand-600 hover:underline"
          >
            Joriy oyga qaytish
          </button>
        )}
      </div>

      <div ref={scrollRef} className="flex gap-1 overflow-x-auto pb-1">
        {days.map((d) => {
          const isToday = d === today
          const n = isToday && todayCount !== undefined ? todayCount : (counts?.[d] ?? 0)
          const active = selected === d
          const dow = WEEKDAYS[new Date(`${d}T00:00:00`).getDay()]
          return (
            <button
              key={d}
              ref={isToday ? todayRef : undefined}
              type="button"
              onClick={() => onSelect(active ? '' : d)}
              title={isToday ? 'Bugun' : undefined}
              className={cn(
                'flex min-w-[46px] shrink-0 flex-col items-center rounded-lg border px-1.5 py-1.5 transition-colors',
                active
                  ? 'border-brand-500 bg-brand-50'
                  : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
                // Bugungi kun tanlanmagan bo'lsa ham ajralib tursin.
                isToday && !active && 'ring-1 ring-brand-300',
              )}
            >
              <span className="text-[10px] leading-none text-slate-400">{dow}</span>
              <span
                className={cn(
                  'mt-0.5 text-sm font-semibold leading-none',
                  active ? 'text-brand-700' : 'text-slate-700',
                )}
              >
                {d.slice(8, 10)}
              </span>
              {markedSet ? (
                <span
                  className={cn(
                    'mt-[7px] mb-[3px] h-1.5 w-1.5 rounded-full',
                    markedSet.has(d)
                      ? active
                        ? 'bg-brand-600'
                        : 'bg-brand-400'
                      : 'bg-slate-200',
                  )}
                />
              ) : (
                <span
                  className={cn(
                    'mt-1 text-xs font-bold leading-none',
                    n === 0 ? 'text-slate-300' : active ? 'text-brand-700' : 'text-slate-800',
                  )}
                >
                  {n === 0 ? '·' : n}
                </span>
              )}
            </button>
          )
        })}
      </div>

      {hint && <p className="mt-1.5 text-xs text-slate-400">{hint}</p>}
    </div>
  )
}
