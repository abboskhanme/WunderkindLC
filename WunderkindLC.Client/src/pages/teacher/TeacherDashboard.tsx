import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertCircle,
  CalendarDays,
  CalendarX2,
  ChevronRight,
  ClipboardCheck,
  GraduationCap,
  Send,
  Users,
  Wallet,
} from 'lucide-react'
import type { SalaryLedger } from '@/types'
import {
  getTeacherOverview,
  getTeacherSalary,
  getTeacherSchedule,
  type TeacherLesson,
  type TeacherOverview,
} from '@/api/services/teacher'
import { useAuth } from '@/context/auth-context'
import { useTeacherPortal } from '@/context/teacher-portal-context'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn, formatMoney, openTelegram, telegramUrl } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import {
  TEACHER_NAV,
  addDays,
  dayMonthWeekday,
  daysLabel,
  navVisible,
  nowHm,
  relativeDay,
  timeRange,
} from '@/lib/teacherPortal'
import { Avatar, EmptyState, LessonRow, SkeletonRows, StatTile, TCard } from '@/components/teacher/TeacherUi'

/** Bosh sahifada "o'tgan, lekin davomati kiritilmagan" darslar qidiriladigan kunlar. */
const PENDING_LOOKBACK_DAYS = 7

/**
 * O'qituvchi bosh sahifasi — "bugun nima qilishim kerak":
 * bugungi darslar (holati va "Davomat" tugmasi bilan), o'tgan kunlardan qolib ketgan davomatlar,
 * asosiy ko'rsatkichlar (guruh, o'quvchi, dars, maosh) va tezkor bo'limlar.
 */
export function TeacherDashboard() {
  const { user } = useAuth()
  const { can } = usePerm()
  const portal = useTeacherPortal()
  const [data, setData] = useState<TeacherOverview | null>(null)
  const [pending, setPending] = useState<TeacherLesson[]>([])
  const [salary, setSalary] = useState<SalaryLedger | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [now, setNow] = useState(nowHm)
  const today = todayIso()

  const canSalary = can('salary', 'view')

  useEffect(() => {
    let alive = true
    getTeacherOverview()
      .then((d) => {
        if (!alive) return
        setData(d)
        // O'tgan 7 kun ichida davomati kiritilmagan darslar (faqat jadval ruxsati bo'lsa).
        if (d.canSchedule)
          getTeacherSchedule(addDays(d.today, -PENDING_LOOKBACK_DAYS), addDays(d.today, -1))
            .then((ls) => alive && setPending(ls.filter((l) => !l.conducted && !l.substitutedBy)))
            .catch(() => {}) // ikkilamchi blok — xato bo'lsa shunchaki ko'rinmaydi
      })
      .catch((err) => alive && setError(apiErrorMessage(err, "Ma'lumotni yuklab bo'lmadi")))
    if (canSalary)
      getTeacherSalary()
        .then((s) => alive && setSalary(s))
        .catch(() => {}) // maosh plitkasi ixtiyoriy — ko'rinmay qoladi
    return () => {
      alive = false
    }
  }, [canSalary])

  // Dars holati ("Hozir", "Kutilmoqda") vaqt bilan o'zgaradi — har daqiqada yangilanadi.
  useEffect(() => {
    const id = window.setInterval(() => setNow(nowHm()), 60_000)
    return () => window.clearInterval(id)
  }, [])

  const firstName = (user?.fullName ?? '').trim().split(/\s+/)[0] || 'ustoz'
  const lessons = data?.todayLessons ?? []
  const todayPending = lessons.filter((l) => !l.conducted && !l.substitutedBy && (!l.endTime || l.endTime <= now)).length
  const groups = useMemo(() => data?.groups ?? [], [data])
  const subCount = groups.filter((g) => g.isSubstitute).length

  const month = today.slice(0, 7)
  const monthSalary = salary?.months?.find((m) => m.month === month) ?? null

  // Bugun dars yo'q bo'lsa — eng yaqin keyingi dars (guruh kartalaridan).
  const nextLesson = useMemo(() => {
    const cands = groups.filter((g) => g.nextLessonDate && g.nextLessonDate > today)
    cands.sort((a, b) => a.nextLessonDate.localeCompare(b.nextLessonDate) || a.startTime.localeCompare(b.startTime))
    return cands[0] ?? null
  }, [groups, today])

  const quick = TEACHER_NAV.filter(
    (i) => !['/teacher', '/teacher/profile', '/teacher/feedback'].includes(i.to) && navVisible(i, (p) => can(p, 'view'), portal.isSupport),
  )

  const summary = !data
    ? ''
    : !data.canSchedule
      ? `${groups.length} ta guruh`
      : lessons.length === 0
        ? 'Bugun darsingiz yo\'q'
        : `Bugun ${lessons.length} ta dars${todayPending > 0 ? ` · ${todayPending} tasida davomat kutilmoqda` : ''}`

  return (
    <div className="space-y-3 px-3 pb-6 pt-3 sm:px-4 lg:space-y-4 lg:px-6 lg:pt-5">
      {/* ── Salomlashish ── */}
      <section className="relative overflow-hidden rounded-xl bg-gradient-to-br from-brand-600 to-brand-800 p-4 text-white shadow-[0_8px_24px_rgba(61,104,255,0.25)] sm:p-5">
        <div className="pointer-events-none absolute -right-10 -top-12 h-40 w-40 rounded-full bg-white/10" />
        <div className="pointer-events-none absolute -bottom-16 right-16 h-32 w-32 rounded-full bg-white/5" />
        <div className="relative flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
          <div className="min-w-0">
            <p className="text-[12.5px] font-semibold text-white/80">{dayMonthWeekday(today)}</p>
            <p className="mt-0.5 text-[21px] font-extrabold leading-tight sm:text-[24px]">
              Assalomu alaykum, {firstName} {'\u{1F44B}'}
            </p>
            <p className="mt-1 text-[13.5px] text-white/85">{summary || ' '}</p>
          </div>
          <div className="flex gap-2">
            {can('journal', 'view') && (
              <Link
                to="/teacher/attendance"
                className="inline-flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg bg-white px-4 text-[13.5px] font-bold text-brand-700 transition-colors hover:bg-brand-50 sm:flex-none"
              >
                <ClipboardCheck className="h-4 w-4" /> Davomat qilish
              </Link>
            )}
            {data?.canSchedule && (
              <Link
                to="/teacher/schedule"
                className="inline-flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg border border-white/30 bg-white/10 px-4 text-[13.5px] font-bold text-white transition-colors hover:bg-white/20 sm:flex-none"
              >
                <CalendarDays className="h-4 w-4" /> Jadval
              </Link>
            )}
          </div>
        </div>
      </section>

      {error && (
        <div className="flex items-center gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-[13px] text-red-700">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {/* ── Ko'rsatkichlar ── */}
      <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4 lg:gap-4">
        <StatTile
          icon={Users}
          label="Guruhlar"
          value={data ? groups.length : '—'}
          hint={subCount > 0 ? `${subCount} tasida o'rinbosar` : 'Faol guruhlar'}
          to="/teacher/groups"
        />
        <StatTile
          icon={GraduationCap}
          tone="sky"
          label="O'quvchilar"
          value={data ? data.studentCount : '—'}
          hint="Faol va sinovdagi"
          to={can('journal', 'view') ? '/teacher/students' : undefined}
        />
        <StatTile
          icon={CalendarDays}
          tone={todayPending > 0 ? 'amber' : 'green'}
          label="Bugungi darslar"
          value={data ? (data.canSchedule ? lessons.length : '—') : '—'}
          hint={!data?.canSchedule ? 'Jadval yopiq' : todayPending > 0 ? `${todayPending} ta davomat kutilmoqda` : lessons.length ? 'Hammasi joyida' : 'Dam olish kuni'}
          to={data?.canSchedule ? '/teacher/schedule' : undefined}
        />
        {canSalary ? (
          <StatTile
            icon={Wallet}
            tone="violet"
            label="Bu oy maoshi"
            value={monthSalary ? formatMoney(monthSalary.expected) : '—'}
            hint={monthSalary ? `Berildi: ${formatMoney(monthSalary.paid)}` : 'Hisoblanmagan'}
            to="/teacher/salary"
          />
        ) : (
          <StatTile
            icon={ClipboardCheck}
            tone={pending.length > 0 ? 'red' : 'green'}
            label="Qolib ketgan davomat"
            value={pending.length}
            hint={`Oxirgi ${PENDING_LOOKBACK_DAYS} kun`}
          />
        )}
      </div>

      {/* QARZDORLAR — o'z guruhlari bo'yicha soni va JAMI qarz (uchrashuv talabi, 2026-10-07).
          O'quvchi kesimidagi summa ko'rsatilmaydi; ro'yxatda faqat belgi (`teacher-portal.md`). */}
      {(data?.debtorCount ?? 0) > 0 && (
        <Link
          to={can('journal', 'view') ? '/teacher/students' : '/teacher'}
          className="tap-scale flex items-center gap-3 rounded-xl border border-red-200 bg-red-50 p-3 sm:p-4"
        >
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-red-100 text-red-600">
            <AlertCircle className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="text-[13px] font-semibold text-red-700">
              Qarzdorlar: {data!.debtorCount} ta o'quvchi
            </p>
            <p className="text-[12px] text-red-600">
              Jami qarz: <span className="font-mono font-bold">{formatMoney(data!.debtTotal ?? 0)}</span> — to'lovni eslating
            </p>
          </div>
          <ChevronRight className="h-4 w-4 shrink-0 text-red-400" />
        </Link>
      )}

      {/* ⚠️ `grid-cols-1` SHART: ustunsiz grid telefonda eng uzun (bir qatorli) guruh nomigacha
          kengayib, butun sahifani ekrandan chiqarib yuborardi. */}
      <div className="grid grid-cols-1 gap-3 lg:grid-cols-3 lg:gap-4">
        <div className="min-w-0 space-y-3 lg:col-span-2 lg:space-y-4">
          {/* ── Bugungi darslar ── */}
          <TCard
            title="Bugungi darslar"
            sub={dayMonthWeekday(today)}
            actions={
              data?.canSchedule ? (
                <Link to="/teacher/schedule" className="inline-flex items-center gap-0.5 text-[13px] font-semibold text-brand-600 hover:underline">
                  Jadval <ChevronRight className="h-4 w-4" />
                </Link>
              ) : undefined
            }
          >
            {!data && !error ? (
              <SkeletonRows rows={2} />
            ) : data && !data.canSchedule ? (
              <EmptyState
                icon={CalendarX2}
                title="Dars jadvali yopiq"
                text="Sizga «Dars jadvali» bo'limi berilmagan. Kerak bo'lsa markaz ma'muriyatiga murojaat qiling."
              />
            ) : lessons.length === 0 ? (
              <EmptyState
                icon={CalendarX2}
                title="Bugun darsingiz yo'q"
                text={
                  nextLesson
                    ? `Keyingi dars: ${relativeDay(nextLesson.nextLessonDate, today)}, ${nextLesson.startTime || ''} — ${nextLesson.name}`
                    : 'Yaqin kunlarda rejalashtirilgan dars topilmadi.'
                }
              />
            ) : (
              <div className="space-y-2.5">
                {lessons.map((l) => (
                  <LessonRow key={l.groupId + l.date} lesson={l} today={today} now={now} />
                ))}
              </div>
            )}
          </TCard>

          {/* ── Qolib ketgan davomatlar ── */}
          {pending.length > 0 && (
            <TCard
              title="Davomati kiritilmagan darslar"
              sub={`Oxirgi ${PENDING_LOOKBACK_DAYS} kun — jurnal to'liq bo'lishi uchun belgilang`}
              className="border-amber-200"
            >
              <div className="space-y-2.5">
                {pending.slice(0, 6).map((l) => (
                  <LessonRow key={l.groupId + l.date} lesson={l} today={today} now={now} showDate={relativeDay(l.date, today)} compact />
                ))}
                {pending.length > 6 && (
                  <p className="text-center text-[12.5px] text-mute">Yana {pending.length - 6} ta dars — «Davomat» bo'limida</p>
                )}
              </div>
            </TCard>
          )}
        </div>

        <div className="min-w-0 space-y-3 lg:space-y-4">
          {/* ── Tezkor bo'limlar ── */}
          <TCard title="Tezkor bo'limlar">
            <div className="grid grid-cols-3 gap-2">
              {quick.map((q) => {
                const Icon = q.icon
                return (
                  <Link
                    key={q.to}
                    to={q.to}
                    className="flex flex-col items-center gap-1.5 rounded-lg border border-line-soft bg-panel2 px-1 py-3 text-center transition-colors hover:border-brand-200 hover:bg-brand-50"
                  >
                    <span className="relative flex h-9 w-9 items-center justify-center rounded-lg bg-white text-brand-600 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
                      <Icon className="h-[18px] w-[18px]" />
                    </span>
                    <span className="text-[11.5px] font-semibold leading-tight text-ink">{q.label}</span>
                  </Link>
                )
              })}
            </div>
          </TCard>

          {/* ── Mening guruhlarim ── */}
          <TCard
            title="Mening guruhlarim"
            tight
            actions={
              <Link to="/teacher/groups" className="inline-flex items-center gap-0.5 text-[13px] font-semibold text-brand-600 hover:underline">
                Barchasi <ChevronRight className="h-4 w-4" />
              </Link>
            }
          >
            {!data ? (
              <div className="p-4">
                <SkeletonRows rows={3} />
              </div>
            ) : groups.length === 0 ? (
              <EmptyState icon={Users} title="Guruh yo'q" text="Sizga biriktirilgan faol guruh yo'q. Markaz ma'muriyatiga murojaat qiling." />
            ) : (
              <ul className="divide-y divide-line-soft">
                {groups.slice(0, 6).map((g) => (
                  <li key={g.id}>
                    <Link to={`/teacher/groups/${g.id}`} className="flex items-center gap-3 px-4 py-3 transition-colors hover:bg-panel2">
                      <Avatar name={g.name} size={38} className="rounded-lg" />
                      <div className="min-w-0 flex-1">
                        <p className="truncate text-[14px] font-bold text-ink">{g.name}</p>
                        <p className="truncate text-[12px] text-mute">
                          {g.days.length ? daysLabel(g.days) : 'Kunlar belgilanmagan'}
                          {g.startTime ? ` · ${timeRange(g.startTime, g.endTime)}` : ''}
                        </p>
                      </div>
                      <span className="shrink-0 text-right text-[12px] text-mute">
                        <b className="block text-[14px] text-ink">{g.active + g.trial}</b>
                        o'quvchi
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </TCard>

          {/* ── Telegram kanal (sozlangan bo'lsa) ── */}
          {portal.telegramChannel.trim() && (
            <a
              href={telegramUrl(portal.telegramChannel)}
              onClick={(e) => {
                e.preventDefault()
                openTelegram(portal.telegramChannel)
              }}
              className="flex items-center gap-3 rounded-xl border border-line bg-white p-3.5 shadow-[var(--shadow-card)] transition-colors hover:border-brand-200"
            >
              <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-[#229ED9] text-white">
                <Send className="h-5 w-5" />
              </span>
              <span className="min-w-0 flex-1">
                <span className="block text-[14px] font-bold text-ink">Telegram kanalimiz</span>
                <span className="block text-[12px] text-mute">Markaz e'lonlari — kanalga o'ting</span>
              </span>
              <ChevronRight className={cn('h-5 w-5 shrink-0 text-faint')} />
            </a>
          )}
        </div>
      </div>
    </div>
  )
}
