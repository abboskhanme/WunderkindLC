import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertCircle,
  BookOpenCheck,
  CalendarClock,
  CalendarDays,
  ClipboardCheck,
  Clock,
  DoorOpen,
  Search,
  UserCheck,
  Users,
} from 'lucide-react'
import { getTeacherOverview, type TeacherGroupCard } from '@/api/services/teacher'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import { daysLabel, relativeDay, timeRange } from '@/lib/teacherPortal'
import { Avatar, EmptyState, TPage } from '@/components/teacher/TeacherUi'

/**
 * "Guruhlarim" — o'qituvchining barcha faol guruhlari: jadval (kunlar, vaqt, xona), o'quvchilar
 * soni (faol / sinov / muzlatilgan), keyingi dars va tezkor amallar (Jurnal, Davomat).
 * Ma'lumot `GET /api/teacher/overview` dan (bosh sahifa bilan bir xil manba).
 * `/teacher/journal` (eski manzil) ham shu sahifani ochadi.
 */
export function TeacherGroupsPage() {
  const { can } = usePerm()
  const [groups, setGroups] = useState<TeacherGroupCard[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const today = todayIso()
  const canJournal = can('journal', 'view')

  useEffect(() => {
    getTeacherOverview()
      .then((d) => setGroups(d.groups))
      .catch((err) => setError(apiErrorMessage(err, "Guruhlarni yuklab bo'lmadi")))
  }, [])

  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    return (groups ?? []).filter((g) => !s || g.name.toLowerCase().includes(s) || g.courseName.toLowerCase().includes(s))
  }, [groups, q])

  const total = (groups ?? []).reduce((a, g) => a + g.active + g.trial, 0)

  return (
    <TPage
      title="Guruhlarim"
      sub={groups ? `${groups.length} ta guruh · ${total} o'quvchi` : 'Yuklanmoqda…'}
      actions={
        (groups?.length ?? 0) > 4 ? (
          <label className="flex h-9 w-full items-center gap-2 rounded-lg border border-line bg-white px-3 sm:w-64">
            <Search className="h-4 w-4 shrink-0 text-faint" />
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Guruh yoki kurs…"
              className="min-w-0 flex-1 bg-transparent text-[13.5px] text-ink outline-none placeholder:text-faint"
            />
          </label>
        ) : undefined
      }
    >
      {error && (
        <div className="mb-3 flex items-center gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-[13px] text-red-700">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {!groups && !error ? (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <div key={i} className="skeleton h-[230px] rounded-xl" />
          ))}
        </div>
      ) : groups && groups.length === 0 ? (
        <div className="rounded-xl border border-line bg-white">
          <EmptyState icon={Users} title="Guruh yo'q" text="Sizga biriktirilgan faol guruh yo'q. Markaz ma'muriyatiga murojaat qiling." />
        </div>
      ) : filtered.length === 0 ? (
        <div className="rounded-xl border border-line bg-white">
          <EmptyState icon={Search} title="Topilmadi" text={`«${q}» bo'yicha guruh yo'q`} />
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
          {filtered.map((g) => (
            <GroupCard key={g.id} g={g} today={today} canJournal={canJournal} />
          ))}
        </div>
      )}
    </TPage>
  )
}

function GroupCard({ g, today, canJournal }: { g: TeacherGroupCard; today: string; canJournal: boolean }) {
  const lessonToday = g.nextLessonDate === today
  return (
    <div className="flex min-w-0 flex-col rounded-xl border border-line bg-white shadow-[var(--shadow-card)]">
      <Link to={`/teacher/groups/${g.id}`} className="flex items-center gap-3 border-b border-line-soft p-4 transition-colors hover:bg-panel2">
        <Avatar name={g.name} size={44} className="rounded-xl" />
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-1.5">
            <p className="truncate text-[15.5px] font-bold text-ink">{g.name}</p>
            {g.isSubstitute && (
              <span className="inline-flex shrink-0 items-center gap-1 rounded-md bg-violet-50 px-1.5 py-0.5 text-[10.5px] font-bold text-violet-700">
                <UserCheck className="h-3 w-3" /> O'rinbosar
              </span>
            )}
          </div>
          <p className="truncate text-[12.5px] text-mute">{g.courseName || 'Kurs belgilanmagan'}</p>
        </div>
      </Link>

      <div className="grid flex-1 grid-cols-2 gap-x-3 gap-y-2.5 p-4 text-[12.5px]">
        <Info icon={CalendarDays} label="Kunlar" value={g.days.length ? daysLabel(g.days) : '—'} wide />
        <Info icon={Clock} label="Vaqt" value={timeRange(g.startTime, g.endTime) || '—'} />
        <Info icon={DoorOpen} label="Xona" value={g.room || '—'} />
        <div className="col-span-2 flex flex-wrap gap-1.5 pt-0.5">
          <Pill cls="bg-emerald-50 text-emerald-700">{g.active} faol</Pill>
          {g.trial > 0 && <Pill cls="bg-violet-50 text-violet-700">{g.trial} sinovda</Pill>}
          {g.frozen > 0 && <Pill cls="bg-sky-50 text-sky-700">{g.frozen} muzlatilgan</Pill>}
        </div>
        {g.nextLessonDate && (
          <p
            className={cn(
              'col-span-2 flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-[12.5px] font-semibold',
              lessonToday ? 'bg-brand-50 text-brand-700' : 'bg-panel3 text-mute',
            )}
          >
            <CalendarClock className="h-4 w-4 shrink-0" />
            Keyingi dars: {relativeDay(g.nextLessonDate, today)}
            {g.startTime ? `, ${g.startTime}` : ''}
          </p>
        )}
      </div>

      {canJournal && (
        <div className="grid grid-cols-2 gap-2 border-t border-line-soft p-3">
          <Link
            to={`/teacher/groups/${g.id}`}
            className="inline-flex h-9 items-center justify-center gap-1.5 rounded-lg border border-brand-600/40 text-[13px] font-semibold text-brand-600 transition-colors hover:bg-brand-50"
          >
            <BookOpenCheck className="h-4 w-4" /> Jurnal
          </Link>
          <Link
            to={`/teacher/attendance?group=${encodeURIComponent(g.id)}`}
            className="inline-flex h-9 items-center justify-center gap-1.5 rounded-lg bg-brand-600 text-[13px] font-semibold text-white transition-colors hover:bg-brand-700"
          >
            <ClipboardCheck className="h-4 w-4" /> Davomat
          </Link>
        </div>
      )}
    </div>
  )
}

function Info({ icon: Icon, label, value, wide }: { icon: typeof Clock; label: string; value: string; wide?: boolean }) {
  return (
    <div className={cn('flex min-w-0 items-start gap-2', wide && 'col-span-2')}>
      <Icon className="mt-0.5 h-4 w-4 shrink-0 text-faint" />
      <div className="min-w-0">
        <p className="text-[11px] text-faint">{label}</p>
        <p className="truncate font-semibold text-ink">{value}</p>
      </div>
    </div>
  )
}

function Pill({ cls, children }: { cls: string; children: ReactNode }) {
  return <span className={cn('rounded-md px-2 py-0.5 text-[11.5px] font-bold', cls)}>{children}</span>
}
