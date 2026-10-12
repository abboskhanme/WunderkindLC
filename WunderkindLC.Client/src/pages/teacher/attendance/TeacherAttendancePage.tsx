import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import {
  AlertCircle,
  AlertTriangle,
  BookOpenCheck,
  CalendarX2,
  Check,
  CheckCheck,
  ChevronLeft,
  ChevronRight,
  Clock,
  DoorOpen,
  Loader2,
  X,
} from 'lucide-react'
import type { AbsenceReason } from '@/types'
import type { GroupJournal } from '@/api/services/journal'
import {
  bulkTeacherAttendance,
  getTeacherGroupJournal,
  getTeacherMeta,
  getTeacherOverview,
  getTeacherSchedule,
  type TeacherLesson,
  type TeacherOverview,
} from '@/api/services/teacher'
import { apiErrorMessage, cn } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import {
  addDays,
  dayMonthWeekday,
  lessonPhase,
  nowHm,
  PHASE_STYLE,
  timeRange,
  weekdayIndex,
} from '@/lib/teacherPortal'
import { Avatar, EmptyState, PhaseChip, SkeletonRows, TCard, TPage } from '@/components/teacher/TeacherUi'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'

type Mark = 'present' | 'late' | 'absent' | 'none'
interface RowState {
  mark: Mark
  reasonId: string | null
}

/**
 * TEZKOR DAVOMAT — telefon uchun asosiy ish ekrani: sana → dars → har o'quvchiga
 * «Keldi / Kech / Yo'q» → «Saqlash» → TASDIQLASH oynasi. O'quvchi default BELGILANMAGAN turadi
 * (avtomatik "Keldi" yo'q) va HAMMASI belgilanmaguncha saqlab bo'lmaydi — aks holda jurnal
 * qoidasi belgilanmaganlarni jimgina «keldi» deb o'qirdi.
 *
 * Yozish mavjud `POST /teacher/journal/bulk-attendance` orqali (guruh jurnalidagi «hammaga davomat»
 * bilan bir xil yo'l): server jurnal siyosati, o'rinbosar sanasi va to'lov darvozasini O'ZI tekshiradi.
 * Baho/uy vazifa/xulq TEGILMAYDI.
 *
 * ⚠️ Faqat O'ZGARGAN qatorlar yuboriladi: server har «kelmadi» yozuvida ota-onaga avto-xabar
 * yuboradi — hammasini qayta yuborsak, har saqlashda xabar takrorlanardi.
 */
export function TeacherAttendancePage() {
  const [params, setParams] = useSearchParams()
  const today = todayIso()
  const rawDate = params.get('date') ?? ''
  const date = /^\d{4}-\d{2}-\d{2}$/.test(rawDate) && rawDate <= today ? rawDate : today
  const groupParam = params.get('group') ?? ''

  const [overview, setOverview] = useState<TeacherOverview | null>(null)
  /** Jadvaldan yuklangan darslar — QAYSI sana uchun ekanligi bilan (sana almashsa eskisi ko'rinmasin). */
  const [loaded, setLoaded] = useState<{ date: string; lessons: TeacherLesson[] } | null>(null)
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  /** Yuklangan jurnal + undan hisoblangan boshlang'ich holat; `key` = "guruh|sana". */
  const [sheet, setSheet] = useState<{
    key: string
    journal: GroupJournal
    initial: Record<string, RowState>
  } | null>(null)
  const [rows, setRows] = useState<Record<string, RowState>>({})
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [savedKey, setSavedKey] = useState('')
  /** Saqlashdan oldingi tasdiqlash oynasi + uning ichidagi xato. */
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [confirmError, setConfirmError] = useState<string | null>(null)
  const now = nowHm()

  const setQuery = (next: { date?: string; group?: string | null }) => {
    const p = new URLSearchParams(params)
    if (next.date !== undefined) p.set('date', next.date)
    if (next.group !== undefined) {
      if (next.group) p.set('group', next.group)
      else p.delete('group')
    }
    setError(null)
    setParams(p, { replace: true })
  }

  // Guruhlar + sabablar — bir marta.
  useEffect(() => {
    getTeacherOverview()
      .then(setOverview)
      .catch((err) => setError(apiErrorMessage(err, "Guruhlarni yuklab bo'lmadi")))
    getTeacherMeta()
      .then((m) => setReasons(m?.absenceReasons ?? []))
      .catch(() => {}) // sabablarsiz ham ishlaydi: server standart "Sababsiz" ni qo'yadi
  }, [])

  // Shu kundagi darslar (jadval ruxsati bo'lsa — serverdan, ko'chirish va o'rinbosarlik bilan).
  useEffect(() => {
    if (!overview?.canSchedule) return
    let alive = true
    getTeacherSchedule(date, date)
      .then((ls) => alive && setLoaded({ date, lessons: ls }))
      .catch((err) => {
        if (!alive) return
        setLoaded({ date, lessons: [] })
        setError(apiErrorMessage(err, "Darslarni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [overview, date])

  // Jadval ruxsati bo'lmasa — guruh kunlaridan taxminiy ro'yxat (aniq tekshiruv baribir jurnal ustunlarida).
  const lessons = useMemo<TeacherLesson[] | null>(() => {
    if (!overview) return null
    if (overview.canSchedule) return loaded?.date === date ? loaded.lessons : null
    const wd = weekdayIndex(date)
    return overview.groups
      .filter((g) => g.days.includes(wd))
      .map((g) => ({
        groupId: g.id, groupName: g.name, courseName: g.courseName, date,
        startTime: g.startTime, endTime: g.endTime, room: g.room, studentCount: g.active + g.trial,
        isSubstitute: g.isSubstitute, substitutedBy: '', moved: false, conducted: false, attendanceTaken: false,
      }))
  }, [overview, loaded, date])

  // Tanlangan dars: manzildagi guruh > davomati kutilayotgan birinchi dars > birinchi dars.
  const selected = useMemo(() => {
    if (!lessons) return null
    if (groupParam) return lessons.find((l) => l.groupId === groupParam) ?? null
    const open = lessons.filter((l) => !l.substitutedBy)
    // "Davomat kutilayotgan" — davomati OLINMAGAN dars (yagona ta'rif: `attendanceTaken`, `conducted` EMAS —
    // bitta baho darsni "o'tildi" qiladi, lekin davomat olinmagan bo'ladi).
    return open.find((l) => !l.attendanceTaken) ?? open[0] ?? null
  }, [lessons, groupParam])
  const groupNotOnDate = !!groupParam && lessons !== null && !lessons.some((l) => l.groupId === groupParam)
  const paramGroup = overview?.groups.find((g) => g.id === groupParam)

  const reasonById = useMemo(() => new Map(reasons.map((r) => [r.id, r])), [reasons])
  const absentReasons = useMemo(() => reasons.filter((r) => !r.isLate), [reasons])
  const lateReasons = useMemo(() => reasons.filter((r) => r.isLate), [reasons])

  const sheetKey = selected && !selected.substitutedBy ? `${selected.groupId}|${date}` : ''

  /** Jurnaldan boshlang'ich holatni hisoblaydi va ekranga qo'yadi. */
  const applyJournal = useCallback(
    (groupId: string, j: GroupJournal) => {
      // «Keldi» oldindan FAQAT davomat TO'LIQ olingan darsda (`attendanceTakenDates` — server `AttendanceRules`:
      // davomat olingan + belgisiz = keldi). "O'tildi" (`conductedDates`) yetmaydi: bitta baho qo'yilgan darsda
      // hamma o'quvchi jimgina «Keldi» bo'lib to'lardi va «Saqlash» ularni haqiqatan «keldi» qilib yozardi.
      const takenNow = (j.attendanceTakenDates ?? []).includes(date)
      const init: Record<string, RowState> = {}
      const ui: Record<string, RowState> = {}
      for (const s of j.students) {
        if (s.status === 'frozen') continue
        const e = j.entries.find((x) => x.studentId === s.studentId && x.date === date)
        let st: RowState = { mark: 'none', reasonId: null }
        if (e?.reasonId) st = { mark: reasonById.get(e.reasonId)?.isLate ? 'late' : 'absent', reasonId: e.reasonId }
        else if (e?.present || e?.grade != null) st = { mark: 'present', reasonId: null }
        else if (takenNow && (!s.presentDefaultFrom || date >= s.presentDefaultFrom)) st = { mark: 'present', reasonId: null }
        init[s.studentId] = st
        // Hali belgilanmagan o'quvchi BELGILANMAGAN bo'lib turadi — o'qituvchi har birini o'zi belgilaydi
        // (foydalanuvchi talabi: avtomatik "Keldi" xato davomatga olib kelardi).
        ui[s.studentId] = st
      }
      setSheet({ key: `${groupId}|${date}`, journal: j, initial: init })
      setRows(ui)
    },
    [date, reasonById],
  )

  useEffect(() => {
    if (!selected || selected.substitutedBy) return
    let alive = true
    getTeacherGroupJournal(selected.groupId, date.slice(0, 7))
      .then((j) => alive && applyJournal(selected.groupId, j))
      .catch((err) => alive && setError(apiErrorMessage(err, "Jurnalni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [selected, date, applyJournal])

  // Boshqa dars/sana tanlangan bo'lsa eski jurnal ko'rsatilmaydi (yuklanguncha skelet).
  const journal = sheet && sheet.key === sheetKey ? sheet.journal : null
  const initial = journal ? sheet!.initial : {}
  const journalLoading = !!sheetKey && !journal
  const saved = savedKey === sheetKey && !!sheetKey

  const students = useMemo(
    () => (journal?.students ?? []).filter((s) => s.status !== 'frozen'),
    [journal],
  )
  const isLessonDate = !!journal?.columns.some((c) => c.date === date)
  /** Davomat shu kunga TO'LIQ olingan (`attendanceTakenDates`) — "o'tildi" (`conductedDates`) EMAS. */
  const taken = !!journal?.attendanceTakenDates?.includes(date)
  const blocked = (memberStart: string) => !!memberStart && date < memberStart

  const changed = students.filter((s) => {
    if (blocked(s.memberStart)) return false
    const a = rows[s.studentId]
    const b = initial[s.studentId]
    return a && b && a.mark !== 'none' && (a.mark !== b.mark || (a.mark !== 'present' && a.reasonId !== b.reasonId))
  })
  const counts = students.reduce(
    (acc, s) => {
      if (blocked(s.memberStart)) return acc
      const m = rows[s.studentId]?.mark ?? 'none'
      acc[m] = (acc[m] ?? 0) + 1
      return acc
    },
    {} as Record<Mark, number>,
  )

  /**
   * Belgilanmay qolgan o'quvchilar soni. ⚠️ Ular bor ekan saqlab BO'LMAYDI: jurnal qoidasi bo'yicha
   * davomat olingan darsda yozuvi yo'q o'quvchi «keldi» hisoblanadi (`StudentJournalBuilder`) —
   * ya'ni qisman saqlash qolganlarni JIMGINA «keldi» qilib qo'yardi.
   */
  const unmarked = counts.none ?? 0
  const canSave = !saving && changed.length > 0 && unmarked === 0

  const setMark = (studentId: string, mark: Mark) => {
    setSavedKey('')
    setRows((prev) => {
      const cur = prev[studentId]
      if (cur?.mark === mark && initial[studentId]?.mark === 'none')
        return { ...prev, [studentId]: { mark: 'none', reasonId: null } }
      if (mark === 'present') return { ...prev, [studentId]: { mark, reasonId: null } }
      const pool = mark === 'late' ? lateReasons : absentReasons
      const keep = cur?.mark === mark && cur.reasonId ? cur.reasonId : null
      const preferred = pool.find((r) => /sababsiz/i.test(r.name)) ?? pool[0]
      return { ...prev, [studentId]: { mark, reasonId: keep ?? preferred?.id ?? null } }
    })
  }
  const setReason = (studentId: string, reasonId: string) =>
    setRows((prev) => ({ ...prev, [studentId]: { ...prev[studentId], reasonId } }))
  /** Faqat BELGILANMAGANLARNI "Keldi" qiladi — qo'lda belgilanganlar tegilmaydi. */
  const restPresent = () => {
    setSavedKey('')
    setRows((prev) =>
      Object.fromEntries(
        Object.entries(prev).map(([id, r]) => [id, r.mark === 'none' ? { mark: 'present' as Mark, reasonId: null } : r]),
      ),
    )
  }

  const save = async () => {
    if (!journal || changed.length === 0 || unmarked > 0) return
    setSaving(true)
    setConfirmError(null)
    // Bir xil (holat, sabab) dagilar bitta so'rovda.
    const batches = new Map<string, { absent: boolean; reasonId: string | null; ids: string[] }>()
    for (const s of changed) {
      const r = rows[s.studentId]
      const key = r.mark === 'present' ? 'present' : `${r.mark}|${r.reasonId ?? ''}`
      const b = batches.get(key) ?? { absent: r.mark !== 'present', reasonId: r.mark === 'present' ? null : r.reasonId, ids: [] }
      b.ids.push(s.studentId)
      batches.set(key, b)
    }
    try {
      for (const b of batches.values())
        await bulkTeacherAttendance(journal.group.id, journal.group.courseId, 1, b.ids, date, b.absent, b.reasonId)
      setSavedKey(sheetKey)
      setConfirmOpen(false)
      // Saqlangan dars ekranda QOLADI: aks holda "birinchi belgilanmagan dars" qoidasi sahifani
      // o'zi keyingi guruhga o'tkazib yuborardi va o'qituvchi natijani ko'rmasdi.
      if (!groupParam) setQuery({ group: journal.group.id })
      setLoaded((prev) =>
        prev && {
          ...prev,
          lessons: prev.lessons.map((l) =>
            l.groupId === journal.group.id ? { ...l, conducted: true, attendanceTaken: true } : l,
          ),
        },
      )
    } catch (err) {
      // Oyna YOPILMAYDI — sabab uning ichida ko'rinadi (error-visibility.md §2).
      setConfirmError(apiErrorMessage(err, "Davomatni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
      // Qisman saqlangan bo'lishi mumkin — ekran har holda serverdagi holatni ko'rsatsin.
      const gid = journal.group.id
      await getTeacherGroupJournal(gid, date.slice(0, 7))
        .then((j) => applyJournal(gid, j))
        .catch((err) => setError(apiErrorMessage(err, "Jurnalni yangilab bo'lmadi")))
    }
  }

  const lessonList = lessons ?? []

  return (
    <TPage
      title="Davomat"
      sub={dayMonthWeekday(date)}
      actions={
        <div className="flex w-full items-center gap-1.5 sm:w-auto">
          <button
            type="button"
            onClick={() => setQuery({ date: addDays(date, -1), group: groupParam || null })}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600"
            aria-label="Oldingi kun"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <input
            type="date"
            value={date}
            max={today}
            onChange={(e) => e.target.value && setQuery({ date: e.target.value, group: groupParam || null })}
            className="h-9 min-w-0 flex-1 rounded-lg border border-line bg-white px-2.5 text-[13.5px] font-semibold text-ink outline-none focus:border-brand-600 sm:w-40 sm:flex-none"
          />
          <button
            type="button"
            disabled={date >= today}
            onClick={() => setQuery({ date: addDays(date, 1), group: groupParam || null })}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600 disabled:opacity-40"
            aria-label="Keyingi kun"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          {date !== today && (
            <button
              type="button"
              onClick={() => setQuery({ date: today, group: groupParam || null })}
              className="h-9 shrink-0 rounded-lg border border-brand-600/40 px-3 text-[13px] font-semibold text-brand-600 hover:bg-brand-50"
            >
              Bugun
            </button>
          )}
        </div>
      }
    >
      {error && (
        <div className="mb-3 flex items-start gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-[13px] text-red-700">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" /> <span>{error}</span>
        </div>
      )}

      {/* ── Shu kundagi darslar ── */}
      {lessons === null ? (
        <SkeletonRows rows={1} className="mb-3" />
      ) : lessonList.length === 0 ? (
        <TCard className="mb-3">
          <EmptyState
            icon={CalendarX2}
            title={groupNotOnDate && paramGroup ? `${paramGroup.name}: bu kunda dars yo'q` : "Bu kunda darsingiz yo'q"}
            text="Boshqa kunni tanlang. Dars ko'chirilgan bo'lsa, u yangi kunida ko'rinadi."
          />
        </TCard>
      ) : (
        <div className="-mx-3 mb-3 flex gap-2 overflow-x-auto px-3 pb-1 sm:mx-0 sm:flex-wrap sm:px-0">
          {lessonList.map((l) => {
            const active = selected?.groupId === l.groupId
            const phase = lessonPhase(l, today, now)
            return (
              <button
                key={l.groupId}
                type="button"
                onClick={() => setQuery({ group: l.groupId })}
                className={cn(
                  'flex min-w-[190px] max-w-[260px] shrink-0 items-center gap-2.5 rounded-xl border bg-white px-3 py-2.5 text-left transition-colors',
                  active ? 'border-brand-600 ring-2 ring-brand-100' : 'border-line hover:border-brand-300',
                )}
              >
                <span className={cn('h-2.5 w-2.5 shrink-0 rounded-full', PHASE_STYLE[phase].dot)} />
                <span className="min-w-0">
                  <span className="block truncate text-[14px] font-bold text-ink">{l.groupName}</span>
                  <span className="block truncate text-[12px] text-mute">
                    {timeRange(l.startTime, l.endTime) || 'Vaqt yo\'q'} · {l.attendanceTaken ? 'belgilangan' : l.substitutedBy ? "o'rinbosar" : 'belgilanmagan'}
                  </span>
                </span>
              </button>
            )
          })}
        </div>
      )}

      {groupNotOnDate && lessonList.length > 0 && (
        <p className="mb-3 flex items-center gap-2 rounded-lg bg-amber-50 px-3 py-2 text-[13px] text-amber-800">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          {paramGroup ? `«${paramGroup.name}» guruhida` : 'Tanlangan guruhda'} bu kunda dars yo'q — boshqa darsni tanlang.
        </p>
      )}

      {/* ── Tanlangan dars ── */}
      {selected && selected.substitutedBy ? (
        <TCard>
          <EmptyState
            icon={AlertTriangle}
            title={`Bu darsni ${selected.substitutedBy} o'tadi`}
            text="Shu kunga o'rinbosar tayinlangan — davomatni o'rinbosar o'qituvchi belgilaydi."
          />
        </TCard>
      ) : selected ? (
        <TCard
          tight
          title={selected.groupName}
          sub={
            <span className="flex flex-wrap items-center gap-x-3 gap-y-1">
              <PhaseChip phase={lessonPhase(selected, today, now)} />
              <span>{selected.courseName}</span>
              {selected.startTime && (
                <span className="inline-flex items-center gap-1">
                  <Clock className="h-3 w-3" /> {timeRange(selected.startTime, selected.endTime)}
                </span>
              )}
              {selected.room && (
                <span className="inline-flex items-center gap-1">
                  <DoorOpen className="h-3 w-3" /> {selected.room}
                </span>
              )}
            </span>
          }
          actions={
            <Link
              to={`/teacher/groups/${selected.groupId}`}
              className="hidden items-center gap-1 rounded-lg border border-line px-2.5 py-1.5 text-[12.5px] font-semibold text-brand-600 hover:bg-brand-50 sm:inline-flex"
            >
              <BookOpenCheck className="h-4 w-4" /> Jurnal
            </Link>
          }
        >
          {journalLoading && !journal ? (
            <div className="p-4">
              <SkeletonRows rows={4} />
            </div>
          ) : !journal ? null : !isLessonDate ? (
            <EmptyState
              icon={CalendarX2}
              title="Jurnalda bu kunga dars ustuni yo'q"
              text="Guruh jadvali bo'yicha bu kunda dars bo'lmaydi (yoki dars boshqa kunga ko'chirilgan)."
            />
          ) : students.length === 0 ? (
            <EmptyState icon={CalendarX2} title="O'quvchi yo'q" text="Guruhda davomat qilinadigan faol o'quvchi yo'q." />
          ) : (
            <>
              <div className="flex flex-wrap items-center gap-2 border-b border-line-soft px-4 py-2.5">
                {!taken ? (
                  <p className="flex-1 text-[12.5px] text-amber-700">
                    Davomat hali olinmagan — har bir o'quvchini belgilang.
                  </p>
                ) : (
                  <p className="flex-1 text-[12.5px] text-emerald-700">Davomat saqlangan. O'zgartirsangiz — qayta saqlang.</p>
                )}
                {(counts.none ?? 0) > 0 && (
                  <button
                    type="button"
                    onClick={restPresent}
                    className="inline-flex h-8 items-center gap-1 rounded-lg border border-line bg-white px-2.5 text-[12.5px] font-semibold text-emerald-700 hover:bg-emerald-50"
                  >
                    <CheckCheck className="h-4 w-4" /> Qolganlari keldi ({counts.none})
                  </button>
                )}
              </div>

              <ul className="divide-y divide-line-soft">
                {students.map((s, i) => {
                  const r = rows[s.studentId] ?? { mark: 'none', reasonId: null }
                  const isBlocked = blocked(s.memberStart)
                  const pool = r.mark === 'late' ? lateReasons : absentReasons
                  return (
                    <li key={s.studentId} className={cn('px-3 py-2.5 sm:px-4', isBlocked && 'opacity-60')}>
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
                        <div className="flex min-w-0 flex-1 items-center gap-2.5">
                          <span className="w-5 shrink-0 text-right text-[12px] font-semibold text-faint">{i + 1}</span>
                          <Avatar name={s.fullName} photoUrl={s.photoUrl} size={34} />
                          <div className="min-w-0">
                            <p className="truncate text-[14px] font-bold text-ink">{s.fullName}</p>
                            <p className="truncate text-[11.5px] text-mute">
                              {isBlocked
                                ? `Guruhga ${s.memberStart} dan qo'shilgan`
                                : s.status === 'trial'
                                  ? 'Sinov darsida'
                                  : s.debtMonths >= 1
                                    ? `To'lov kutilmoqda (${s.debtMonths} oy)`
                                    : 'Faol'}
                            </p>
                          </div>
                        </div>
                        {!isBlocked && (
                          <div className="grid grid-cols-3 gap-1 sm:flex sm:w-auto">
                            <MarkButton on={r.mark === 'present'} tone="green" onClick={() => setMark(s.studentId, 'present')} icon={Check} label="Keldi" />
                            {lateReasons.length > 0 ? (
                              <MarkButton on={r.mark === 'late'} tone="amber" onClick={() => setMark(s.studentId, 'late')} icon={Clock} label="Kech" />
                            ) : (
                              <span className="hidden sm:block" />
                            )}
                            <MarkButton on={r.mark === 'absent'} tone="red" onClick={() => setMark(s.studentId, 'absent')} icon={X} label="Yo'q" />
                          </div>
                        )}
                      </div>
                      {(r.mark === 'absent' || r.mark === 'late') && pool.length > 1 && (
                        <div className="mt-2 flex flex-wrap gap-1.5 sm:pl-[70px]">
                          {pool.map((reason) => (
                            <button
                              key={reason.id}
                              type="button"
                              onClick={() => setReason(s.studentId, reason.id)}
                              className={cn(
                                'rounded-md border px-2.5 py-1 text-[12px] font-semibold transition-colors',
                                r.reasonId === reason.id
                                  ? r.mark === 'late'
                                    ? 'border-amber-400 bg-amber-50 text-amber-700'
                                    : 'border-red-300 bg-red-50 text-red-600'
                                  : 'border-line bg-white text-mute hover:text-ink',
                              )}
                            >
                              {reason.name}
                            </button>
                          ))}
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>

              {/* ── Saqlash paneli (skrollda pastda yopishib turadi) ── */}
              <div className="sticky bottom-0 z-10 flex flex-wrap items-center gap-2 rounded-b-xl border-t border-line bg-white/95 px-3 py-2.5 backdrop-blur sm:px-4">
                <div className="flex flex-1 flex-wrap items-center gap-x-3 gap-y-1 text-[12.5px] font-semibold">
                  <span className="text-emerald-700">✓ {counts.present ?? 0} keldi</span>
                  {lateReasons.length > 0 && <span className="text-amber-700">⏱ {counts.late ?? 0} kech</span>}
                  <span className="text-red-600">✕ {counts.absent ?? 0} yo'q</span>
                  {unmarked > 0 && <span className="text-amber-700">○ {unmarked} ta belgilanmagan — saqlash uchun hammasini belgilang</span>}
                  {saved && changed.length === 0 && <span className="text-brand-600">Saqlandi ✓</span>}
                </div>
                <button
                  type="button"
                  onClick={() => {
                    setConfirmError(null)
                    setConfirmOpen(true)
                  }}
                  disabled={!canSave}
                  title={unmarked > 0 ? `Avval barcha o'quvchilarni belgilang (${unmarked} ta qoldi)` : undefined}
                  className="inline-flex h-10 min-w-[140px] items-center justify-center gap-1.5 rounded-lg bg-brand-600 px-4 text-[14px] font-bold text-white transition-colors hover:bg-brand-700 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                  {changed.length === 0 ? (taken ? 'Saqlangan' : 'Saqlash') : `Saqlash (${changed.length})`}
                </button>
              </div>
            </>
          )}
        </TCard>
      ) : null}

      {/* ── Saqlashdan oldin TASDIQLASH (foydalanuvchi talabi) ── */}
      {journal && (
        <Modal
          open={confirmOpen}
          onClose={() => !saving && setConfirmOpen(false)}
          title="Davomatni tasdiqlang"
          size="sm"
          footer={
            <>
              <Button variant="secondary" className="h-10 px-4 text-[13.5px]" disabled={saving} onClick={() => setConfirmOpen(false)}>
                Bekor qilish
              </Button>
              <Button className="h-10 px-4 text-[13.5px] font-bold" disabled={!canSave} onClick={save}>
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                Tasdiqlash va saqlash
              </Button>
            </>
          }
        >
          <p className="text-[13.5px] text-mute">
            <b className="text-ink">{journal.group.name}</b> · {dayMonthWeekday(date)}
          </p>
          <div className={cn('mt-3 grid gap-2 text-center', lateReasons.length > 0 ? 'grid-cols-3' : 'grid-cols-2')}>
            <ConfirmStat label="Keldi" value={counts.present ?? 0} cls="bg-emerald-50 text-emerald-700" />
            {lateReasons.length > 0 && <ConfirmStat label="Kech" value={counts.late ?? 0} cls="bg-amber-50 text-amber-700" />}
            <ConfirmStat label="Kelmadi" value={counts.absent ?? 0} cls="bg-red-50 text-red-600" />
          </div>

          {students.some((st) => rows[st.studentId]?.mark === 'absent' || rows[st.studentId]?.mark === 'late') && (
            <div className="mt-3">
              <p className="mb-1.5 text-[11.5px] font-bold uppercase tracking-wide text-faint">Kelmagan va kech kelganlar</p>
              <ul className="max-h-48 space-y-1 overflow-y-auto">
                {students
                  .filter((st) => rows[st.studentId]?.mark === 'absent' || rows[st.studentId]?.mark === 'late')
                  .map((st) => {
                    const r = rows[st.studentId]
                    const reason = r.reasonId ? reasonById.get(r.reasonId)?.name : null
                    return (
                      <li key={st.studentId} className="flex items-center justify-between gap-2 rounded-lg bg-panel2 px-2.5 py-1.5 text-[13px]">
                        <span className="truncate font-semibold text-ink">{st.fullName}</span>
                        <span className={cn('shrink-0 text-[12px] font-semibold', r.mark === 'late' ? 'text-amber-700' : 'text-red-600')}>
                          {reason ?? (r.mark === 'late' ? 'Kech' : 'Sababsiz')}
                        </span>
                      </li>
                    )
                  })}
              </ul>
            </div>
          )}

          <p className="mt-3 text-[12px] text-mute">
            Saqlanadi: <b className="text-ink">{changed.length}</b> ta o'quvchi. Kelmagan deb belgilanganlarning
            ota-onasiga avtomatik xabar yuborilishi mumkin.
          </p>
          {confirmError && (
            <p className="mt-3 flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2 text-[13px] text-red-600">
              <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" /> {confirmError}
            </p>
          )}
        </Modal>
      )}

      {reasonById.size > 0 && absentReasons.length === 0 && (
        <p className="mt-3 text-[12px] text-mute">Kelmaganlik sabablari sozlanmagan — «Sababsiz» deb yoziladi.</p>
      )}
    </TPage>
  )
}

function ConfirmStat({ label, value, cls }: { label: string; value: number; cls: string }) {
  return (
    <div className={cn('rounded-lg px-2 py-2', cls)}>
      <p className="text-[20px] font-bold leading-tight">{value}</p>
      <p className="text-[12px] font-semibold">{label}</p>
    </div>
  )
}

const MARK_TONES = {
  green: 'border-emerald-500 bg-emerald-500 text-white',
  amber: 'border-amber-500 bg-amber-500 text-white',
  red: 'border-red-500 bg-red-500 text-white',
} as const

function MarkButton({
  on,
  tone,
  onClick,
  icon: Icon,
  label,
}: {
  on: boolean
  tone: keyof typeof MARK_TONES
  onClick: () => void
  icon: typeof Check
  label: string
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={on}
      className={cn(
        'inline-flex h-9 items-center justify-center gap-1 rounded-lg border px-3 text-[13px] font-bold transition-colors sm:min-w-[84px]',
        on ? MARK_TONES[tone] : 'border-line bg-white text-mute hover:text-ink',
      )}
    >
      <Icon className="h-4 w-4" />
      {label}
    </button>
  )
}
