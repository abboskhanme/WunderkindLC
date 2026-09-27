import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { AlertCircle, CalendarX2, ChevronLeft, ChevronRight, DoorOpen, Repeat, UserCheck, Users } from 'lucide-react'
import { getTeacherSchedule, type TeacherLesson } from '@/api/services/teacher'
import { apiErrorMessage, cn } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import {
  PHASE_STYLE,
  WEEKDAY_LONG,
  WEEKDAY_SHORT,
  addDays,
  dayMonth,
  dayMonthWeekday,
  lessonPhase,
  nowHm,
  timeRange,
  weekRangeLabel,
  weekStartOf,
} from '@/lib/teacherPortal'
import { EmptyState, LessonRow, SkeletonRows, TCard, TPage } from '@/components/teacher/TeacherUi'

/**
 * DARS JADVALI — o'qituvchining haftalik darslari (`GET /api/teacher/schedule`).
 * Ko'chirilgan darslar yangi kunida, o'rinbosarlik kunlari va "bugun darsni boshqa odam o'tadi"
 * belgilari bilan. Telefonda: kunlar chizig'i + tanlangan kun ro'yxati; kompyuterda: 7 ustunli hafta.
 * Hafta manzilda saqlanadi (`?week=yyyy-MM-dd`) — orqaga tugmasi va havola ishlasin.
 */
export function TeacherSchedulePage() {
  const [params, setParams] = useSearchParams()
  const today = todayIso()
  const rawWeek = params.get('week') ?? ''
  const weekStart = /^\d{4}-\d{2}-\d{2}$/.test(rawWeek) ? weekStartOf(rawWeek) : weekStartOf(today)
  const weekEnd = addDays(weekStart, 6)
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStart, i))

  const [loaded, setLoaded] = useState<{ week: string; lessons: TeacherLesson[] } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pickedDay, setPickedDay] = useState<string | null>(null)
  const now = nowHm()

  useEffect(() => {
    let alive = true
    getTeacherSchedule(weekStart, weekEnd)
      .then((ls) => alive && setLoaded({ week: weekStart, lessons: ls }))
      .catch((err) => {
        if (!alive) return
        setLoaded({ week: weekStart, lessons: [] })
        setError(apiErrorMessage(err, "Jadvalni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [weekStart, weekEnd])

  const lessons = loaded?.week === weekStart ? loaded.lessons : null
  const byDay = useMemo(() => {
    const m = new Map<string, TeacherLesson[]>()
    for (const l of lessons ?? []) m.set(l.date, [...(m.get(l.date) ?? []), l])
    return m
  }, [lessons])

  // Telefonda ko'rsatiladigan kun: tanlangan (shu haftada bo'lsa) > bugun (shu haftada bo'lsa) > dushanba.
  const day = pickedDay && days.includes(pickedDay) ? pickedDay : days.includes(today) ? today : weekStart
  const dayLessons = byDay.get(day) ?? []
  const total = lessons?.length ?? 0
  const hours = (lessons ?? []).reduce((acc, l) => {
    const [sh, sm] = l.startTime.split(':').map(Number)
    const [eh, em] = l.endTime.split(':').map(Number)
    return Number.isFinite(sh) && Number.isFinite(eh) ? acc + (eh * 60 + (em || 0) - (sh * 60 + (sm || 0))) : acc
  }, 0)

  const goWeek = (delta: number) => {
    setError(null)
    setPickedDay(null)
    const p = new URLSearchParams(params)
    p.set('week', addDays(weekStart, delta * 7))
    setParams(p, { replace: true })
  }
  const goToday = () => {
    setError(null)
    setPickedDay(today)
    const p = new URLSearchParams(params)
    p.delete('week')
    setParams(p, { replace: true })
  }

  return (
    <TPage
      title="Dars jadvali"
      sub={
        lessons
          ? `${weekRangeLabel(weekStart, weekEnd)} · ${total} ta dars${hours > 0 ? ` · ${Math.round((hours / 60) * 10) / 10} soat` : ''}`
          : weekRangeLabel(weekStart, weekEnd)
      }
      actions={
        <div className="flex w-full items-center gap-1.5 sm:w-auto">
          <button
            type="button"
            onClick={() => goWeek(-1)}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600"
            aria-label="Oldingi hafta"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="flex h-9 min-w-0 flex-1 items-center justify-center rounded-lg border border-line bg-white px-3 text-[13px] font-semibold text-ink sm:w-44 sm:flex-none">
            {weekRangeLabel(weekStart, weekEnd)}
          </span>
          <button
            type="button"
            onClick={() => goWeek(1)}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600"
            aria-label="Keyingi hafta"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          {!days.includes(today) && (
            <button
              type="button"
              onClick={goToday}
              className="h-9 shrink-0 rounded-lg border border-brand-600/40 px-3 text-[13px] font-semibold text-brand-600 hover:bg-brand-50"
            >
              Bu hafta
            </button>
          )}
        </div>
      }
    >
      {error && (
        <div className="mb-3 flex items-center gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-[13px] text-red-700">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {/* ── TELEFON / PLANSHET: kunlar chizig'i + kun ro'yxati ── */}
      <div className="lg:hidden">
        <div className="mb-3 grid grid-cols-7 gap-1 rounded-xl border border-line bg-white p-1.5">
          {days.map((d, i) => {
            const active = d === day
            const count = byDay.get(d)?.length ?? 0
            return (
              <button
                key={d}
                type="button"
                onClick={() => setPickedDay(d)}
                className={cn(
                  'flex flex-col items-center gap-0.5 rounded-lg py-1.5 transition-colors',
                  active ? 'bg-brand-600 text-white' : d === today ? 'bg-brand-50 text-brand-700' : 'text-ink hover:bg-panel3',
                )}
              >
                <span className={cn('text-[11px] font-semibold', active ? 'text-white/80' : 'text-mute')}>{WEEKDAY_SHORT[i]}</span>
                <span className="text-[16px] font-bold leading-tight">{Number(d.slice(8, 10))}</span>
                <span
                  className={cn(
                    'h-1.5 w-1.5 rounded-full',
                    count === 0 ? 'bg-transparent' : active ? 'bg-white' : 'bg-brand-600',
                  )}
                />
              </button>
            )
          })}
        </div>

        <TCard
          title={d2title(day, today)}
          sub={lessons ? (dayLessons.length ? `${dayLessons.length} ta dars` : "Dars yo'q") : undefined}
        >
          {!lessons ? (
            <SkeletonRows rows={2} />
          ) : dayLessons.length === 0 ? (
            <EmptyState icon={CalendarX2} title="Bu kunda dars yo'q" text="Boshqa kunni tanlang yoki haftani almashtiring." className="py-6" />
          ) : (
            <div className="space-y-2.5">
              {dayLessons.map((l) => (
                <LessonRow key={l.groupId + l.date} lesson={l} today={today} now={now} />
              ))}
            </div>
          )}
        </TCard>
      </div>

      {/* ── KOMPYUTER: 7 ustunli hafta ── */}
      <div className="hidden gap-2 lg:grid lg:grid-cols-7">
        {days.map((d, i) => {
          const list = byDay.get(d) ?? []
          const isToday = d === today
          return (
            <div key={d} className={cn('flex min-h-[320px] flex-col rounded-xl border bg-white', isToday ? 'border-brand-300 ring-1 ring-brand-100' : 'border-line')}>
              <div className={cn('rounded-t-xl border-b px-3 py-2', isToday ? 'border-brand-100 bg-brand-50' : 'border-line-soft')}>
                <p className={cn('text-[12px] font-semibold', isToday ? 'text-brand-700' : 'text-mute')}>
                  {WEEKDAY_LONG[i]}
                  {isToday && ' · bugun'}
                </p>
                <p className="text-[15px] font-bold text-ink">{dayMonth(d)}</p>
              </div>
              <div className="flex-1 space-y-2 p-2">
                {!lessons ? (
                  <div className="skeleton h-16 rounded-lg" />
                ) : list.length === 0 ? (
                  <p className="pt-6 text-center text-[12px] text-faint">Dars yo'q</p>
                ) : (
                  list.map((l) => <WeekCell key={l.groupId} l={l} today={today} now={now} />)
                )}
              </div>
            </div>
          )
        })}
      </div>

      {/* Belgilar izohi */}
      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-[12px] text-mute">
        {(['done', 'pending', 'now', 'substituted'] as const).map((p) => (
          <span key={p} className="inline-flex items-center gap-1.5">
            <span className={cn('h-2 w-2 rounded-full', PHASE_STYLE[p].dot)} /> {PHASE_STYLE[p].label}
          </span>
        ))}
        <span className="inline-flex items-center gap-1.5">
          <Repeat className="h-3.5 w-3.5 text-sky-600" /> Ko'chirilgan dars
        </span>
        <span className="inline-flex items-center gap-1.5">
          <UserCheck className="h-3.5 w-3.5 text-violet-600" /> Siz o'rinbosarsiz
        </span>
      </div>
    </TPage>
  )
}

function d2title(day: string, today: string): string {
  return day === today ? `Bugun — ${dayMonthWeekday(day)}` : dayMonthWeekday(day)
}

/** Kompyuterdagi hafta katagidagi bitta dars. */
function WeekCell({ l, today, now }: { l: TeacherLesson; today: string; now: string }) {
  const phase = lessonPhase(l, today, now)
  const s = PHASE_STYLE[phase]
  const canOpen = !l.substitutedBy && l.date <= today
  const to = canOpen ? `/teacher/attendance?group=${encodeURIComponent(l.groupId)}&date=${l.date}` : `/teacher/groups/${l.groupId}`
  return (
    <Link
      to={to}
      title={canOpen ? 'Davomatni ochish' : "Guruhni ochish"}
      className={cn(
        'block rounded-lg border border-l-[3px] bg-white p-2 transition-colors hover:bg-panel2',
        phase === 'done' && 'border-l-emerald-500',
        phase === 'pending' && 'border-l-amber-500',
        phase === 'now' && 'border-l-brand-600 bg-brand-50/40',
        (phase === 'upcoming' || phase === 'future') && 'border-l-slate-300',
        phase === 'substituted' && 'border-l-slate-400 opacity-70',
      )}
    >
      <p className="text-[12px] font-bold text-ink">{timeRange(l.startTime, l.endTime) || '—'}</p>
      <p className="truncate text-[13px] font-bold text-brand-700">{l.groupName}</p>
      <p className="truncate text-[11.5px] text-mute">{l.courseName}</p>
      <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-mute">
        {l.room && (
          <span className="inline-flex items-center gap-0.5">
            <DoorOpen className="h-3 w-3" /> {l.room}
          </span>
        )}
        <span className="inline-flex items-center gap-0.5">
          <Users className="h-3 w-3" /> {l.studentCount}
        </span>
        {l.moved && <Repeat className="h-3 w-3 text-sky-600" />}
        {l.isSubstitute && <UserCheck className="h-3 w-3 text-violet-600" />}
      </div>
      <p className={cn('mt-1 truncate text-[10.5px] font-semibold', phase === 'done' ? 'text-emerald-700' : phase === 'pending' ? 'text-amber-700' : 'text-faint')}>
        {phase === 'substituted' ? `O'rinbosar: ${l.substitutedBy}` : s.label}
      </p>
    </Link>
  )
}
