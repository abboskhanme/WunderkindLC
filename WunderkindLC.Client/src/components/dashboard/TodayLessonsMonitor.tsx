import { useMemo, useState } from 'react'
import {
  CalendarCheck,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ClipboardList,
  Star,
  XCircle,
} from 'lucide-react'
import type { TodayLessonMonitor } from '@/types'
import { getTodayLessons } from '@/api/services/dashboard'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { cn } from '@/lib/utils'

const DAYS = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']
const MONTHS = [
  'Yanvar',
  'Fevral',
  'Mart',
  'Aprel',
  'May',
  'Iyun',
  'Iyul',
  'Avgust',
  'Sentabr',
  'Oktabr',
  'Noyabr',
  'Dekabr',
]

const pad = (n: number) => String(n).padStart(2, '0')
const fmtDate = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`

/** Bajarilgan/bajarilmagan holat chipi (davomat yoki baho uchun). */
function StatusChip({ done, label }: { done: boolean; label: string }) {
  const Icon = done ? CheckCircle2 : XCircle
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold whitespace-nowrap',
        done
          ? 'border-emerald-200 bg-emerald-50 text-emerald-700'
          : 'border-red-200 bg-red-50 text-red-600',
      )}
    >
      <Icon className="h-3.5 w-3.5" />
      {label}
    </span>
  )
}

/**
 * Bosh sahifa "Darslar monitoringi" — tanlangan sanada (default bugun) dars kuni bo'lgan har bir
 * guruh bo'yicha o'qituvchi davomat qilganmi va baho qo'yganmi ko'rsatadi. O'qituvchi bo'yicha
 * guruhlangan, keng ekranda ikki ustun. Tepada kun/oy tanlash — eski sanalarni ham ko'rish mumkin.
 */
type AttFilter = 'all' | 'done' | 'undone'
const ATT_FILTERS: { value: AttFilter; label: string }[] = [
  { value: 'all', label: 'Hammasi' },
  { value: 'undone', label: 'Davomat qilmagan' },
  { value: 'done', label: 'Davomat qilgan' },
]

export function TodayLessonsMonitor() {
  const todayStr = fmtDate(new Date())
  const [date, setDate] = useState(todayStr)
  const [attFilter, setAttFilter] = useState<AttFilter>('all')
  const isToday = date === todayStr

  const { data, loading, error } = useAsync(() => getTodayLessons(date), [date])

  /** Kunni ±1 ga surish (bugundan keyinga o'tkazmaydi). */
  const shiftDay = (delta: number) => {
    const [y, m, d] = date.split('-').map(Number)
    const next = fmtDate(new Date(y, m - 1, d + delta))
    setDate(next > todayStr ? todayStr : next)
  }

  // Oy tanlash ro'yxati — oxirgi 12 oy (+tanlangan sana undan eski bo'lsa, u ham qo'shiladi).
  const monthOptions = useMemo(() => {
    const now = new Date()
    const opts = Array.from({ length: 12 }, (_, i) => {
      const d = new Date(now.getFullYear(), now.getMonth() - i, 1)
      return {
        value: `${d.getFullYear()}-${pad(d.getMonth() + 1)}`,
        label: `${MONTHS[d.getMonth()]} ${d.getFullYear()}`,
      }
    })
    const cur = date.slice(0, 7)
    if (!opts.some((o) => o.value === cur)) {
      const [y, m] = cur.split('-').map(Number)
      opts.push({ value: cur, label: `${MONTHS[m - 1]} ${y}` })
    }
    return opts
  }, [date])

  /** Oy tanlanganda: joriy oy bo'lsa — bugun, aks holda o'sha oyning 1-kuni. */
  const onMonthChange = (value: string) => {
    setDate(value === todayStr.slice(0, 7) ? todayStr : `${value}-01`)
  }

  const { byTeacher, doneAtt, doneGrade, total, shownTotal } = useMemo(() => {
    const lessons = data?.lessons ?? []
    // Filtr faqat ko'rsatiladigan ro'yxatga ta'sir qiladi; yig'indi hisoblari (yuqorida)
    // to'liq kun bo'yicha qoladi.
    const filtered =
      attFilter === 'done'
        ? lessons.filter((l) => l.attendanceDone)
        : attFilter === 'undone'
          ? lessons.filter((l) => !l.attendanceDone)
          : lessons
    const map = new Map<string, { teacher: string; lessons: TodayLessonMonitor[] }>()
    for (const l of filtered) {
      const key = l.teacherId || 'none'
      if (!map.has(key)) map.set(key, { teacher: l.teacherName, lessons: [] })
      map.get(key)!.lessons.push(l)
    }
    const byTeacher = [...map.values()].sort((a, b) => a.teacher.localeCompare(b.teacher))
    return {
      byTeacher,
      doneAtt: lessons.filter((l) => l.attendanceDone).length,
      doneGrade: lessons.filter((l) => l.gradesDone).length,
      total: lessons.length,
      shownTotal: filtered.length,
    }
  }, [data, attFilter])

  return (
    <Card
      title={isToday ? 'Bugungi darslar' : 'Darslar monitoringi'}
      sub={
        data
          ? `${DAYS[data.dayIndex]} · ${data.date} — o'qituvchilar davomat qildimi va baho qo'ydimi`
          : "Darslar bo'yicha davomat/baho nazorati"
      }
      actions={
        <div className="flex flex-wrap items-center justify-end gap-1.5">
          <select
            value={date.slice(0, 7)}
            onChange={(e) => onMonthChange(e.target.value)}
            className="h-8 rounded-lg border border-slate-200 bg-white px-2 text-xs font-semibold text-slate-700 outline-none focus:border-blue-300"
            aria-label="Oy tanlash"
          >
            {monthOptions.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </select>
          <button
            type="button"
            onClick={() => shiftDay(-1)}
            className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-50"
            aria-label="Oldingi kun"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <input
            type="date"
            value={date}
            max={todayStr}
            onChange={(e) => e.target.value && setDate(e.target.value)}
            className="h-8 rounded-lg border border-slate-200 bg-white px-2 text-xs font-semibold text-slate-700 outline-none focus:border-blue-300"
            aria-label="Sana tanlash"
          />
          <button
            type="button"
            onClick={() => shiftDay(1)}
            disabled={isToday}
            className="grid h-8 w-8 place-items-center rounded-lg border border-slate-200 text-slate-500 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
            aria-label="Keyingi kun"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          {!isToday && (
            <button
              type="button"
              onClick={() => setDate(todayStr)}
              className="h-8 rounded-lg border border-blue-200 bg-blue-50 px-2.5 text-xs font-semibold text-blue-700 hover:bg-blue-100"
            >
              Bugun
            </button>
          )}
        </div>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : error ? (
        <p className="text-red-600">Xatolik: {error}</p>
      ) : total === 0 ? (
        <div className="state">
          <div className="state-icon">
            <ClipboardList className="h-6 w-6" />
          </div>
          <h4>{isToday ? "Bugun dars yo'q" : "Bu kunda dars yo'q"}</h4>
          <p>
            {isToday
              ? 'Bugun uchun rejalashtirilgan guruh darsi topilmadi.'
              : 'Tanlangan sana uchun rejalashtirilgan guruh darsi topilmadi.'}
          </p>
        </div>
      ) : (
        <>
          <div className="mb-3 flex flex-wrap items-center gap-2 text-xs font-semibold">
            <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-1 text-emerald-700">
              <CalendarCheck className="h-3.5 w-3.5" />
              Davomat: {doneAtt}/{total}
            </span>
            <span className="inline-flex items-center gap-1 rounded-full bg-blue-50 px-2.5 py-1 text-blue-700">
              <Star className="h-3.5 w-3.5" />
              Baho: {doneGrade}/{total}
            </span>
            {/* Davomat holati bo'yicha filtr — faqat pastdagi ro'yxatni toraytiradi. */}
            <div className="ml-auto inline-flex rounded-lg border border-slate-200 p-0.5">
              {ATT_FILTERS.map((f) => (
                <button
                  key={f.value}
                  type="button"
                  onClick={() => setAttFilter(f.value)}
                  className={cn(
                    'rounded-md px-2.5 py-1 text-[11px] font-semibold transition-colors',
                    attFilter === f.value
                      ? 'bg-blue-600 text-white'
                      : 'text-slate-600 hover:bg-slate-100',
                  )}
                >
                  {f.label}
                </button>
              ))}
            </div>
          </div>
          {shownTotal === 0 ? (
            <div className="state">
              <div className="state-icon">
                <ClipboardList className="h-6 w-6" />
              </div>
              <h4>Ro'yxat bo'sh</h4>
              <p>
                {attFilter === 'undone'
                  ? 'Barcha darslarga davomat qilingan.'
                  : attFilter === 'done'
                    ? 'Hali hech bir darsga davomat qilinmagan.'
                    : "Dars yo'q."}
              </p>
            </div>
          ) : (
          /* Keng ekranda 2 ustun — bitta o'qituvchi bloki bo'linmasdan (break-inside-avoid) joylashadi. */
          <div className="gap-x-6 md:columns-2">
            {byTeacher.map((t) => (
              <div key={t.teacher} className="mb-4 break-inside-avoid">
                <p className="mb-1.5 text-xs font-bold uppercase tracking-wide text-slate-500">
                  {t.teacher}
                </p>
                <div className="space-y-1.5">
                  {t.lessons.map((l) => (
                    <div
                      key={l.groupId}
                      className={cn(
                        'flex flex-wrap items-center justify-between gap-2 rounded-lg border p-2.5',
                        l.attendanceDone && l.gradesDone
                          ? 'border-emerald-100 bg-emerald-50/40'
                          : 'border-slate-100 bg-white',
                      )}
                    >
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-sm font-semibold text-slate-800">
                          {l.groupName}
                          {l.courseName && (
                            <span className="font-normal text-slate-400"> · {l.courseName}</span>
                          )}
                        </p>
                        <p className="truncate text-xs text-slate-400">
                          {l.startTime}
                          {l.endTime ? `–${l.endTime}` : ''}
                          {l.room ? ` · ${l.room}` : ''}
                          {` · ${l.studentsCount} o'quvchi`}
                        </p>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <StatusChip done={l.attendanceDone} label="Davomat" />
                        <StatusChip done={l.gradesDone} label="Baho" />
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
          )}
        </>
      )}
    </Card>
  )
}
