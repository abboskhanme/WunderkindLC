import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useParams, Link } from 'react-router-dom'
import {
  ArrowLeft, Users, BookOpen, User,
  CalendarDays, Clock, MapPin, CheckCircle2,
  ListChecks, ChevronRight, ChevronDown, Plus, Minus, Repeat, CalendarClock, Flag,
  TrendingUp, RotateCcw, ClipboardCheck, ImageIcon, PhoneCall,
} from 'lucide-react'
import type { AbsenceReason } from '@/types'
import {
  getTeacherGroupJournal, setTeacherJournalEntry, clearTeacherJournalEntry, bulkTeacherAttendance,
  rescheduleTeacherLesson, cancelTeacherReschedule,
  getTeacherGroupCurriculum, setTeacherGroupCover, changeTeacherGroupRevision,
  getTeacherMeta, getTeacherGradingBoard, setTeacherGrade, bulkTeacherGrade,
} from '@/api/services/teacher'
import type { GroupJournal } from '@/api/services/journal'
import type { GroupCurriculum } from '@/api/services/curriculum'
import { cn, formatDate, apiErrorMessage, gradeBadgeCls, balanceTextCls, balanceDotCls, balanceTitle, HEAVY_DEBT_MONTHS } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { GradingSection } from '@/components/grading/GradingSection'
import type { GradingBoard } from "@/api/services/grading"
import { Loader } from '@/components/ui/Loader'
import { GroupContactTab } from '@/components/contacts/GroupContactTab'
import { getTeacherContactReasons, sendTeacherGroupContacts } from '@/api/services/contacts'
import { Modal } from '@/components/ui/Modal'
import { PhotoViewerModal } from '@/components/ui/PhotoViewerModal'
import { Button } from '@/components/ui/Button'
import { JournalCellModal } from '@/pages/admin/journal/JournalCellModal'
import { TeacherGroupTestsPanel } from '@/pages/teacher/tests/TeacherGroupTestsPanel'

const weekdayShort = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']
const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m


/** Mastery darajasi — rangi va yorlig'i */
function masteryDisplay(m: number | undefined): { label: string; cls: string } {
  switch (m) {
    case 0:
      return { label: '😴', cls: 'bg-slate-50 text-slate-600 hover:bg-slate-100' }
    case 1:
      return { label: '👂', cls: 'bg-blue-50 text-blue-600 hover:bg-blue-100' }
    case 2:
      return { label: '🙋', cls: 'bg-emerald-50 text-emerald-600 hover:bg-emerald-100' }
    case 3:
      return { label: '⭐', cls: 'bg-amber-50 text-amber-600 hover:bg-amber-100' }
    default:
      return { label: '', cls: '' }
  }
}

export function TeacherGroupDetailPage() {
  const { id = '' } = useParams()
  const [journal, setJournal] = useState<GroupJournal | null>(null)
  const [grading, setGrading] = useState<GradingBoard | null>(null)
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [cell, setCell] = useState<{ studentId: string; studentName: string; date: string } | null>(null)
  /** Jurnalda F.I.SH bosilganda ochiladigan rasm oynasi (o'quvchini yuzidan tanish uchun). */
  const [photoOf, setPhotoOf] = useState<{ fullName: string; photoUrl: string } | null>(null)
  /** JournalCellModal saqlash/tozalash xatoligi — banner sifatida modal ichida ko'rsatiladi. */
  const [cellError, setCellError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  /** Sarlavhadagi sana bosilganda — shu kun uchun hammaga davomat modali. */
  const [bulkDate, setBulkDate] = useState<string | null>(null)
  const [bulkSaving, setBulkSaving] = useState(false)
  /** Darsni boshqa kunga ko'chirish (bulk modal ichida): forma ochiqmi + yangi sana/vaqt. */
  const [rescheduleOpen, setRescheduleOpen] = useState(false)
  const [rToDate, setRToDate] = useState('')
  const [rTime, setRTime] = useState('')
  const [rSaving, setRSaving] = useState(false)
  const [rError, setRError] = useState<string | null>(null)

  // ---- Guruh o'quv dasturi (darsda o'tilgan) ----
  const [groupView, setGroupView] = useState<
    'jurnal' | 'davomat' | 'baholash' | 'reyting' | 'imtihonlar' | 'dastur' | 'aloqa'
  >('jurnal')
  const [curr, setCurr] = useState<GroupCurriculum | null>(null)
  const [currLoading, setCurrLoading] = useState(true)
  // Endi ALOHIDA tab — ochilganda darhol ko'rinsin (ilgari jurnal ostidagi yig'iq blok edi).
  const [currOpen, setCurrOpen] = useState(true)
  const [currExpanded, setCurrExpanded] = useState<Set<string>>(new Set())
  const [revSaving, setRevSaving] = useState(false)

  const loadCurr = useCallback(() => {
    if (!id) return
    setCurrLoading(true)
    getTeacherGroupCurriculum(id)
      .then(setCurr)
      .catch(() => setCurr(null))
      .finally(() => setCurrLoading(false))
  }, [id])

  useEffect(() => {
    loadCurr()
  }, [loadCurr])

  const load = useCallback(
    (month?: string) => {
      if (!id) return
      setLoading(true)
      getTeacherGroupJournal(id, month)
        .then(setJournal)
        .finally(() => setLoading(false))
    },
    [id],
  )

  const loadGrading = useCallback(
    (month?: string) => {
      if (!id) return
      getTeacherGradingBoard(id, month)
        .then(setGrading)
        .catch(() => setGrading(null))
    },
    [id],
  )

  useEffect(() => {
    load()
    getTeacherMeta()
      .then((m) => setReasons(m?.absenceReasons ?? []))
      .catch(() => {})
  }, [load])

  useEffect(() => {
    if (groupView === 'reyting' && journal?.month) {
      loadGrading(journal.month)
    }
  }, [groupView, journal?.month, loadGrading])

  const reasonById = useMemo(
    () => new Map(reasons.map((r) => [r.id, r])),
    [reasons],
  )
  const entryMap = useMemo(
    () => new Map((journal?.entries ?? []).map((e) => [`${e.studentId}|${e.date}`, e])),
    [journal],
  )
  const conductedSet = useMemo(() => new Set(journal?.conductedDates ?? []), [journal])
  // O'quvchilari jurnalga kiritilgan va baholashga kiritilgan baholarning yig'indisi bo'yicha saralash.
  const sortedAndScoredStudents = useMemo(() => {
    const students = (journal?.students ?? []).filter((s) => s.status !== 'frozen')
    if (!grading || grading.students.length === 0) {
      // Faqat jurnal bahosi
      return students.map((s, idx) => {
        const totalGrade = journal!.columns.reduce((sum, c) => {
          const e = entryMap.get(`${s.studentId}|${c.date}`)
          return sum + (e?.grade ?? 0)
        }, 0)
        return { ...s, journalTotal: totalGrade, gradingTotal: 0, combinedTotal: totalGrade, originalIndex: idx }
      })
        .sort((a, b) => b.combinedTotal - a.combinedTotal || a.originalIndex - b.originalIndex)
    }
    // Baholash va jurnal baholarini birlashtirish
    const gradingStudentMap = new Map(grading.students.map((gs) => [gs.studentId, gs]))
    return students.map((s, idx) => {
      const journalTotal = journal!.columns.reduce((sum, c) => {
        const e = entryMap.get(`${s.studentId}|${c.date}`)
        return sum + (e?.grade ?? 0)
      }, 0)
      const gs = gradingStudentMap.get(s.studentId)
      const doneKeys = new Set(gs?.doneKeys ?? [])
      const gradingTotal = doneKeys.size
      return {
        ...s,
        journalTotal,
        gradingTotal,
        combinedTotal: journalTotal + gradingTotal,
        originalIndex: idx,
      }
    })
      .sort((a, b) => b.combinedTotal - a.combinedTotal || a.originalIndex - b.originalIndex)
  }, [journal, grading, entryMap])

  const journalStudents = useMemo(
    () => sortedAndScoredStudents,
    [sortedAndScoredStudents],
  )

  const g = journal?.group
  const today = new Date().toISOString().slice(0, 10)
  const absentReasons = useMemo(() => reasons.filter((r) => !r.isLate), [reasons])
  // "Kech qoldi" sabablari davomat foiziga hisoblanmaydi (JournalCellModal/DashboardController bilan bir xil).
  const lateReasonIds = useMemo(
    () => new Set(reasons.filter((r) => r.isLate).map((r) => r.id)),
    [reasons],
  )

  // Davomat tabi: shu oy jurnalidan har o'quvchi bo'yicha davomat foizi (yangi so'rovsiz).
  const attendanceRows = useMemo(() => {
    const conducted = journal?.conductedDates ?? []
    return journalStudents.map((st) => {
      // O'quvchi guruhga qo'shilgandan (memberStart) keyingi o'tilgan darslar.
      const myConducted = conducted.filter((d) => !st.memberStart || d >= st.memberStart)
      const total = myConducted.length
      // Sababli kelmagan darslar (kech qolgan mustasno).
      let absences = 0
      for (const d of myConducted) {
        const e = entryMap.get(`${st.studentId}|${d}`)
        if (e?.reasonId && !lateReasonIds.has(e.reasonId)) absences += 1
      }
      const present = total - absences
      const percent = total > 0 ? Math.round((present / total) * 100) : 0
      return { studentId: st.studentId, fullName: st.fullName, photoUrl: st.photoUrl ?? '', balance: st.balance, debtMonths: st.debtMonths, total, present, absences, percent }
    })
  }, [journalStudents, journal, entryMap, lateReasonIds])

  const handleSave = async (
    grade: number | null,
    reasonId: string | null,
    homework: number,
    behavior: number,
    mastery: number | null,
    present: boolean,
  ) => {
    if (!journal || !cell) return
    setSaving(true)
    setCellError(null)
    try {
      await setTeacherJournalEntry(journal.group.id, journal.group.courseId, cell.studentId, cell.date, {
        grade, reasonId, homework, behavior, mastery, present,
      })
      setCell(null)
      load(journal.month)
    } catch (err) {
      setCellError(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const handleClear = async () => {
    if (!journal || !cell) return
    setSaving(true)
    setCellError(null)
    try {
      await clearTeacherJournalEntry(journal.group.id, journal.group.courseId, cell.studentId, cell.date)
      setCell(null)
      load(journal.month)
    } catch (err) {
      setCellError(apiErrorMessage(err, "Tozalab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  // Daraja yoyish/yig'ish (default — yopiq)
  const toggleTopic = (topicId: string) =>
    setCurrExpanded((s) => {
      const next = new Set(s)
      if (next.has(topicId)) next.delete(topicId)
      else next.add(topicId)
      return next
    })

  // Birinchi o'tilmagan band — "keyingi" maslahati uchun
  const nextItemId = useMemo(() => {
    if (!curr) return null
    for (const md of curr.modules)
      for (const tp of md.topics)
        for (const it of tp.items) if (!it.covered) return it.id
    return null
  }, [curr])

  // Band belgilash — optimistik, so'ng refetch (prognoz aniq qolishi uchun)
  const toggleCover = async (itemId: string, covered: boolean) => {
    if (!curr) return
    const prev = curr
    setCurr({
      ...curr,
      coveredCount: curr.coveredCount + (covered ? 1 : -1),
      modules: curr.modules.map((md) => ({
        ...md,
        topics: md.topics.map((tp) => ({
          ...tp,
          items: tp.items.map((it) => (it.id === itemId ? { ...it, covered } : it)),
        })),
      })),
    })
    try {
      await setTeacherGroupCover(id, itemId, covered)
      loadCurr()
    } catch {
      setCurr(prev)
      alert("Saqlab bo'lmadi")
    }
  }

  const changeRevision = async (delta: number) => {
    if (!curr || revSaving) return
    setRevSaving(true)
    try {
      await changeTeacherGroupRevision(id, delta)
      loadCurr()
    } catch {
      alert("Saqlab bo'lmadi")
    } finally {
      setRevSaving(false)
    }
  }

  // Sarlavhadagi sana bosilganda — shu darsdagi BARCHA o'quvchiga birdan davomat.
  const doBulk = async (absent: boolean, reasonId: string | null) => {
    if (!journal || !bulkDate) return
    setBulkSaving(true)
    try {
      await bulkTeacherAttendance(
        journal.group.id,
        journal.group.courseId,
        1,
        journalStudents.map((s) => s.studentId),
        bulkDate,
        absent,
        reasonId,
      )
      setBulkDate(null)
      load(journal.month)
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBulkSaving(false)
    }
  }

  // Shu oyga ko'chirilgan darslar: yangi kun (toDate) → ko'chirish yozuvi (ustun belgisi + bekor qilish uchun).
  const rescheduledByDate = useMemo(() => {
    const m = new Map<string, { id: string; fromDate: string; time?: string | null }>()
    for (const r of journal?.reschedules ?? []) m.set(r.toDate, { id: r.id, fromDate: r.fromDate, time: r.time })
    return m
  }, [journal])

  // Darsni boshqa kunga ko'chirish (bulk modaldan): asl kun = bulkDate, yangi kun = rToDate.
  const doReschedule = async () => {
    if (!journal || !bulkDate || !rToDate) return
    setRSaving(true)
    setRError(null)
    try {
      await rescheduleTeacherLesson(journal.group.id, bulkDate, rToDate, rTime || undefined)
      setBulkDate(null)
      setRescheduleOpen(false)
      load(journal.month)
    } catch (err) {
      setRError(apiErrorMessage(err, "Ko'chirib bo'lmadi"))
    } finally {
      setRSaving(false)
    }
  }

  // Ko'chirishni bekor qilish — dars asl kuniga qaytadi.
  const doCancelReschedule = async (rescheduleId: string) => {
    if (!journal) return
    setRSaving(true)
    setRError(null)
    try {
      await cancelTeacherReschedule(rescheduleId)
      setBulkDate(null)
      setRescheduleOpen(false)
      load(journal.month)
    } catch (err) {
      setRError(apiErrorMessage(err, "Bekor qilib bo'lmadi"))
    } finally {
      setRSaving(false)
    }
  }

  const cellEntry = cell ? entryMap.get(`${cell.studentId}|${cell.date}`) ?? null : null

  return (
    <div className="space-y-5 px-4 pt-3 pb-6">
      {/* Sarlavha */}
      <div className="flex items-center gap-3">
        <Link
          to="/teacher"
          className="rounded-[14px] border border-line bg-white p-2 text-mute transition-colors hover:bg-tealsoft hover:text-teal-700"
        >
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="min-w-0">
          <h1 className="truncate text-xl font-extrabold tracking-tight text-ink">{g ? g.name : "Guruh"}</h1>
          {g && (
            <p className="mt-0.5 truncate text-sm text-faint">
              {g.courseName || "Kurs biriktirilmagan"}
            </p>
          )}
        </div>
      </div>

      {loading && !journal ? (
        <Loader label="Yuklanmoqda..." />
      ) : !g ? (
        <Card className="rounded-[20px] border border-line bg-white py-16 text-center text-faint shadow-[var(--shadow-card)]">
          Guruh topilmadi
        </Card>
      ) : (
        <>
          {/* Guruh ma'lumotlari */}
          <Card className="rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
            <div className="grid grid-cols-2 gap-x-4 gap-y-4">
              <Info icon={BookOpen} label="Kurs" value={g.courseName || "—"} />
              <Info icon={User} label="O'qituvchi" value={g.teacherName || "—"} />
              <Info
                icon={CalendarDays}
                label="Kunlar"
                value={g.days.length ? g.days.map((d) => weekdayShort[d] ?? d).join(", ") : "—"}
              />
              <Info
                icon={Clock}
                label="Vaqt"
                value={
                  g.startTime || g.endTime ? `${g.startTime || "—"}${g.endTime ? ` â€“ ${g.endTime}` : ""}` : "—"
                }
                mono
              />
              <Info icon={MapPin} label="Xona" value={g.room || "—"} />
              <Info icon={Users} label="O'quvchilar" value={String(journalStudents.length)} mono />
            </div>
          </Card>

          {/* Jurnal / Davomat / Baholash / Reyting / Imtihonlar tab almashtirgich */}
          <div className="flex flex-wrap gap-1 rounded-xl border border-line bg-paper2 p-1">
            {([
              { key: "jurnal", label: "Jurnal" },
              { key: "davomat", label: "Davomat" },
              { key: "baholash", label: "Baholash" },
              { key: "reyting", label: "Reyting" },
              { key: "imtihonlar", label: "Imtihonlar" },
              { key: "dastur", label: "O'quv dasturi" },
              // ALOQA — o'quvchini "Bog'lanish kerak" navbatiga yuborish (admin ko'radi va qo'ng'iroq qiladi).
              { key: "aloqa", label: "Aloqa" },
            ] as const).map((t) => (
              <button
                key={t.key}
                type="button"
                onClick={() => {
                  setGroupView(t.key)
                  if (t.key === "jurnal") load(journal?.month)
                }}
                className={cn(
                  "rounded-lg px-4 py-1.5 text-sm font-semibold transition-colors",
                  groupView === t.key ? "bg-white text-ink shadow-sm" : "text-mute",
                )}
              >
                {t.label}
              </button>
            ))}
          </div>

          {/* Oylik jurnal */}
          {groupView === "jurnal" && (
            <Card className="rounded-[20px] border border-line bg-white p-0 shadow-[var(--shadow-card)]">
              <div className="flex items-center gap-2 border-b border-line-soft px-4 py-3">
                <BookOpen className="h-5 w-5 text-teal-600" />
                <h2 className="font-semibold text-ink">Jurnal</h2>
                <span className="inline-flex items-center gap-1 text-sm text-faint">
                  <Users className="h-4 w-4" /> {journalStudents.length}
                </span>
              </div>

              {/* Oy navigatsiyasi — gorizontal skroll */}
              {journal && journal.months.length > 0 && (
                <div className="flex gap-1.5 overflow-x-auto border-b border-line-soft px-4 py-2.5">
                  {journal.months.map((m) => (
                    <button
                      key={m}
                      type="button"
                      onClick={() => load(m)}
                      title={monthLabel(m)}
                      className={cn(
                        "shrink-0 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors",
                        journal.month === m
                          ? "bg-teal-600 text-white"
                          : "bg-panel3 text-mute hover:bg-tealsoft",
                      )}
                    >
                      {monthLabel(m)}
                    </button>
                  ))}
                </div>
              )}

              {!g.courseId ? (
                <p className="px-4 py-12 text-center text-sm text-faint">
                  Guruhga kurs biriktirilmagan — jurnal yuritib bo'lmaydi.
                </p>
              ) : journalStudents.length === 0 ? (
                <p className="px-4 py-12 text-center text-sm text-faint">
                  Bu guruhda faol o'quvchi yo'q.
                </p>
              ) : (journal?.columns.length ?? 0) === 0 ? (
                <p className="px-4 py-12 text-center text-sm text-faint">
                  {monthLabel(journal?.month ?? "")} oyida bu guruh kunlariga dars to'g'ri kelmadi.
                </p>
              ) : (
                <>
                {/* Rang izohi — o'quvchi ismining rangi SHU GURUH balansiga qarab beriladi.
                    Ranglar `balanceDotCls` bilan bir manbadan olinadi (izoh va jadval bir-biriga mos tursin). */}
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line-soft px-4 py-2 text-[11px] text-faint">
                  <span className="inline-flex items-center gap-1">
                    <span className={cn('h-2 w-2 rounded-full', balanceDotCls(0))} /> To'lagan
                  </span>
                  <span className="inline-flex items-center gap-1">
                    <span className={cn('h-2 w-2 rounded-full', balanceDotCls(-1))} /> Qarzdor
                  </span>
                  <span className="inline-flex items-center gap-1">
                    <span className={cn('h-2 w-2 rounded-full', balanceDotCls(-1, HEAVY_DEBT_MONTHS))} /> 2+ oylik qarz
                  </span>
                </div>
                <div className="overflow-x-auto">
                  <table className="min-w-full border-collapse text-sm">
                    <thead>
                      <tr className="bg-panel3 text-xs text-mute">
                        {/* № ustuni ham STICKY: aks holda o'ngga siljitilganda F.I.SH ustuni
                            uning ustidan surilib, tartib raqami ko'rinmay qolardi. Kengligi QAT'IY
                            (w-10 = 40px) — F.I.SH ustuni aynan shu masofada (`left-10`) turadi. */}
                        <th className="sticky left-0 z-20 w-10 border-b-2 border-r border-line bg-panel3 px-1 py-2.5 text-center font-semibold">
                          №
                        </th>
                        <th className="sticky left-10 z-20 border-b-2 border-r-2 border-line bg-panel3 px-3 py-2.5 text-left font-semibold">
                          O'quvchi
                        </th>
                        {journal!.columns.map((c) => {
                          const dt = new Date(c.date)
                          const wd = (dt.getDay() + 6) % 7
                          const isToday = c.date === today
                          return (
                            <th
                              key={c.date}
                              className={cn(
                                "border-b-2 border-r border-line p-0 text-center font-semibold",
                                isToday ? "bg-tealsoft text-teal-700" : "text-mute",
                              )}
                            >
                              <button
                                type="button"
                                onClick={() => setBulkDate(c.date)}
                                title="Shu kun uchun hammaga davomat"
                                className="w-full px-2 py-1.5 transition-colors hover:bg-tealsoft"
                              >
                                <div className="text-sm">{c.date.slice(8, 10)}</div>
                                <div
                                  className={cn(
                                    "text-[10px] font-medium",
                                    isToday ? "text-teal-500" : "text-faint",
                                  )}
                                >
                                  {weekdayShort[wd]}
                                </div>
                              </button>
                            </th>
                          )
                        })}
                        <th className="sticky right-0 z-10 border-b-2 border-l-2 border-line bg-panel2 px-3 py-1.5 text-center font-semibold text-mute">
                          Jami
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {journalStudents.map((st, idx) => {
                        return (
                        <tr key={st.studentId} className="bg-white even:bg-panel2">
                          <td className="sticky left-0 z-10 w-10 border-b border-r border-line bg-inherit px-1 py-2 text-center">
                            <span className="text-xs font-medium text-mute">{idx + 1}</span>
                          </td>
                          <td className="sticky left-10 z-10 border-b border-r-2 border-line bg-inherit px-3 py-2">
                            {/* FISH to'liq ko'rinishi shart (telefonda o'qish uchun) — kesilmaydi,
                                bir necha qatorga o'raladi; kenglik sticky ustun barqarorligi uchun qat'iy.
                                Rang: 2+ oylik qarz binafsha-pushti (eng og'ir, qizildan USTUN), qarzdor
                                (balance<0) qizil, to'lagan yashil — admin ro'yxati bilan bir xil.
                                MUHIM: balance/debtMonths — SHU GURUH bo'yicha (boshqa guruhdagi qarz bu yerni qizil qilmaydi). */}
                            {/* F.I.SH bosilsa — o'quvchining rasmi ochiladi (jurnaldan chiqmasdan).
                                Rasmi borlarda ism yonida kichik ikonka turadi. */}
                            <button
                              type="button"
                              onClick={() =>
                                setPhotoOf({ fullName: st.fullName, photoUrl: st.photoUrl ?? '' })
                              }
                              className={cn(
                                'block w-32 whitespace-normal break-words text-left text-sm font-medium leading-snug hover:underline',
                                balanceTextCls(st.balance, st.debtMonths),
                              )}
                              title={`${balanceTitle(st.balance, st.debtMonths)} — rasmni ko'rish uchun bosing`}
                            >
                              {st.fullName}
                              {st.photoUrl && (
                                <ImageIcon className="ml-1 inline-block h-3 w-3 shrink-0 align-[-1px] opacity-50" />
                              )}
                            </button>
                          </td>
                          {journal!.columns.map((c) => {
                            const e = entryMap.get(`${st.studentId}|${c.date}`)
                            const reason = e?.reasonId ? reasonById.get(e.reasonId) : undefined
                            const isToday = c.date === today
                            // O'quvchi guruhda boshlagan (memberStart) yoki guruh yaratilishidan OLDINGI
                            // darslarga davomat/baho qo'yib bo'lmaydi — katak bloklanadi.
                            const beforeMember = !!st.memberStart && c.date < st.memberStart
                            const groupStart = journal!.group.startDate
                            const isBeforeStart = (!!groupStart && c.date < groupStart) || beforeMember
                            // Keldi (yashil): dars o'tildi + baho yo'q + sabab yo'q + (ANIQ "keldi" belgisi
                            // BOR yoki sana o'quvchi tizimga kiritilganidan (presentDefaultFrom) keyin) —
                            // orqaga sanalgan a'zolikda ko'rib chiqilmagan darslar avto-"keldi" bo'lmaydi,
                            // lekin o'qituvchi "Keldi"/"hammasi keldi" bosgan (e.present) katak yashil bo'ladi.
                            const present =
                              e?.grade == null && !reason && conductedSet.has(c.date) &&
                              (e?.present || !st.presentDefaultFrom || c.date >= st.presentDefaultFrom)
                            const masteryInfo = e?.mastery != null ? masteryDisplay(e.mastery) : { label: '', cls: '' }
                            return (
                              <td
                                key={c.date}
                                className={cn(
                                  "border-b border-r border-line-soft p-1 text-center",
                                  isBeforeStart ? "bg-slate-50" : isToday && "bg-tealsoft",
                                )}
                              >
                                <button
                                  type="button"
                                  disabled={isBeforeStart}
                                  onClick={() => {
                                    setCellError(null)
                                    setCell({
                                      studentId: st.studentId,
                                      studentName: st.fullName,
                                      date: c.date,
                                    })
                                  }}
                                  className={cn(
                                    "flex h-9 w-full min-w-9 items-center justify-center rounded-md text-sm font-semibold transition-colors",
                                    isBeforeStart
                                      ? "cursor-not-allowed text-slate-200"
                                      : e?.grade != null
                                      ? gradeBadgeCls(e.grade)
                                      : e?.mastery != null
                                        ? masteryInfo.cls
                                        : reason
                                          ? reason.isLate
                                            ? "bg-amber-50 text-amber-700"
                                            : "bg-red-50 text-red-600"
                                          : present
                                            ? "bg-emerald-50 text-emerald-600"
                                            : "text-faint",
                                  )}
                                  title={isBeforeStart ? (beforeMember ? "O'quvchi guruhga qo'shilishidan oldingi dars" : 'Sana guruh yaratilishidan oldin') : `${st.fullName} — ${formatDate(c.date)}`}
                                >
                                  {e?.grade != null
                                    ? e.grade
                                    : e?.mastery != null
                                      ? masteryInfo.label
                                      : reason
                                        ? reason.short || reason.name.slice(0, 2)
                                        : present
                                          ? "✓"
                                          : "·"}
                                </button>
                              </td>
                            )
                          })}
                          <td className="sticky right-0 z-10 border-b border-l-2 border-line bg-inherit px-3 py-2 text-center">
                            <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold text-ink', st.combinedTotal > 0 ? 'bg-tealsoft text-teal-700' : 'text-faint')}>
                              {st.combinedTotal || '—'}
                            </span>
                          </td>
                        </tr>
                      )
                      })}
                    </tbody>
                  </table>
                </div>
                </>
              )}
            </Card>
          )}

          {/* Davomat — shu oy jurnalidan har o'quvchi bo'yicha davomat foizi */}
          {groupView === "davomat" && (
            <Card className="rounded-[20px] border border-line bg-white p-0 shadow-[var(--shadow-card)]">
              <div className="flex items-center gap-2 border-b border-line-soft px-4 py-3">
                <ClipboardCheck className="h-5 w-5 text-teal-600" />
                <h2 className="font-semibold text-ink">Davomat</h2>
                <span className="text-sm text-faint">{monthLabel(journal?.month ?? "")}</span>
              </div>
              {journalStudents.length === 0 ? (
                <p className="px-4 py-12 text-center text-sm text-faint">Bu guruhda faol o'quvchi yo'q.</p>
              ) : (journal?.conductedDates?.length ?? 0) === 0 ? (
                <p className="px-4 py-12 text-center text-sm text-faint">
                  {monthLabel(journal?.month ?? "")} oyida o'tilgan dars yo'q.
                </p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="min-w-full border-collapse text-sm">
                    <thead>
                      <tr className="bg-panel3 text-xs uppercase text-mute">
                        {/* Qator raqami (№) — jurnal jadvalidagi bilan bir xil uslub */}
                        <th className="w-8 border-b border-line px-2 py-3 text-center font-semibold">№</th>
                        <th className="border-b border-line px-4 py-3 text-left font-semibold">O'quvchi</th>
                        <th className="border-b border-line px-3 py-3 text-center font-semibold">Darslar</th>
                        <th className="border-b border-line px-3 py-3 text-center font-semibold">Keldi</th>
                        <th className="border-b border-line px-3 py-3 text-center font-semibold">Kelmadi</th>
                        <th className="border-b border-line px-3 py-3 text-center font-semibold">Davomat</th>
                      </tr>
                    </thead>
                    <tbody>
                      {attendanceRows.map((r, idx) => (
                        <tr key={r.studentId} className="border-b border-line-soft hover:bg-panel2">
                          <td className="w-8 px-2 py-3 text-center">
                            <span className="text-xs font-medium text-mute">{idx + 1}</span>
                          </td>
                          <td className="px-4 py-3">
                            {/* Jurnal tabidagi kabi — ism bosilsa rasm ochiladi. */}
                            <button
                              type="button"
                              onClick={() => setPhotoOf({ fullName: r.fullName, photoUrl: r.photoUrl })}
                              className={cn(
                                'text-left text-sm font-medium hover:underline',
                                balanceTextCls(r.balance, r.debtMonths),
                              )}
                              title={`${balanceTitle(r.balance, r.debtMonths)} — rasmni ko'rish uchun bosing`}
                            >
                              {r.fullName}
                              {r.photoUrl && (
                                <ImageIcon className="ml-1 inline-block h-3 w-3 shrink-0 align-[-1px] opacity-50" />
                              )}
                            </button>
                          </td>
                          <td className="px-3 py-3 text-center font-mono text-sm text-mute">{r.total}</td>
                          <td className="px-3 py-3 text-center font-mono text-sm font-semibold text-emerald-600">
                            {r.present}
                          </td>
                          <td className="px-3 py-3 text-center font-mono text-sm font-semibold text-red-500">
                            {r.absences || "—"}
                          </td>
                          <td className="px-3 py-3 text-center">
                            <span
                              className={cn(
                                "inline-flex min-w-[52px] items-center justify-center rounded-md px-2 py-1 text-sm font-bold",
                                r.total === 0
                                  ? "text-faint"
                                  : r.percent >= 90
                                    ? "bg-emerald-50 text-emerald-700"
                                    : r.percent >= 70
                                      ? "bg-amber-50 text-amber-700"
                                      : "bg-red-50 text-red-600",
                              )}
                            >
                              {r.total === 0 ? "—" : `${r.percent}%`}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}

          {/* Baholash — har darsga mezonlar bo'yicha bajardi/bajarmadi (faqat o'z guruhi) */}
          {groupView === "baholash" && (
            <Card className="rounded-[20px] border border-line bg-white shadow-[var(--shadow-card)]">
              <h2 className="mb-3 font-semibold text-ink">Baholash</h2>
              <GradingSection
                groupId={id}
                fetchBoard={getTeacherGradingBoard}
                saveGrade={setTeacherGrade}
                bulkGrade={bulkTeacherGrade}
              />
            </Card>
          )}

          {/* Reyting — o'quvchilarning o'rtacha bahosi va baholash statistikasi (bir yoki bir nechta oy) */}
          {groupView === "aloqa" && id && (
            <Card className="rounded-[20px] border border-line bg-white p-4 shadow-[var(--shadow-card)]">
              <div className="mb-3 flex items-center gap-2">
                <PhoneCall className="h-5 w-5 text-teal-600" />
                <h2 className="font-semibold text-ink">Bog'lanish kerak</h2>
              </div>
              <GroupContactTab
                students={(journal?.students ?? []).map((s) => ({
                  id: s.studentId,
                  fullName: s.fullName,
                  status: s.status,
                }))}
                loadReasons={getTeacherContactReasons}
                onSend={(ids, reasonId, note) =>
                  sendTeacherGroupContacts(id, {
                    studentIds: ids,
                    reasonId: reasonId || undefined,
                    note: note || undefined,
                  })
                }
              />
            </Card>
          )}

          {groupView === "reyting" && <RatingsTab groupId={id} months={journal?.months ?? []} defaultMonth={journal?.month} />}

          {/* Imtihonlar — shu guruh testlari (yaratish/tahrirlash/ball qo'yish) */}
          {groupView === "imtihonlar" && (
            <TeacherGroupTestsPanel groupId={id} title="Imtihonlar (testlar)" />
          )}

          {/* O'QUV DASTURI — endi ALOHIDA tab (ilgari barcha tablar ostida, jurnal pastida
              turardi). Sarlavhasini bosib yig'ish imkoniyati qoldi. */}
          {groupView === "dastur" && (
            <CurriculumSection
              curr={curr}
              loading={currLoading}
              open={currOpen}
              onToggleOpen={() => setCurrOpen((v) => !v)}
              expanded={currExpanded}
              onToggleTopic={toggleTopic}
              onToggleCover={toggleCover}
              onChangeRevision={changeRevision}
              revSaving={revSaving}
              nextItemId={nextItemId}
            />
          )}
        </>
      )}

      <JournalCellModal
        open={!!cell}
        studentName={cell?.studentName ?? ""}
        dateLabel={cell ? formatDate(cell.date) : ""}
        entry={cellEntry}
        reasons={reasons}
        error={cellError}
        onClose={() => {
          if (saving) return
          setCell(null)
          setCellError(null)
        }}
        onSave={handleSave}
        onClear={handleClear}
      />

      {/* F.I.SH bosilganda — o'quvchining rasmi (telefon/kompyuterga moslashadi) */}
      <PhotoViewerModal
        open={!!photoOf}
        onClose={() => setPhotoOf(null)}
        url={photoOf?.photoUrl}
        title={photoOf?.fullName}
      />

      {/* Sarlavha sanasi bosilganda — shu kun uchun hammaga birdan davomat */}
      <Modal
        open={!!bulkDate}
        onClose={() => {
          if (bulkSaving || rSaving) return
          setBulkDate(null)
          setRescheduleOpen(false)
          setRError(null)
        }}
        size="sm"
        title={bulkDate ? `${formatDate(bulkDate)} — davomat` : "Davomat"}
        footer={
          <Button
            variant="secondary"
            onClick={() => {
              setBulkDate(null)
              setRescheduleOpen(false)
              setRError(null)
            }}
            disabled={bulkSaving || rSaving}
          >
            Yopish
          </Button>
        }
      >
        <div className="space-y-4">
          <p className="text-sm text-mute">
            Shu darsdagi <span className="font-semibold text-ink">{journalStudents.length}</span> o'quvchiga
            birdan qo'llanadi.
          </p>
          <div className="grid grid-cols-2 gap-2">
            <button
              type="button"
              onClick={() => doBulk(false, null)}
              disabled={bulkSaving}
              className="rounded-lg bg-emerald-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-emerald-700 disabled:opacity-50"
            >
              ✓ Hammasi keldi
            </button>
            <button
              type="button"
              onClick={() => doBulk(true, null)}
              disabled={bulkSaving}
              className="rounded-lg bg-red-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-red-700 disabled:opacity-50"
            >
              ✗ Hammasi kelmadi
            </button>
          </div>
          {absentReasons.length > 0 && (
            <div>
              <p className="mb-2 text-sm font-medium text-mute">Yoki sabab bilan kelmadi:</p>
              <div className="flex flex-wrap gap-2">
                {absentReasons.map((r) => (
                  <button
                    key={r.id}
                    type="button"
                    disabled={bulkSaving}
                    onClick={() => doBulk(true, r.id)}
                    className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm font-medium text-red-600 transition-colors hover:bg-red-100 disabled:opacity-50"
                  >
                    {r.name}
                  </button>
                ))}
              </div>
            </div>
          )}

          {/* Darsni boshqa kunga ko'chirish (bir martalik) */}
          <div className="border-t border-slate-100 pt-4">
            {bulkDate && rescheduledByDate.has(bulkDate) ? (
              // Bu ustunning o'zi ko'chirilgan dars — asl kuniga qaytarish mumkin.
              <div className="space-y-2">
                <p className="flex items-center gap-2 text-sm text-sky-700">
                  <CalendarClock className="h-4 w-4 shrink-0" />
                  Bu dars{' '}
                  <b>{formatDate(rescheduledByDate.get(bulkDate)!.fromDate)}</b> dan ko'chirilgan
                  {rescheduledByDate.get(bulkDate)!.time ? ` (${rescheduledByDate.get(bulkDate)!.time})` : ''}.
                </p>
                <button
                  type="button"
                  disabled={rSaving}
                  onClick={() => doCancelReschedule(rescheduledByDate.get(bulkDate)!.id)}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50 disabled:opacity-50"
                >
                  <RotateCcw className="h-4 w-4" /> Asl kuniga qaytarish
                </button>
              </div>
            ) : !rescheduleOpen ? (
              <button
                type="button"
                disabled={bulkSaving}
                onClick={() => {
                  setRescheduleOpen(true)
                  setRToDate('')
                  setRTime(journal?.group.startTime ?? '')
                  setRError(null)
                }}
                className="inline-flex items-center gap-1.5 rounded-lg border border-sky-200 bg-sky-50 px-3 py-2 text-sm font-medium text-sky-700 transition-colors hover:bg-sky-100 disabled:opacity-50"
              >
                <CalendarClock className="h-4 w-4" /> Darsni boshqa kunga ko'chirish
              </button>
            ) : (
              <div className="space-y-3">
                <p className="text-sm font-medium text-mute">
                  Darsni boshqa kunga ko'chirish (bir martalik)
                </p>
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="mb-1 block text-xs text-mute">Yangi sana</label>
                    <input
                      type="date"
                      value={rToDate}
                      onChange={(e) => setRToDate(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-ink outline-none focus:border-brand-400"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs text-mute">Vaqt (ixtiyoriy)</label>
                    <input
                      type="time"
                      value={rTime}
                      onChange={(e) => setRTime(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-ink outline-none focus:border-brand-400"
                    />
                  </div>
                </div>
                {rError && <p className="text-sm font-medium text-red-600">{rError}</p>}
                <div className="flex gap-2">
                  <button
                    type="button"
                    disabled={rSaving || !rToDate}
                    onClick={doReschedule}
                    className="inline-flex items-center gap-1.5 rounded-lg bg-sky-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-sky-700 disabled:opacity-50"
                  >
                    <CalendarClock className="h-4 w-4" /> {rSaving ? "Ko'chirilmoqda..." : "Ko'chirish"}
                  </button>
                  <button
                    type="button"
                    disabled={rSaving}
                    onClick={() => {
                      setRescheduleOpen(false)
                      setRError(null)
                    }}
                    className="rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-50"
                  >
                    Bekor
                  </button>
                </div>
              </div>
            )}
          </div>
        </div>
      </Modal>
    </div>
  )
}

function Info({
  icon: Icon,
  label,
  value,
  mono,
}: {
  icon: typeof BookOpen
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-tealsoft text-teal-600">
        <Icon className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-faint">{label}</p>
        <p className={cn("break-words text-sm font-semibold text-ink", mono && "font-mono")}>
          {value}
        </p>
      </div>
    </div>
  )
}

// ============================ Reyting bo'limi (bir yoki bir nechta oy) ============================

/** Bitta oy uchun jurnal + baholash ma'lumoti — reyting yig'indisini hisoblash uchun. */
interface RatingMonthData {
  journal: GroupJournal
  grading: GradingBoard | null
}

function RatingsTab({
  groupId,
  months,
  defaultMonth,
}: {
  groupId: string
  months: string[]
  defaultMonth?: string
}) {
  const [selectedMonths, setSelectedMonths] = useState<string[]>(defaultMonth ? [defaultMonth] : [])
  const [data, setData] = useState<RatingMonthData[]>([])
  const [loading, setLoading] = useState(true)

  // Oylar ro'yxati kelganda (yoki o'zgarganda) — hozirgi tanlov bekor bo'lsa, oxirgi oyni tanlaymiz.
  useEffect(() => {
    if (months.length === 0) return
    setSelectedMonths((prev) => {
      const valid = prev.filter((m) => months.includes(m))
      if (valid.length > 0) return valid
      return defaultMonth && months.includes(defaultMonth) ? [defaultMonth] : [months[months.length - 1]]
    })
    // defaultMonth faqat boshlang'ich tanlovni belgilaydi — keyingi o'zgarishlarda qayta ishlatilmaydi.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [months])

  useEffect(() => {
    if (!groupId || selectedMonths.length === 0) {
      setData([])
      setLoading(false)
      return
    }
    let cancelled = false
    setLoading(true)
    Promise.all(
      selectedMonths.map((m) =>
        Promise.all([
          getTeacherGroupJournal(groupId, m).catch(() => null),
          getTeacherGradingBoard(groupId, m).catch(() => null),
        ]),
      ),
    )
      .then((results) => {
        if (cancelled) return
        const combined: RatingMonthData[] = results
          .filter((r): r is [GroupJournal, GradingBoard | null] => r[0] != null)
          .map(([journal, grading]) => ({ journal, grading }))
        setData(combined)
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [groupId, selectedMonths])

  const toggleMonth = (m: string) => {
    setSelectedMonths((prev) => {
      if (prev.includes(m)) {
        // Kamida 1 oy tanlangan qoladi.
        if (prev.length === 1) return prev
        return prev.filter((x) => x !== m)
      }
      return [...prev, m].sort()
    })
  }

  const monthChips = months.length > 0 && (
    <div className="flex flex-wrap gap-1.5">
      {months.map((m) => (
        <button
          key={m}
          type="button"
          onClick={() => toggleMonth(m)}
          title={monthLabel(m)}
          className={cn(
            "shrink-0 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors",
            selectedMonths.includes(m)
              ? "bg-teal-600 text-white"
              : "bg-panel3 text-mute hover:bg-tealsoft",
          )}
        >
          {monthLabel(m)}
        </button>
      ))}
    </div>
  )

  if (loading) {
    return (
      <div className="space-y-4">
        {monthChips}
        <Loader label="Reyting yuklanmoqda..." />
      </div>
    )
  }

  // Baholash mezonlari bor bo'lgan birinchi oy — mezonlar ro'yxati doim guruh darajasida bir xil.
  const criteria = data.find((d) => d.grading)?.grading?.criteria ?? []
  const hasAnyStudents = data.some((d) => (d.grading?.students.length ?? 0) > 0 || d.journal.students.length > 0)

  if (!hasAnyStudents) {
    return (
      <div className="space-y-4">
        {monthChips}
        <Card className="rounded-[20px] border border-line bg-white py-12 px-4 text-center text-faint shadow-[var(--shadow-card)]">
          <TrendingUp className="mx-auto mb-3 h-8 w-8 opacity-40" />
          <p>Baholash mezonlari topilmadi yoki o'quvchi yo'q.</p>
        </Card>
      </div>
    )
  }

  // O'quvchilar ro'yxati — tanlangan oylardagi barcha (baholash yoki jurnal) o'quvchilarning birlashmasi.
  const studentNameById = new Map<string, string>()
  for (const d of data) {
    for (const s of d.grading?.students ?? []) studentNameById.set(s.studentId, s.fullName)
    for (const s of d.journal.students) if (!studentNameById.has(s.studentId)) studentNameById.set(s.studentId, s.fullName)
  }

  // Tanlangan OYLAR bo'yicha yig'indi: jurnal bahosi, bajarilgan mezonlar (jami va har mezon bo'yicha).
  const journalTotalByStudent = new Map<string, number>()
  const doneTotalByStudent = new Map<string, number>()
  const doneByStudentCriterion = new Map<string, Map<string, number>>()
  let totalDatesSum = 0

  for (const d of data) {
    const entryMap = new Map((d.journal.entries ?? []).map((e) => [`${e.studentId}|${e.date}`, e]))
    for (const studentId of studentNameById.keys()) {
      let total = 0
      for (const col of d.journal.columns) {
        const e = entryMap.get(`${studentId}|${col.date}`)
        if (e?.grade) total += e.grade
      }
      if (total > 0) journalTotalByStudent.set(studentId, (journalTotalByStudent.get(studentId) ?? 0) + total)
    }
    if (d.grading) {
      totalDatesSum += d.grading.dates.length
      for (const s of d.grading.students) {
        const doneKeys = new Set(s.doneKeys)
        doneTotalByStudent.set(s.studentId, (doneTotalByStudent.get(s.studentId) ?? 0) + doneKeys.size)
        let critMap = doneByStudentCriterion.get(s.studentId)
        if (!critMap) {
          critMap = new Map()
          doneByStudentCriterion.set(s.studentId, critMap)
        }
        for (const key of doneKeys) {
          const [critId] = key.split("|")
          critMap.set(critId, (critMap.get(critId) ?? 0) + 1)
        }
      }
    }
  }

  const totalPossible = totalDatesSum * criteria.length

  // Har o'quvchi uchun kombindan reyting = jurnal bahosi yig'indisi + bajarilgan mezonlar yig'indisi.
  const studentStats = Array.from(studentNameById.entries())
    .map(([studentId, fullName]) => {
      const journalTotal = journalTotalByStudent.get(studentId) ?? 0
      const done = doneTotalByStudent.get(studentId) ?? 0
      const percentage = totalPossible > 0 ? Math.round((done / totalPossible) * 100) : 0
      const criteriaStats = criteria.map((crit) => ({
        criterion: crit,
        done: doneByStudentCriterion.get(studentId)?.get(crit.id) ?? 0,
        total: totalDatesSum,
      }))
      const combinedRating = journalTotal + done
      return { studentId, fullName, journalTotal, done, totalPossible, percentage, criteriaStats, combinedRating }
    })
    // Kombindan reyting bo'yicha saralash (katta → kichik)
    .sort((a, b) => b.combinedRating - a.combinedRating)

  // O'rtacha foizni hisoblash
  const avgPercentage =
    studentStats.length > 0
      ? Math.round(studentStats.reduce((s, st) => s + st.percentage, 0) / studentStats.length)
      : 0

  return (
    <div className="space-y-4">
      {monthChips}

      {/* KPI kartalar */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <RatingKpi label="O'rtacha" value={`${avgPercentage}%`} icon={TrendingUp} />
        <RatingKpi
          label="Jami o'quvchi"
          value={String(studentStats.length)}
          icon={Users}
        />
        <RatingKpi
          label="To'liq bajarildi"
          value={String(studentStats.filter((s) => s.percentage === 100).length)}
          icon={CheckCircle2}
        />
        <RatingKpi
          label="Bo'sh"
          value={String(studentStats.filter((s) => s.percentage === 0).length)}
          icon={Flag}
        />
      </div>

      {/* O'quvchilar jadvali */}
      <Card className="rounded-[20px] border border-line bg-white p-0 shadow-[var(--shadow-card)]">
        <div className="overflow-x-auto">
          <table className="min-w-full border-collapse">
            <thead>
              <tr className="bg-panel3">
                <th className="border-b border-line px-4 py-3 text-left text-xs font-semibold uppercase text-mute">
                  O'quvchi
                </th>
                <th className="border-b border-line px-3 py-3 text-center text-xs font-semibold uppercase text-mute">
                  Jurnal
                </th>
                <th className="border-b border-line px-3 py-3 text-center text-xs font-semibold uppercase text-mute">
                  Bajarildi
                </th>
                <th className="border-b border-line px-3 py-3 text-center text-xs font-semibold uppercase text-mute">
                  Jami
                </th>
                {criteria.slice(0, 3).map((crit) => (
                  <th
                    key={crit.id}
                    className="border-b border-line px-3 py-3 text-center text-xs font-semibold uppercase text-mute"
                    title={crit.name}
                  >
                    <span className="block max-w-[60px] truncate">{crit.name}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {studentStats.map((stat) => (
                <tr key={stat.studentId} className="border-b border-line-soft hover:bg-panel2">
                  <td className="px-4 py-3 text-sm font-medium text-ink">
                    {stat.fullName}
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-ink">
                    <span className="font-mono">{stat.journalTotal}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-ink">
                    <span className="font-mono">{stat.done}</span>
                    <span className="text-faint">/{stat.totalPossible}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-bold text-ink">
                    <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold', stat.combinedRating > 0 ? 'bg-tealsoft text-teal-700' : 'text-faint')}>
                      {stat.combinedRating || '—'}
                    </span>
                  </td>
                  {stat.criteriaStats.slice(0, 3).map((cs) => (
                    <td
                      key={cs.criterion.id}
                      className="px-3 py-3 text-center text-sm font-semibold text-ink"
                    >
                      <span className="font-mono text-teal-600">{cs.done}</span>
                      <span className="text-faint">/{cs.total}</span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Mezonlar bo'yicha detallar (agar ko'p bo'lsa) */}
      {criteria.length > 3 && (
        <Card className="rounded-[20px] border border-line bg-white p-4 shadow-[var(--shadow-card)]">
          <h3 className="mb-4 font-semibold text-ink">Mezonlar bo'yicha tahlil</h3>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {criteria.map((crit) => {
              let totalDone = 0
              for (const critMap of doneByStudentCriterion.values()) totalDone += critMap.get(crit.id) ?? 0
              const denom = studentStats.length * totalDatesSum
              const pct = denom > 0 ? Math.round((totalDone / denom) * 100) : 0
              return (
                <div key={crit.id} className="rounded-lg bg-panel2 p-3">
                  <p className="mb-2 text-sm font-semibold text-ink">{crit.name}</p>
                  <div className="flex items-center gap-2">
                    <div className="flex-1">
                      <div className="h-2 overflow-hidden rounded-full bg-panel3">
                        <div
                          className="h-full rounded-full bg-teal-500 transition-all"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                    <span className="font-mono text-xs font-semibold text-ink w-10 text-right">{pct}%</span>
                  </div>
                  <p className="mt-1 text-xs text-faint">
                    <span className="font-mono font-semibold">{totalDone}</span> /{" "}
                    {denom}
                  </p>
                </div>
              )
            })}
          </div>
        </Card>
      )}
    </div>
  )
}

function RatingKpi({
  label,
  value,
  icon: Icon,
}: {
  label: string
  value: string
  icon: typeof Users
}) {
  return (
    <div className="rounded-[18px] border border-line bg-white p-3 shadow-[var(--shadow-card)]">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-faint">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-lg font-semibold text-ink">{value}</div>
    </div>
  )
}

// ============================ O'quv dasturi bo'limi ============================

function CurriculumSection({
  curr,
  loading,
  open,
  onToggleOpen,
  expanded,
  onToggleTopic,
  onToggleCover,
  onChangeRevision,
  revSaving,
  nextItemId,
}: {
  curr: GroupCurriculum | null
  loading: boolean
  open: boolean
  onToggleOpen: () => void
  expanded: Set<string>
  onToggleTopic: (topicId: string) => void
  onToggleCover: (itemId: string, covered: boolean) => void
  onChangeRevision: (delta: number) => void
  revSaving: boolean
  nextItemId: string | null
}) {
  const pct = curr && curr.totalItems > 0 ? Math.round((curr.coveredCount / curr.totalItems) * 100) : 0

  return (
    <Card className="rounded-[20px] border border-line bg-white p-0 shadow-[var(--shadow-card)]">
      <button
        type="button"
        onClick={onToggleOpen}
        className="flex w-full items-center gap-2 px-4 py-3 text-left"
      >
        <ListChecks className="h-5 w-5 text-teal-600" />
        <h2 className="flex-1 font-semibold text-ink">O'quv dasturi (darsda o'tilgan)</h2>
        {curr && curr.totalItems > 0 && (
          <span className="font-mono text-sm font-semibold text-teal-700">
            {curr.coveredCount}/{curr.totalItems}
          </span>
        )}
        {open ? (
          <ChevronDown className="h-4 w-4 text-faint" />
        ) : (
          <ChevronRight className="h-4 w-4 text-faint" />
        )}
      </button>

      {open && (
        <div className="border-t border-line-soft">
          {loading && !curr ? (
            <div className="px-4 py-6">
              <Loader label="O'quv dasturi yuklanmoqda..." />
            </div>
          ) : !curr || curr.totalItems === 0 || curr.modules.length === 0 ? (
            <p className="px-4 py-10 text-center text-sm text-faint">
              Bu guruh kursida o'quv dasturi yo'q.
            </p>
          ) : (
            <>
              {/* PROGNOZ KARTASI */}
              <div className="border-b border-line-soft px-4 py-4">
                <div className="mb-4">
                  <div className="mb-1.5 flex items-center justify-between text-sm">
                    <span className="font-medium text-mute">Bajarildi</span>
                    <span className="font-mono font-semibold text-teal-700">
                      {curr.coveredCount}/{curr.totalItems} · {pct}%
                    </span>
                  </div>
                  <div className="h-2.5 w-full overflow-hidden rounded-full bg-panel3">
                    <div className="h-full rounded-full bg-teal-500 transition-all" style={{ width: `${pct}%` }} />
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <ForecastTile icon={CheckCircle2} label="O'tilgan">
                    <span className="font-mono">{curr.coveredCount}</span>
                    <span className="text-faint">/{curr.totalItems}</span>
                  </ForecastTile>
                  <ForecastTile icon={Repeat} label="Takrorlash">
                    <span className="font-mono">{curr.revisionLessons}</span>
                  </ForecastTile>
                  <ForecastTile icon={Flag} label="Qolgan">
                    <span className="font-mono">{curr.remainingItems}</span> band
                  </ForecastTile>
                  <ForecastTile icon={CalendarClock} label="Tugatishga">
                    <span className="text-mute">~</span>
                    <span className="font-mono">{curr.estLessonsLeft}</span> dars
                    {curr.estFinishDate && (
                      <span className="mt-0.5 block text-xs font-normal text-faint">
                        ≈ {formatDate(curr.estFinishDate)} da
                      </span>
                    )}
                  </ForecastTile>
                </div>

                <div className="mt-4 flex flex-wrap items-center gap-2">
                  <button
                    type="button"
                    disabled={revSaving}
                    onClick={() => onChangeRevision(1)}
                    className="inline-flex items-center gap-1.5 rounded-lg bg-tealsoft px-3 py-2 text-sm font-medium text-teal-700 transition-colors hover:bg-teal-100 disabled:opacity-50"
                  >
                    <Plus className="h-4 w-4" /> Takrorlash darsi
                  </button>
                  <button
                    type="button"
                    disabled={revSaving || curr.revisionLessons <= 0}
                    onClick={() => onChangeRevision(-1)}
                    title="Oxirgi takrorlash darsini olib tashlash"
                    className="inline-flex items-center justify-center rounded-lg border border-line bg-white p-2 text-mute transition-colors hover:bg-panel2 disabled:opacity-40"
                  >
                    <Minus className="h-4 w-4" />
                  </button>
                </div>
              </div>

              {/* DASTUR DARAXTI — modullar → mavzular (tekis, to'liq kenglik) */}
              <div className="divide-y divide-line-soft">
                {curr.modules.map((module) => {
                  const mItems = module.topics.flatMap((tp) => tp.items)
                  const mCovered = mItems.filter((it) => it.covered).length
                  const moduleOpen = expanded.has(module.id)
                  const mComplete = mItems.length > 0 && mCovered === mItems.length
                  return (
                    <div key={module.id} className="bg-panel2/60">
                      <button
                        type="button"
                        onClick={() => onToggleTopic(module.id)}
                        className="flex w-full items-center gap-2.5 px-4 py-3 text-left transition-colors hover:bg-panel2"
                      >
                        <span className="shrink-0 text-faint">
                          {moduleOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                        </span>
                        <span className="min-w-0 flex-1 truncate text-sm font-bold text-ink">{module.name}</span>
                        <span
                          className={cn(
                            "shrink-0 rounded-full px-2.5 py-1 text-xs font-medium",
                            mComplete ? "bg-emerald-50 text-emerald-700" : "bg-panel3 text-mute",
                          )}
                        >
                          <span className="font-mono">
                            {mCovered}/{mItems.length}
                          </span>
                        </span>
                      </button>

                      {moduleOpen && (
                        <div className="divide-y divide-line-soft bg-white">
                          {module.topics.map((topic) => {
                  const tCovered = topic.items.filter((it) => it.covered).length
                  const tOpen = expanded.has(topic.id)
                  const complete = topic.items.length > 0 && tCovered === topic.items.length
                  return (
                    <div key={topic.id}>
                      <button
                        type="button"
                        onClick={() => onToggleTopic(topic.id)}
                        className="flex w-full items-center gap-2.5 px-4 py-3 text-left transition-colors hover:bg-panel2"
                      >
                        <span className="shrink-0 text-faint">
                          {tOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                        </span>
                        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-tealsoft text-teal-600">
                          <BookOpen className="h-4 w-4" />
                        </span>
                        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-ink">
                          {topic.title}
                        </span>
                        <span
                          className={cn(
                            "shrink-0 rounded-full px-2.5 py-1 text-xs font-medium",
                            complete ? "bg-emerald-50 text-emerald-700" : "bg-panel3 text-mute",
                          )}
                        >
                          <span className="font-mono">
                            {tCovered}/{topic.items.length}
                          </span>
                        </span>
                      </button>

                      {tOpen && (
                        <div className="bg-panel2 pb-1.5">
                          {topic.note && <p className="px-4 pb-1 pt-2 text-xs text-faint">{topic.note}</p>}
                          {topic.items.length === 0 ? (
                            <p className="px-4 py-2 text-xs text-faint">Topshiriq yo'q.</p>
                          ) : (
                            topic.items.map((item) => {
                              const isNext = item.id === nextItemId
                              return (
                                <label
                                  key={item.id}
                                  className={cn(
                                    "flex cursor-pointer items-center gap-2.5 px-4 py-2 transition-colors",
                                    item.covered
                                      ? "bg-emerald-50/40"
                                      : isNext
                                        ? "bg-tealsoft"
                                        : "hover:bg-white",
                                  )}
                                >
                                  <input
                                    type="checkbox"
                                    checked={item.covered}
                                    onChange={() => onToggleCover(item.id, !item.covered)}
                                    className="h-4 w-4 shrink-0 cursor-pointer rounded border-line text-teal-600 focus:ring-teal-400"
                                  />
                                  <span
                                    className={cn(
                                      "min-w-0 flex-1 text-sm",
                                      item.covered ? "text-faint line-through" : "text-ink",
                                    )}
                                  >
                                    {item.text}
                                    {isNext && (
                                      <span className="ml-2 rounded bg-tealsoft px-1.5 py-0.5 text-[10px] font-medium text-teal-700 no-underline">
                                        keyingi
                                      </span>
                                    )}
                                  </span>
                                  {item.covered && item.coveredDate && (
                                    <span className="shrink-0 self-center whitespace-nowrap rounded bg-white px-1.5 py-0.5 font-mono text-[11px] text-faint no-underline">
                                      {formatDate(item.coveredDate)}
                                    </span>
                                  )}
                                </label>
                              )
                            })
                          )}
                        </div>
                      )}
                    </div>
                  )
                          })}
                        </div>
                      )}
                    </div>
                  )
                })}
              </div>
            </>
          )}
        </div>
      )}
    </Card>
  )
}

function ForecastTile({
  icon: Icon,
  label,
  children,
}: {
  icon: typeof BookOpen
  label: string
  children: ReactNode
}) {
  return (
    <div className="rounded-[18px] border border-line bg-white p-3 shadow-[var(--shadow-card)]">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-faint">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-sm font-semibold text-ink">{children}</div>
    </div>
  )
}
