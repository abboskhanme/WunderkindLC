import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { ChevronLeft, ChevronRight, Download, Phone, Search, UserX, Clock3, CheckCircle2, BookOpen, MessageSquare, UserCheck, AlertTriangle, AlarmClock } from 'lucide-react'
import { getDailyAbsence, type AbsentStudent, type DailyAbsence } from '@/api/services/studentAttendance'
import { getTodayLessons } from '@/api/services/dashboard'
import type { TodayLessonMonitor } from '@/types'
import { NotTakenLessonsCard } from '@/components/attendance/NotTakenLessonsCard'
import { overdueCount } from '@/lib/attendanceWatch'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, cn, exportToCsv } from '@/lib/utils'
import { formatPhone } from '@/lib/numberFormat'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { PhoneCallModal, type CallOption } from '@/components/PhoneCallModal'
import { TelegramSendModal, type TelegramRecipient } from '@/components/messaging/TelegramSendModal'
import { AttendanceRedList } from './AttendanceRedList'
import { AttendancePeriodView } from './AttendancePeriodView'
import { tabFromUrl } from '@/lib/tabParam'

import { DateInput } from '@/components/ui/DateInput'
const ABSENCE_TABS = ['kunlik', 'davr', 'qizil'] as const
const ABSENCE_TAB_LABELS: Record<(typeof ABSENCE_TABS)[number], string> = {
  kunlik: 'Kunlik',
  davr: 'Davr tahlili',
  qizil: "Qizil ro'yxat",
}
type AbsenceTab = (typeof ABSENCE_TABS)[number]

type ChipFilter = 'absent' | 'late' | 'all'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

const today = () => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/** Sanani bir kunga siljitish ("yyyy-MM-dd" satrdan — TZ siljishisiz). */
function shiftDate(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, m - 1, d + days)
  return `${dt.getFullYear()}-${String(dt.getMonth() + 1).padStart(2, '0')}-${String(dt.getDate()).padStart(2, '0')}`
}

/** Bo'sh raqamlarni tashlab, bir xil raqamni faqat birinchi label bilan qoldiradi (PhoneCallModal uchun). */
function dedupeCallOptions(options: CallOption[]): CallOption[] {
  const seen = new Set<string>()
  return options.filter((o) => {
    if (!o.number || seen.has(o.number)) return false
    seen.add(o.number)
    return true
  })
}

/**
 * «O'quvchilar davomati» — uch tab: «Kunlik» (shu kun kelmagan/kechikkanlar), «Davr tahlili» (≤ 92 kun, guruhlar,
 * sabablar, sinflar — SPEC-backlog-2026-10-17 A1) va «Qizil ro'yxat» (ketma-ket sababsiz qoldirganlar, CRM Phase 3).
 * Boshlang'ich tab `?tab=davr` / `?tab=qizil` bilan ham ochiladi (`lib/tabParam.ts`).
 */
export function StudentAbsencePage() {
  const [tab, setTab] = useState<AbsenceTab>(() => tabFromUrl(ABSENCE_TABS, 'kunlik'))
  return (
    <div className="space-y-4">
      <div className="flex w-fit rounded-lg border border-slate-200 bg-white p-1">
        {ABSENCE_TABS.map((k) => (
          <button
            key={k}
            type="button"
            onClick={() => setTab(k)}
            className={cn(
              'h-8 rounded-md px-3 text-[13px] font-medium transition-colors',
              tab === k ? 'bg-brand-600 text-white' : 'text-slate-600 hover:bg-slate-50',
            )}
          >
            {ABSENCE_TAB_LABELS[k]}
          </button>
        ))}
      </div>
      {tab === 'kunlik' ? (
        <DailyAbsenceView />
      ) : tab === 'davr' ? (
        <AttendancePeriodView />
      ) : (
        <>
          <PageHeader
            title="Qizil ro'yxat"
            sub="Bitta guruhda ketma-ket SABABSIZ dars qoldirganlar. 3 ga yetganda tizim «Bog'lanish kerak» talabini o'zi ochadi."
          />
          <AttendanceRedList />
        </>
      )}
    </div>
  )
}

/** «Kunlik» — shu kunda darsga kelmagan/kechikkan o'quvchilar. */
function DailyAbsenceView() {
  const [date, setDate] = useState(today())
  const [data, setData] = useState<DailyAbsence | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  const [chip, setChip] = useState<ChipFilter>('absent')
  const [search, setSearch] = useState('')
  const [groupFilter, setGroupFilter] = useState('all')

  /** Qo'ng'iroq qilish — PhoneCallModal uchun tanlangan o'quvchi */
  const [callStudent, setCallStudent] = useState<AbsentStudent | null>(null)
  /** Ommaviy Telegram xabar uchun tanlangan o'quvchilar (studentId) + modal holati. */
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [msgOpen, setMsgOpen] = useState(false)

  /** «Davomat olinmagan guruhlar» — Darslar monitoringi bilan BITTA manba (`today-lessons`). */
  const [lessons, setLessons] = useState<TodayLessonMonitor[]>([])
  const [lessonsError, setLessonsError] = useState('')
  const { can } = usePerm()

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sana o'zgarganda ma'lumotni qayta yuklash (maqsadli)
    setLoading(true)
    setError('')
    setSelected(new Set())
    getDailyAbsence(date)
      .then(setData)
      .catch((err) => setError(apiErrorMessage(err, "Davomatni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [date])

  useEffect(() => {
    let alive = true
    getTodayLessons(date)
      .then((t) => { if (alive) { setLessons(t.lessons); setLessonsError('') } })
      .catch((err) => { if (alive) setLessonsError(apiErrorMessage(err, "Darslar ro'yxatini yuklab bo'lmadi")) })
    return () => { alive = false }
  }, [date])

  const isToday = date === today()

  // BUGUN ko'rilayotganda sonlar har daqiqada jimgina yangilanadi (real vaqtga yaqin — uchrashuv talabi).
  // Fon o'qish: xato asosiy yuklashda ko'rsatiladi, bu yerda yutiladi (`error-visibility.md` §4).
  useEffect(() => {
    if (!isToday) return
    const t = window.setInterval(() => {
      getDailyAbsence(date)
        .then(setData)
        .catch(() => {})
      // Dars bosqichi (kechikkanmi) soatga bog'liq — u ham har daqiqada yangilanadi.
      getTodayLessons(date)
        .then((t) => setLessons(t.lessons))
        .catch(() => {})
    }, 60_000)
    return () => window.clearInterval(t)
  }, [date, isToday])

  const groups = useMemo(() => {
    const set = new Set<string>()
    ;(data?.rows ?? []).forEach((r) => set.add(r.groupName))
    return Array.from(set).sort((a, b) => a.localeCompare(b))
  }, [data])

  const filtered = (data?.rows ?? []).filter((r) => {
    if (chip === 'absent' && r.isLate) return false
    if (chip === 'late' && !r.isLate) return false
    if (groupFilter !== 'all' && r.groupName !== groupFilter) return false
    const q = search.trim().toLowerCase()
    if (q && !r.fullName.toLowerCase().includes(q) && !r.groupName.toLowerCase().includes(q)) return false
    return true
  })

  // Tanlash (ommaviy Telegram xabar) — o'quvchi bir nechta guruhda bo'lsa ham bitta studentId.
  const filteredStudentIds = useMemo(() => {
    const seen = new Set<string>()
    for (const r of filtered) seen.add(r.studentId)
    return seen
  }, [filtered])
  const allSelected = filteredStudentIds.size > 0 && [...filteredStudentIds].every((id) => selected.has(id))

  const toggleOne = (studentId: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(studentId)) next.delete(studentId)
      else next.add(studentId)
      return next
    })
  const toggleAll = () =>
    setSelected((prev) => {
      if (allSelected) {
        const next = new Set(prev)
        filteredStudentIds.forEach((id) => next.delete(id))
        return next
      }
      return new Set([...prev, ...filteredStudentIds])
    })

  // Tanlangan o'quvchilar (studentId bo'yicha takrorlanmas) — Telegram xabar oynasi uchun.
  const msgRecipients: TelegramRecipient[] = useMemo(() => {
    const byId = new Map<string, TelegramRecipient>()
    for (const r of filtered) {
      if (!selected.has(r.studentId) || byId.has(r.studentId)) continue
      byId.set(r.studentId, { id: r.studentId, fullName: r.fullName })
    }
    return [...byId.values()]
  }, [filtered, selected])

  const handleExport = () => {
    exportToCsv(
      `davomat_${date}.csv`,
      ['O\'quvchi', 'Guruh', 'Sabab', 'Ota-ona', 'Telefon'],
      filtered.map((r) => [r.fullName, r.groupName, r.reasonName, r.parentFullName, r.parentPhone]),
    )
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title="O'quvchilar davomati"
        sub="Shu kunda darsga kelmagan/kechikkan o'quvchilar — ota-onaga darrov qo'ng'iroq qilish uchun."
        actions={
          <div className="flex items-center gap-2">
            {/* Yuborish `messages.broadcast` ruxsatini talab qiladi — ruxsatsizga tugma ko'rsatilmaydi. */}
            {can('messages.broadcast', 'create') && (
              <Button onClick={() => setMsgOpen(true)} disabled={selected.size === 0}>
                <MessageSquare className="h-4 w-4" /> Telegram xabar{selected.size > 0 ? ` (${selected.size})` : ''}
              </Button>
            )}
            <Button variant="secondary" onClick={handleExport} disabled={filtered.length === 0}>
              <Download className="h-4 w-4" /> CSV
            </Button>
          </div>
        }
      />

      {/* Sana boshqaruvi */}
      <Card>
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            onClick={() => setDate((d) => shiftDate(d, -1))}
            className="rounded-lg border border-slate-200 p-2 text-slate-500 transition-colors hover:bg-slate-50"
            title="Oldingi kun"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <DateInput
            value={date}
            max={today()}
            onChange={(e) => setDate(e.target.value)}
            className={control}
          />
          <button
            type="button"
            disabled={isToday}
            onClick={() => setDate((d) => shiftDate(d, 1))}
            className="rounded-lg border border-slate-200 p-2 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
            title="Keyingi kun"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          {!isToday && (
            <Button variant="secondary" onClick={() => setDate(today())}>
              Bugun
            </Button>
          )}
        </div>
      </Card>

      {error && (
        <div className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-medium text-red-600">
          {error}
        </div>
      )}

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* Statistika */}
          <div className="grid grid-cols-2 gap-4 sm:grid-cols-4 xl:grid-cols-7">
            <StatCard label="Keldi" value={data?.presentCount ?? 0} icon={UserCheck} iconBg="bg-emerald-50" iconColor="text-emerald-600" />
            <StatCard label="Kelmadi" value={data?.absentCount ?? 0} icon={UserX} iconBg="bg-red-50" iconColor="text-red-600" />
            <StatCard label="Sababsiz" value={data?.unexcusedCount ?? 0} icon={AlertTriangle} iconBg="bg-red-50" iconColor="text-red-700" />
            <StatCard label="Kechikdi" value={data?.lateCount ?? 0} icon={Clock3} iconBg="bg-amber-50" iconColor="text-amber-600" />
            <StatCard label="Davomat olindi" value={data?.markedStudents ?? 0} icon={CheckCircle2} iconBg="bg-emerald-50" iconColor="text-emerald-600" />
            <StatCard label="Darsi o'tilgan guruh" value={data?.conductedGroups ?? 0} icon={BookOpen} iconBg="bg-brand-50" iconColor="text-brand-600" />
            <StatCard
              label="Davomat olinmagan"
              value={overdueCount(lessons)}
              icon={AlarmClock}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint="Dars boshlangan, davomat yo'q"
            />
          </div>

          <NotTakenLessonsCard
            lessons={lessons}
            error={lessonsError}
            monitorHref={can('classes.notAttended', 'view') ? '/admin/nazorat/davomat-qilinmagan' : null}
          />

          {(data?.conductedGroups ?? 0) === 0 ? (
            <Card className="py-10 text-center text-slate-400">
              Bu kunda hali hech qaysi guruhda davomat olinmagan.
            </Card>
          ) : (data?.absentCount ?? 0) === 0 && (data?.lateCount ?? 0) === 0 ? (
            <Card className="py-10 text-center">
              <p className="text-lg font-semibold text-emerald-600">Bu kunda kelmagan o'quvchi yo'q 🎉</p>
              <p className="mt-1 text-sm text-slate-400">
                Davomat olingan guruhlar: {data?.conductedGroups ?? 0}
              </p>
            </Card>
          ) : (
            <Card tight>
              {/* Filtrlar */}
              <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 px-4 py-3">
                <div className="tabs">
                  <button
                    type="button"
                    onClick={() => setChip('absent')}
                    className={cn('tab', chip === 'absent' && 'active')}
                  >
                    Kelmaganlar ({data?.absentCount ?? 0})
                  </button>
                  <button
                    type="button"
                    onClick={() => setChip('late')}
                    className={cn('tab', chip === 'late' && 'active')}
                  >
                    Kechikkanlar ({data?.lateCount ?? 0})
                  </button>
                  <button
                    type="button"
                    onClick={() => setChip('all')}
                    className={cn('tab', chip === 'all' && 'active')}
                  >
                    Hammasi
                  </button>
                </div>
                <div className="search-inline">
                  <Search className="h-4 w-4 shrink-0 text-slate-400" />
                  <input
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Ism yoki guruh bo'yicha qidirish..."
                  />
                </div>
                <select value={groupFilter} onChange={(e) => setGroupFilter(e.target.value)} className={control}>
                  <option value="all">Barcha guruhlar</option>
                  {groups.map((g) => (
                    <option key={g} value={g}>
                      {g}
                    </option>
                  ))}
                </select>
                <span className="ml-auto text-sm text-slate-400">{filtered.length} ta</span>
              </div>

              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th className="w-8">
                        <input
                          type="checkbox"
                          checked={allSelected}
                          onChange={toggleAll}
                          title="Hammasini tanlash"
                          className="h-4 w-4 cursor-pointer accent-brand-500"
                        />
                      </th>
                      <th>O'quvchi</th>
                      <th>Guruh</th>
                      <th>Dars vaqti</th>
                      <th>Sabab</th>
                      <th>Telefon</th>
                      <th className="text-right">Amallar</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((r) => (
                      <tr key={`${r.studentId}-${r.groupId}`}>
                        <td>
                          <input
                            type="checkbox"
                            checked={selected.has(r.studentId)}
                            onChange={() => toggleOne(r.studentId)}
                            className="h-4 w-4 cursor-pointer accent-brand-500"
                          />
                        </td>
                        <td>
                          <div>
                            <Link
                              to={`/admin/students/${r.studentId}`}
                              className="font-medium text-slate-800 no-underline hover:underline"
                            >
                              {r.fullName}
                            </Link>
                            <div className="text-xs text-slate-400">{r.parentFullName}</div>
                          </div>
                        </td>
                        <td>
                          <Badge tone="violet">{r.groupName}</Badge>
                          <div className="mt-1 text-xs text-slate-400">{r.courseName}</div>
                        </td>
                        <td className="font-mono text-slate-600">
                          {r.startTime}–{r.endTime}
                          <div className="font-sans text-xs text-slate-400">{r.teacherName}</div>
                        </td>
                        <td>
                          <Badge tone={r.isLate ? 'amber' : 'red'}>{r.reasonName}</Badge>
                        </td>
                        <td className="whitespace-nowrap font-mono text-slate-600">{formatPhone(r.parentPhone)}</td>
                        <td className="text-right">
                          <button
                            type="button"
                            title="Qo'ng'iroq qilish"
                            onClick={() => setCallStudent(r)}
                            className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                          >
                            <Phone className="h-4 w-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                    {filtered.length === 0 && (
                      <tr>
                        <td colSpan={7} className="px-4 py-12 text-center text-slate-400">
                          Filtrga mos o'quvchi topilmadi
                        </td>
                      </tr>
                    )}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </>
      )}

      <TelegramSendModal open={msgOpen} onClose={() => setMsgOpen(false)} recipients={msgRecipients} />

      <PhoneCallModal
        open={!!callStudent}
        onClose={() => setCallStudent(null)}
        title={callStudent?.fullName}
        numbers={
          callStudent
            ? dedupeCallOptions([
                { label: "O'z raqami", number: callStudent.phone ?? '' },
                { label: 'Ota-ona', number: callStudent.parentPhone },
                { label: 'Otasi', number: callStudent.fatherPhone ?? '' },
                { label: 'Onasi', number: callStudent.motherPhone ?? '' },
              ])
            : []
        }
      />
    </div>
  )
}
