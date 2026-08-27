import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { usePerm, useSuperOrGranted } from '@/lib/permissions'
import {
  ArrowLeft, Users, BookOpen, User, Archive,
  CalendarDays, Clock, MapPin, Wallet, Snowflake, CheckCircle2,
  ListChecks, ChevronRight, ChevronDown, Plus, Minus, Repeat, CalendarClock, Flag, TrendingUp, Trophy,
  ArrowLeftRight, RotateCcw, X, Pencil, ClipboardList, CalendarCheck, History,
  UserPlus, MessageSquare, ArrowUpDown, Sparkles, EyeOff, PhoneCall, Settings2,
} from 'lucide-react'
import type { AbsenceReason, MasteryLevel, Group, GroupMember } from '@/types'
import {
  getGroupJournal, setJournalEntry, clearJournalEntry, bulkAttendance,
  rescheduleLesson, cancelReschedule,
  type GroupJournal, type PaymentHiddenReason,
} from '@/api/services/journal'
import {
  getGroupCurriculum, setGroupCover, changeGroupRevision,
  type GroupCurriculum,
} from '@/api/services/curriculum'
import {
  activateMember, freezeMember, returnMemberToTrial, getClasses, updateClass,
  getGroupMembers, removeGroupMember, type ClassPayload,
  bulkFreezeMembers, bulkActivateMembers, bulkMembershipSummary,
} from '@/api/services/classes'
import { getStudents } from '@/api/services/students'
import { getGroupPayments, type GroupPaymentsReport } from '@/api/services/finance'
import { GroupTestsPanel } from '../tests/GroupTestsPanel'
import { getSettings } from '@/api/services/settings'
import { cn, formatMoney, formatDate, apiErrorMessage, gradeBadgeCls, balanceTextCls, balanceDotCls, balanceTitle, HEAVY_DEBT_MONTHS } from '@/lib/utils'
import { backState, type BackState } from '@/lib/nav'
import { Card } from '@/components/ui/Card'
import { DropdownMenu } from '@/components/ui/DropdownMenu'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { GroupContactTab } from '@/components/contacts/GroupContactTab'
import { getActionReasons } from '@/api/services/actionReasons'
import { createContactRequestsBulk } from '@/api/services/contacts'
import { GradingSection } from '@/components/grading/GradingSection'
import {
  getGradingBoard, setGrade, bulkGrade,
  type GradingBoard, type GradingBoardCriterion,
} from '@/api/services/grading'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { JournalCellModal } from '../journal/JournalCellModal'
import { CompleteAndTransferModal } from './CompleteAndTransferModal'
import { CloseGroupModal } from './CloseGroupModal'
import { TransferGroupModal } from './TransferGroupModal'
import { ClassMembersModal } from './ClassMembersModal'
import { GroupAiPanel } from './GroupAiPanel'
import { ClassFormModal } from './ClassFormModal'
import { SmsModal, type SmsRecipient } from '../students/SmsModal'

const weekdayShort = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']
const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m

/** Reyting tabidagi bitta oy uchun jurnal+baholash ma'lumoti. */
interface RatingMonthData {
  month: string
  journal: GroupJournal | null
  grading: GradingBoard | null
}

function statusBadge(status: string): { label: string; cls: string } {
  switch (status) {
    case 'active':
      return { label: 'Aktiv', cls: 'bg-emerald-50 text-emerald-700' }
    case 'frozen':
      return { label: 'Muzlatilgan', cls: 'bg-sky-50 text-sky-700' }
    default:
      return { label: 'Sinov', cls: 'bg-amber-50 text-amber-700' }
  }
}

/** "O'qituvchida ko'rinmaydi" belgisining tooltip matni — sababi bo'yicha (Jurnal boshqaruvi →
 *  To'lov nazorati). Bu muzlatish emas: to'lov qilinishi bilan qator o'qituvchida ham qaytadi. */
function paymentHiddenTitle(reason: PaymentHiddenReason): string {
  const base =
    reason === 'prevMonth'
      ? "O'tgan oy uchun to'lov yo'q"
      : reason === 'cutoff'
        ? "Joriy oy uchun belgilangan sanagacha to'lov yo'q"
        : "To'lov nazorati sozlamasi bo'yicha yashirilgan"
  return `${base} — o'qituvchi jurnalida ko'rinmaydi (muzlatish emas: hisob davom etadi, to'lovdan keyin qaytadi).`
}

/** Mastery darajasi — rangi va yorlig'i */
function masteryDisplay(m: MasteryLevel | undefined): { label: string; cls: string } {
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

type Tab = 'jurnal' | 'davomat' | 'baholash' | 'reyting' | 'imtihonlar' | 'dastur' | 'aloqa' | 'tarix' | 'tolovlar' | 'ai'

export function ClassDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const { user } = useAuth()
  const { can } = usePerm()
  // O'zgarishlar tarixi — alohida `audit` ruxsati (admin/superadmin uchun har doim true).
  const canSeeAudit = can('audit', 'view')
  /** "Aloqa" tabi — o'quvchini bog'lanish navbatiga yuborish (`contacts` ruxsati). */
  const canContact = can('contacts', 'create')
  // «Bonus hisoblansin» ptichkasi — FAQAT superadmin yoki «Xodimlar va rollar» dan shu ruxsat
  // berilgan xodim uchun. Oddiy "admin" roli ham ko'rmaydi — shuning uchun can() emas.
  const canSetBonus = useSuperOrGranted('retentionBonus')
  const [journal, setJournal] = useState<GroupJournal | null>(null)
  const [reasons, setReasons] = useState<AbsenceReason[]>([])
  const [loading, setLoading] = useState(true)
  const [cell, setCell] = useState<{ studentId: string; studentName: string; date: string } | null>(null)
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
  /** CHAP ustundagi a'zolar ro'yxati (TO'LIQ tarix — chiqqan/muzlatilgan/sinov/aktiv). */
  const [members, setMembers] = useState<GroupMember[]>([])
  /** A'zolar ro'yxatini ism bo'yicha alfavit tartibida saralash (A-Z / Z-A). */
  const [membersSortAsc, setMembersSortAsc] = useState(true)
  /** Ro'yxatdagi "⋮" menyudan tanlangan a'zo + amal. */
  const [rosterTarget, setRosterTarget] = useState<GroupMember | null>(null)
  const [rosterReason, setRosterReason] = useState<'freeze' | 'return' | 'remove' | 'activate' | null>(null)
  const [rosterTransferOpen, setRosterTransferOpen] = useState(false)
  const [rosterDate, setRosterDate] = useState('')
  const [rosterBusy, setRosterBusy] = useState(false)
  /** Guruh "⋮" menyusidan OMMAVIY amal — qaysi amal uchun tasdiq modali ochiq (`null` = yopiq). */
  const [groupBulk, setGroupBulk] = useState<'freeze' | 'activate' | null>(null)
  /** Ommaviy amal so'rov jarayonida — takroriy bosishni bloklaydi. */
  const [groupBulkBusy, setGroupBulkBusy] = useState(false)
  /** Guruh o'qituvchisi id'si — profilga link uchun (jurnal DTO'sida faqat teacherName bor). */
  const [teacherId, setTeacherId] = useState('')
  /** "Yangi o'quvchi" yaratish formasi ochiqmi — yaratilgach shu guruhga qo'shiladi. */
  /** A'zo qo'shish modali (mavjud o'quvchini qidirish yoki yangi yaratish) ochiqmi. */
  const [membersOpen, setMembersOpen] = useState(false)
  /** Guruh obyekti (ClassMembersModal uchun) — getClasses'dan. */
  const [group, setGroup] = useState<Group | null>(null)
  /** "Guruhni tahrirlash" — sarlavha "⋮" menyusidan. */
  const [editOpen, setEditOpen] = useState(false)
  /** "SMS jo'natish" (guruhga) — sarlavha "⋮" menyusidan; faol a'zolarning to'liq ma'lumoti. */
  const [smsOpen, setSmsOpen] = useState(false)
  const [smsRecipients, setSmsRecipients] = useState<SmsRecipient[]>([])
  const [smsLoading, setSmsLoading] = useState(false)

  // ---- O'ng ustundagi faol bo'lim (tab) ----
  const [tab, setTab] = useState<Tab>('jurnal')
  /** "Oylik to'lov yig'ilishi" tabi — moliya ruxsati bor xodimga (o'qituvchiga KO'RINMAYDI:
   *  hisobot endpointi ham "finance" ruxsatini talab qiladi). */
  const canSeePayments = user?.role !== 'teacher' && can('finance.main', 'view')
  const [grading, setGrading] = useState<GradingBoard | null>(null)
  const [curr, setCurr] = useState<GroupCurriculum | null>(null)
  // ---- Reyting — ko'p-oylik filtr ----
  const [ratingMonths, setRatingMonths] = useState<string[]>([])
  const [ratingData, setRatingData] = useState<RatingMonthData[]>([])
  const [ratingLoading, setRatingLoading] = useState(false)
  const ratingCache = useRef(new Map<string, { journal: GroupJournal | null; grading: GradingBoard | null }>())
  const [currLoading, setCurrLoading] = useState(true)
  const [currExpanded, setCurrExpanded] = useState<Set<string>>(new Set())
  const [revSaving, setRevSaving] = useState(false)

  // ---- "Guruhni tugatish" modali ----
  const [showCompleteModal, setShowCompleteModal] = useState(false)
  // ---- "Guruhni yopish" (barchasini muzlatish + arxivga olish) modali ----
  const [showCloseModal, setShowCloseModal] = useState(false)

  const openCompleteModal = () => setShowCompleteModal(true)

  /** "Guruhni tahrirlash" tasdiqlangach — oddiy saqlash (fee-prompt/xona-konflikt kengaytirilgan
   *  oynasisiz; kerak bo'lsa to'liq tahrirlash Guruhlar ro'yxati sahifasidan qilinadi). */
  const handleEditSubmit = async (values: ClassPayload) => {
    if (!id) return
    try {
      const res = await updateClass(id, values)
      const any = res as unknown as Record<string, unknown>
      if (any.roomConflict) {
        alert("Bu xona shu vaqtda band — boshqa xona yoki vaqt tanlang.")
        return
      }
      setGroup(res)
      setEditOpen(false)
      load(journal?.month)
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    }
  }

  /** "SMS jo'natish" (guruhga) — faol a'zolarning to'liq (telefon raqamli) ma'lumotini yuklaydi.
   *  A'zolik holati va SHU GURUH balansi ham uzatiladi — modal ichida "aktiv/sinov/muzlatilgan" va
   *  "faqat qarzdorlar" filtrlari shularga tayanadi. */
  const openGroupSms = async () => {
    if (smsLoading) return
    setSmsLoading(true)
    try {
      const byId = new Map(members.filter((m) => m.isActive).map((m) => [m.studentId, m]))
      const all = await getStudents()
      setSmsRecipients(
        all
          .filter((s) => byId.has(s.id))
          .map((s) => ({
            id: s.id, fullName: s.fullName, phone: s.phone,
            parentPhone: s.parentPhone, fatherPhone: s.fatherPhone, motherPhone: s.motherPhone,
            status: byId.get(s.id)!.status, balance: byId.get(s.id)!.balance,
          })),
      )
      setSmsOpen(true)
    } catch {
      alert("O'quvchilar ma'lumotini yuklab bo'lmadi")
    } finally {
      setSmsLoading(false)
    }
  }

  const loadCurr = useCallback(() => {
    if (!id) return
    setCurrLoading(true)
    getGroupCurriculum(id)
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
      getGroupJournal(id, month)
        .then(setJournal)
        .finally(() => setLoading(false))
    },
    [id],
  )

  const loadGrading = useCallback(
    (month?: string) => {
      if (!id) return
      getGradingBoard(id, month)
        .then(setGrading)
        .catch(() => setGrading(null))
    },
    [id],
  )

  /** Chap ustundagi a'zolar ro'yxatini qayta yuklaydi (TO'LIQ tarix — barcha a'zoliklar). */
  const reloadMembers = useCallback(() => {
    if (!id) return
    getGroupMembers(id).then(setMembers).catch(() => setMembers([]))
  }, [id])

  useEffect(() => {
    reloadMembers()
  }, [reloadMembers])

  useEffect(() => {
    load()
    getSettings()
      .then((s) => setReasons(s.absenceReasons))
      .catch(() => {})
  }, [load])

  // Guruh o'qituvchisi id'si — "O'qituvchi" nomini profilga link qilish uchun (jurnal DTO'sida yo'q).
  useEffect(() => {
    if (!id) return
    // includeArchived=true — TUGATILGAN (arxivlangan) guruh sahifasi ham to'liq ochilsin
    // (o'qituvchi profilidan yoki havola orqali kirilganda ma'lumotlari ko'rinadi).
    getClasses(true)
      .then((cs) => {
        const cls = cs.find((c) => c.id === id)
        setGroup(cls ?? null)
        setTeacherId(cls?.teacherId ?? '')
      })
      .catch(() => {
        setGroup(null)
        setTeacherId('')
      })
  }, [id])

  useEffect(() => {
    if (tab === 'reyting' && journal?.month) {
      loadGrading(journal.month)
    }
  }, [tab, journal?.month, loadGrading])

  // Guruh o'zgarsa — reyting keshi va tanlovi tozalanadi.
  useEffect(() => {
    ratingCache.current.clear()
    setRatingMonths([])
    setRatingData([])
  }, [id])

  // Reyting tab birinchi ochilganda default — joriy jurnal oyi.
  useEffect(() => {
    if (tab === 'reyting' && ratingMonths.length === 0 && journal?.month) {
      setRatingMonths([journal.month])
    }
  }, [tab, journal?.month, ratingMonths.length])

  // Tanlangan oylar bo'yicha jurnal+baholash — keshlangan holda yuklanadi.
  useEffect(() => {
    if (tab !== 'reyting' || !id || ratingMonths.length === 0) return
    let cancelled = false
    setRatingLoading(true)
    Promise.all(
      ratingMonths.map(async (m): Promise<RatingMonthData> => {
        const cached = ratingCache.current.get(m)
        if (cached) return { month: m, journal: cached.journal, grading: cached.grading }
        try {
          const [j, gr] = await Promise.all([getGroupJournal(id, m), getGradingBoard(id, m)])
          ratingCache.current.set(m, { journal: j, grading: gr })
          return { month: m, journal: j, grading: gr }
        } catch {
          ratingCache.current.set(m, { journal: null, grading: null })
          return { month: m, journal: null, grading: null }
        }
      }),
    )
      .then((results) => {
        if (!cancelled) setRatingData(results)
      })
      .finally(() => {
        if (!cancelled) setRatingLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [tab, ratingMonths, id])

  // Reyting oy chipini bosish — toggle, lekin kamida 1 oy tanlangan qoladi.
  const toggleRatingMonth = (m: string) => {
    setRatingMonths((prev) => {
      if (prev.includes(m)) {
        if (prev.length === 1) return prev
        return prev.filter((x) => x !== m)
      }
      return [...prev, m].sort()
    })
  }

  const reasonById = useMemo(
    () => new Map(reasons.map((r) => [r.id, r])),
    [reasons],
  )
  const entryMap = useMemo(
    () => new Map((journal?.entries ?? []).map((e) => [`${e.studentId}|${e.date}`, e])),
    [journal],
  )
  // "O'tildi" deb belgilangan darslar — sababsiz o'quvchi shu kunda KELDI (yashil) deb ko'rsatiladi.
  const conductedSet = useMemo(() => new Set(journal?.conductedDates ?? []), [journal])
  // Muzlatilganlar jurnalga QO'SHILMAYDI — grid'da faqat faol/sinov o'quvchilar, muzlatilganlar pastda alohida.
  // O'quvchilari jurnalga kiritilgan va baholashga kiritilgan baholarning yig'indisi bo'yicha saralash.
  // Agar baholash ma'lumoti bo'lsa, unga bog'langan kombindan yig'indi; aks holda faqat jurnal baholari.
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

  /** Jurnal jadvali qatorlari tartibi: 'score' — ball bo'yicha (standart), 'name' — A-Z/Z-A. */
  const [journalSort, setJournalSort] = useState<'score' | 'nameAsc' | 'nameDesc'>('score')
  const journalStudents = useMemo(() => {
    if (journalSort === 'score') return sortedAndScoredStudents
    const list = [...sortedAndScoredStudents].sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz'))
    return journalSort === 'nameAsc' ? list : list.reverse()
  }, [sortedAndScoredStudents, journalSort])
  const frozenStudents = useMemo(
    () => (journal?.students ?? []).filter((s) => s.status === 'frozen'),
    [journal],
  )
  /** Muzlatilganlar bo'limi — sarlavha tugmasi bilan ochiladi/yopiladi. null = foydalanuvchi hali
   *  tegmagan: standart YOPIQ, lekin guruhda faol o'quvchi qolmagan bo'lsa (masalan guruh yopilgan/
   *  tugatilgan) OCHIQ — aks holda jurnal bo'm-bo'sh ko'rinardi. */
  const [frozenOpen, setFrozenOpen] = useState<boolean | null>(null)
  /** Muzlatilganlar bloki hozir ochiqmi (foydalanuvchi tanlovi, aks holda avtomatik qoida). */
  const frozenVisible = frozenOpen ?? (journalStudents.length === 0 && frozenStudents.length > 0)
  /** Shu guruhdagi FAOL o'quvchilar soni (status === 'active') — o'qituvchi hisoboti "Faol" bilan bir xil ta'rif. */
  const activeCount = useMemo(
    () => (journal?.students ?? []).filter((s) => s.status === 'active').length,
    [journal],
  )

  /** Davomat tabidagi jadval — tanlangan oy uchun har o'quvchi davomat foizi (jurnal ma'lumotidan).
   *  O'tilgan darslar = conductedDates ∩ (memberStart ≤ sana ≤ frozenAt); qoldirgan = sababli
   *  (kech kelish MUSTASNO). Muzlatilgandan KEYINGI darslar hisobga olinmaydi — jurnal jadvali
   *  va o'quvchi portali (StudentAttendanceController memberEnd) bilan bir xil konvensiya. */
  const attendanceRows = useMemo(() => {
    if (!journal) return []
    const conducted = journal.conductedDates ?? []
    return (journal.students ?? []).map((s) => {
      const start = s.memberStart || ''
      const end = s.frozenAt || ''
      const held = conducted.filter((d) => (!start || d >= start) && (!end || d <= end))
      let absent = 0
      for (const d of held) {
        const e = entryMap.get(`${s.studentId}|${d}`)
        if (e?.reasonId) {
          const r = reasonById.get(e.reasonId)
          if (r && !r.isLate) absent++
        }
      }
      const heldCount = held.length
      const pct = heldCount > 0 ? Math.round(((heldCount - absent) / heldCount) * 100) : null
      return { studentId: s.studentId, fullName: s.fullName, status: s.status, held: heldCount, absent, pct }
    })
  }, [journal, entryMap, reasonById])

  const g = journal?.group
  const today = new Date().toISOString().slice(0, 10)

  const handleSave = async (
    grade: number | null,
    reasonId: string | null,
    homework: number,
    behavior: number,
    mastery: MasteryLevel | null,
    present: boolean,
  ) => {
    if (!journal || !cell) return
    setSaving(true)
    setCellError(null)
    try {
      await setJournalEntry(journal.group.id, journal.group.courseId, 1, cell.studentId, cell.date, 1, {
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
      await clearJournalEntry(journal.group.id, journal.group.courseId, 1, cell.studentId, cell.date, 1)
      setCell(null)
      load(journal.month)
    } catch (err) {
      setCellError(apiErrorMessage(err, "Tozalab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  // ---- CHAP ustundagi a'zolar ro'yxati amallari ("⋮" menyudan; StudentDetailPage bilan bir xil oqim) ----
  /** "⋮" menyu — amal boshlanishidan oldin qaysi a'zolikka tegishli ekanini belgilaydi. */
  const openRoster = (m: GroupMember) => {
    setRosterDate(today)
    setRosterTarget(m)
  }
  /** O'quvchi holatiga qarab "chiqarish" sabab kategoriyasi (ClassMembersModal bilan bir xil). */
  const rosterRemoveCategory = (status: string) =>
    status === 'active' ? 'remove_active' : status === 'frozen' ? 'remove_frozen' : 'remove_trial'
  /** Muzlatish/sinovga qaytarish/chiqarish/aktivlashtirish (sanali) — sabab modali tasdiqlangach. */
  const confirmRosterReason = async (
    reasonId: string | undefined,
    date?: string,
    retentionBonus?: boolean,
  ) => {
    if (!rosterTarget || !rosterReason || rosterBusy) return
    setRosterBusy(true)
    try {
      if (rosterReason === 'freeze') await freezeMember(id, rosterTarget.studentId, date ?? rosterDate, reasonId)
      // Bonus ptichkasi faqat aktivlashtirish oynasida bo'ladi — boshqa oqimlarda `undefined`.
      else if (rosterReason === 'activate') await activateMember(id, rosterTarget.studentId, date ?? rosterDate, retentionBonus)
      else if (rosterReason === 'remove') await removeGroupMember(id, rosterTarget.studentId, reasonId)
      else await returnMemberToTrial(id, rosterTarget.studentId, reasonId)
      setRosterReason(null)
      setRosterTarget(null)
      reloadMembers()
      load(journal?.month)
    } catch (err) {
      alert(apiErrorMessage(err, 'Amal bajarilmadi'))
    } finally {
      setRosterBusy(false)
    }
  }

  /**
   * "⋮" menyudagi OMMAVIY amal uchun mos a'zolar. Menyuda son ko'rinib turadi, ya'ni
   * foydalanuvchi bosishdan OLDIN nechta odamga tegishini biladi — "hammasini muzlatish"
   * jimgina 0 ta (yoki kutilmaganda 40 ta) odamga tegib ketmasin.
   */
  const bulkFreezeTargets = useMemo(
    () => members.filter((m) => m.isActive && (m.status === 'active' || m.status === 'trial')),
    [members],
  )
  const bulkActivateTargets = useMemo(
    () => members.filter((m) => m.isActive && m.status !== 'active'),
    [members],
  )

  /**
   * Guruhning BARCHA mos a'zolarini bir paytda muzlatish/aktivlashtirish ("⋮" menyudan).
   *
   * Tanlab qilish kerak bo'lsa — "A'zolar → Boshqarish" modalida checkbox bor; bu yerdagi
   * yo'l ATAYIN "hammasi" uchun, chunki menyuda tanlov ko'rinmaydi. Id'lar baribir OCHIQ
   * yuboriladi (server "hammasi"ni o'zi qidirmaydi) — ya'ni ekranda ko'rilgan ro'yxat
   * bilan amalga ketgan ro'yxat AYNAN bir xil bo'ladi.
   */
  const confirmGroupBulk = async (
    reasonId: string | undefined,
    date?: string,
    retentionBonus?: boolean,
  ) => {
    if (!groupBulk || groupBulkBusy) return
    const action = groupBulk
    const targets = action === 'freeze' ? bulkFreezeTargets : bulkActivateTargets
    const ids = targets.map((m) => m.studentId)
    if (ids.length === 0) {
      setGroupBulk(null)
      return
    }
    const day = date || today
    setGroupBulkBusy(true)
    try {
      const res = action === 'freeze'
        ? await bulkFreezeMembers(id, ids, day, reasonId)
        // Bonus ptichkasi ruxsatsiz bo'lsa umuman yuborilmaydi (server ham shu qoidani tekshiradi).
        : await bulkActivateMembers(id, ids, day, canSetBonus ? retentionBonus : undefined)
      setGroupBulk(null)
      reloadMembers()
      load(journal?.month)
      alert(bulkMembershipSummary(res, action))
    } catch (err) {
      // Modal YOPILADI: uning ichki "submitting" bayrog'i faqat `open` o'zgarganda tiklanadi,
      // ochiq qoldirsak tasdiq tugmasi abadiy o'chiq qolardi.
      setGroupBulk(null)
      alert(apiErrorMessage(err, 'Ommaviy amal bajarilmadi'))
    } finally {
      setGroupBulkBusy(false)
    }
  }

  const absentReasons = useMemo(() => reasons.filter((r) => !r.isLate), [reasons])

  // Mavzu yoyish/yig'ish (default — yopiq)
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

  // Band belgilash — optimistik (mahalliy holatni darhol yangilaymiz), so'ng refetch (prognoz aniq qolishi uchun)
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
      await setGroupCover(id, itemId, covered)
      loadCurr()
    } catch {
      setCurr(prev)
      alert('Saqlab bo\'lmadi')
    }
  }

  // Takrorlash darsi +1 / -1
  const changeRevision = async (delta: number) => {
    if (!curr || revSaving) return
    setRevSaving(true)
    try {
      await changeGroupRevision(id, delta)
      loadCurr()
    } catch {
      alert('Saqlab bo\'lmadi')
    } finally {
      setRevSaving(false)
    }
  }

  // Sarlavhadagi sana bosilganda — shu darsdagi BARCHA o'quvchiga birdan davomat.
  // absent=false → hammasi keldi; true → hammasi kelmadi (reasonId berilsa shu sabab, aks holda standart).
  const doBulk = async (absent: boolean, reasonId: string | null) => {
    if (!journal || !bulkDate) return
    setBulkSaving(true)
    try {
      await bulkAttendance(
        journal.group.id,
        journal.group.courseId,
        bulkDate,
        1,
        journalStudents.map((s) => s.studentId),
        { absent, reasonId },
      )
      setBulkDate(null)
      load(journal.month)
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setBulkSaving(false)
    }
  }

  // Chap ustundagi a'zolar ro'yxati — ism bo'yicha alfavit tartibida (A-Z yoki Z-A).
  const sortedMembers = useMemo(() => {
    const list = [...members].sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz'))
    return membersSortAsc ? list : list.reverse()
  }, [members, membersSortAsc])

  // Bu sahifadan o'quvchi profiliga o'tilganda "Orqaga" havolasi o'quvchilar ro'yxatiga emas,
  // SHU GURUHGA qaytarsin — havolalarga `state` sifatida uzatiladi (qarang `lib/nav.ts`).
  const memberBack = useMemo<BackState>(
    () => backState(`/admin/classes/${id}`, journal?.group?.name ?? group?.name ?? 'Guruhga qaytish'),
    [id, journal?.group?.name, group?.name],
  )

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
      await rescheduleLesson(journal.group.id, bulkDate, rToDate, rTime || undefined)
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
      await cancelReschedule(rescheduleId)
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
    <div className="space-y-6">
      <div>
        <Link
          to="/admin/classes"
          className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 transition-colors hover:text-slate-700"
        >
          <ArrowLeft className="h-4 w-4" /> Barcha guruhlar
        </Link>
      </div>

      {loading && !journal ? (
        <Loader label="Yuklanmoqda..." />
      ) : !g ? (
        <Card className="py-16 text-center text-slate-400">Guruh topilmadi</Card>
      ) : (
        <>
          <div className="grid grid-cols-1 gap-6 lg:grid-cols-[25%_75%]">
            {/* CHAP USTUN — guruh ma'lumoti + a'zolar ro'yxati (bitta karta), 25% */}
            <div className="lg:sticky lg:top-4 lg:self-start">
              <Card className="space-y-5">
                {/* Sarlavha */}
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h1 className="flex flex-wrap items-center gap-2 text-xl font-semibold text-slate-800">
                      {g.name}
                      {/* TUGATILGAN guruh — ma'lumotlari faqat ko'rish uchun ochiq. */}
                      {group?.isArchived && (
                        <span className="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
                          Tugatilgan{group.archivedAt ? ` · ${formatDate(group.archivedAt)}` : ''}
                        </span>
                      )}
                      {/* VAQTINCHA BLOKLANGAN — guruh o'qituvchi ilovasida ko'rinmaydi
                          (admin uchun hammasi ochiq). Blokdan chiqarish — "Guruhlar" ro'yxatida. */}
                      {group?.isBlocked && (
                        <span
                          className="rounded bg-orange-100 px-2 py-0.5 text-xs font-medium text-orange-700"
                          title={group.blockNote || undefined}
                        >
                          Bloklangan — o'qituvchida ko'rinmaydi
                          {group.blockedAt ? ` · ${formatDate(group.blockedAt)}` : ''}
                        </span>
                      )}
                    </h1>
                    <p className="mt-0.5 break-words text-sm text-slate-400">
                      {g.courseName || 'Kurs biriktirilmagan'}
                      {g.teacherName && (
                        <>
                          {' · '}
                          {teacherId ? (
                            <Link to={`/admin/teachers/${teacherId}`} className="text-slate-500 hover:text-brand-600 hover:underline">
                              {g.teacherName}
                            </Link>
                          ) : (
                            g.teacherName
                          )}
                        </>
                      )}
                    </p>
                  </div>
                  <DropdownMenu
                    items={[
                      ...(can('classes.list', 'create')
                        ? [{
                            label: 'Yangi talaba qo\'shish', icon: UserPlus,
                            onClick: () => setMembersOpen(true),
                          }]
                        : []),
                      // OMMAVIY muzlatish/aktivlashtirish — guruhning HAMMA mos a'zosiga.
                      // Bandi mos a'zo BO'LMASA umuman ko'rsatilmaydi: "0 ta" ni bosib,
                      // hech narsa bo'lmagani chalg'itardi. Tanlab qilish — "A'zolar → Boshqarish".
                      ...(can('classes.list', 'create') && bulkFreezeTargets.length > 0
                        ? [{
                            label: `Hammasini muzlatish (${bulkFreezeTargets.length})`, icon: Snowflake,
                            onClick: () => setGroupBulk('freeze'),
                          }]
                        : []),
                      ...(can('classes.list', 'create') && bulkActivateTargets.length > 0
                        ? [{
                            label: `Hammasini aktivlashtirish (${bulkActivateTargets.length})`, icon: CheckCircle2,
                            onClick: () => setGroupBulk('activate'),
                          }]
                        : []),
                      {
                        label: smsLoading ? 'Yuklanmoqda...' : 'SMS jo\'natish', icon: MessageSquare,
                        onClick: openGroupSms,
                      },
                      ...(can('classes.list', 'edit')
                        ? [{ label: 'Guruhni tahrirlash', icon: Pencil, onClick: () => setEditOpen(true) }]
                        : []),
                      // Tugatish/yopish — faqat FAOL guruhda (arxivdagi guruh allaqachon yopilgan).
                      ...(user?.role === 'superadmin' && can('classes.list', 'delete') && !group?.isArchived
                        ? [
                            { label: 'Tugatish (sertifikat bilan)', icon: Trophy, onClick: openCompleteModal },
                            { label: 'Guruhni yopish', icon: Archive, onClick: () => setShowCloseModal(true) },
                          ]
                        : []),
                    ]}
                  />
                </div>

                {/* Ma'lumot bloki — 2 ustunli grid */}
                <div className="grid grid-cols-2 gap-x-3 gap-y-4 border-t border-slate-100 pt-4">
                  <Info icon={BookOpen} label="Kurs" value={g.courseName || '—'} />
                  <Info
                    icon={User}
                    label="O'qituvchi"
                    value={
                      g.teacherName && teacherId ? (
                        <Link to={`/admin/teachers/${teacherId}`} className="hover:text-brand-600 hover:underline">
                          {g.teacherName}
                        </Link>
                      ) : (
                        g.teacherName || '—'
                      )
                    }
                  />
                  <Info icon={Wallet} label="Oylik to'lov" value={formatMoney(g.monthlyFee)} mono />
                  <Info
                    icon={CalendarDays}
                    label="Dars kunlari"
                    value={g.days.length ? g.days.map((d) => weekdayShort[d] ?? d).join(', ') : '—'}
                  />
                  <Info
                    icon={Clock}
                    label="Dars vaqti"
                    value={g.startTime || g.endTime ? `${g.startTime || '—'}${g.endTime ? ` – ${g.endTime}` : ''}` : '—'}
                    mono
                  />
                  <Info icon={MapPin} label="Xona" value={g.room || '—'} />
                </div>

                {/* A'zolar ro'yxati — TO'LIQ tarix (chiqqan/muzlatilgan/sinov/aktiv) */}
                <div className="border-t border-slate-100 pt-4">
                  <div className="mb-3 flex items-center gap-2">
                    <Users className="h-5 w-5 text-brand-600" />
                    <h2 className="font-semibold text-slate-800">A'zolar</h2>
                    <span className="text-sm text-slate-400">{members.filter((m) => m.isActive).length}</span>
                    <div className="ml-auto flex shrink-0 items-center gap-1">
                      {members.length > 1 && (
                        <button
                          type="button"
                          onClick={() => setMembersSortAsc((v) => !v)}
                          title="Ism bo'yicha alfavit tartibida saralash"
                          className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-slate-200 px-2 py-1 text-xs font-medium text-slate-500 transition-colors hover:border-brand-300 hover:text-brand-700"
                        >
                          <ArrowUpDown className="h-3.5 w-3.5" /> {membersSortAsc ? 'A-Z' : 'Z-A'}
                        </button>
                      )}
                      {/* A'zolar modali — qo'shish/qidirish bilan bir qatorda OMMAVIY muzlatish va
                          aktivlashtirish shu yerda. Ilgari modalga faqat "⋮ → Yangi talaba qo'shish"
                          orqali kirilardi, ya'ni ommaviy amallar nomi aldaydigan menyu ostida
                          ko'rinmay qolardi. */}
                      {can('classes.list', 'create') && (
                        <button
                          type="button"
                          onClick={() => setMembersOpen(true)}
                          title="A'zolarni boshqarish: qo'shish, ommaviy muzlatish/aktivlashtirish"
                          className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-slate-200 px-2 py-1 text-xs font-medium text-slate-500 transition-colors hover:border-brand-300 hover:text-brand-700"
                        >
                          <Settings2 className="h-3.5 w-3.5" /> Boshqarish
                        </button>
                      )}
                    </div>
                  </div>
                  {members.length === 0 ? (
                    <p className="py-6 text-center text-sm text-slate-400">Bu guruhda a'zo yo'q.</p>
                  ) : (
                    <>
                      {/* Rang izohi */}
                      <div className="mb-3 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-slate-400">
                        <span className="inline-flex items-center gap-1">
                          <span className="h-2 w-2 rounded-full bg-emerald-500" /> Faol
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <span className="h-2 w-2 rounded-full bg-red-500" /> Qarzdor
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <span className="h-2 w-2 rounded-full bg-sky-500" /> Muzlatilgan
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <span className="h-2 w-2 rounded-full bg-amber-500" /> Sinovda
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <X className="h-3 w-3 text-slate-300" /> Chiqarilgan
                        </span>
                      </div>

                      <div className="max-h-[55vh] overflow-y-auto">
                        <ul className="-mx-2 divide-y divide-slate-100">
                          {sortedMembers.filter((m) => m.isActive).map((m) => (
                            <MemberRow
                              key={m.studentId}
                              m={m}
                              back={memberBack}
                              canManage={can('classes.list', 'create')}
                              onActivate={() => { openRoster(m); setRosterReason('activate') }}
                              onFreeze={() => { openRoster(m); setRosterReason('freeze') }}
                              onReturn={() => { openRoster(m); setRosterReason('return') }}
                              onTransfer={() => { openRoster(m); setRosterTransferOpen(true) }}
                              onRemove={() => { openRoster(m); setRosterReason('remove') }}
                            />
                          ))}
                        </ul>
                        {members.some((m) => !m.isActive) && (
                          <>
                            <p className="mt-2 border-t border-slate-200 pt-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
                              Guruhdan chiqarilganlar
                            </p>
                            <ul className="-mx-2 divide-y divide-slate-100">
                              {sortedMembers.filter((m) => !m.isActive).map((m) => (
                                <MemberRow
                                  key={m.studentId}
                                  m={m}
                                  back={memberBack}
                                  canManage={can('classes.list', 'create')}
                                  onActivate={() => { openRoster(m); setRosterReason('activate') }}
                                  onFreeze={() => { openRoster(m); setRosterReason('freeze') }}
                                  onReturn={() => { openRoster(m); setRosterReason('return') }}
                                  onTransfer={() => { openRoster(m); setRosterTransferOpen(true) }}
                                  onRemove={() => { openRoster(m); setRosterReason('remove') }}
                                />
                              ))}
                            </ul>
                          </>
                        )}
                      </div>
                    </>
                  )}
                </div>
              </Card>
            </div>

            {/* O'NG USTUN — bo'limlar (tab) */}
            <div className="min-w-0 space-y-6">
              <div className="tabs flex-wrap" role="tablist">
                <button type="button" className={cn('tab', tab === 'jurnal' && 'active')} onClick={() => { setTab('jurnal'); load(journal?.month) }}>
                  <BookOpen className="mr-1 inline h-3.5 w-3.5" /> Jurnal
                </button>
                <button type="button" className={cn('tab', tab === 'davomat' && 'active')} onClick={() => setTab('davomat')}>
                  <CalendarCheck className="mr-1 inline h-3.5 w-3.5" /> Davomat
                </button>
                <button type="button" className={cn('tab', tab === 'baholash' && 'active')} onClick={() => setTab('baholash')}>
                  <CheckCircle2 className="mr-1 inline h-3.5 w-3.5" /> Baholash
                </button>
                <button type="button" className={cn('tab', tab === 'reyting' && 'active')} onClick={() => setTab('reyting')}>
                  <TrendingUp className="mr-1 inline h-3.5 w-3.5" /> Reyting
                </button>
                <button type="button" className={cn('tab', tab === 'imtihonlar' && 'active')} onClick={() => setTab('imtihonlar')}>
                  <ClipboardList className="mr-1 inline h-3.5 w-3.5" /> Imtihonlar
                </button>
                <button type="button" className={cn('tab', tab === 'dastur' && 'active')} onClick={() => setTab('dastur')}>
                  <ListChecks className="mr-1 inline h-3.5 w-3.5" /> O'quv dasturi
                </button>
                {canContact && (
                  <button type="button" className={cn('tab', tab === 'aloqa' && 'active')} onClick={() => setTab('aloqa')}>
                    <PhoneCall className="mr-1 inline h-3.5 w-3.5" /> Aloqa
                  </button>
                )}
                {canSeeAudit && (
                  <button type="button" className={cn('tab', tab === 'tarix' && 'active')} onClick={() => setTab('tarix')}>
                    <History className="mr-1 inline h-3.5 w-3.5" /> Tarix
                  </button>
                )}
                {/* Oylik to'lov yig'ilishi — FAQAT moliya ruxsati bor xodimga (o'qituvchiga ko'rinmaydi) */}
                {canSeePayments && (
                  <button type="button" className={cn('tab', tab === 'tolovlar' && 'active')} onClick={() => setTab('tolovlar')}>
                    <Wallet className="mr-1 inline h-3.5 w-3.5" /> Oylik to'lov yig'ilishi
                  </button>
                )}
                <button type="button" className={cn('tab', tab === 'ai' && 'active')} onClick={() => setTab('ai')}>
                  <Sparkles className="mr-1 inline h-3.5 w-3.5" /> AI tahlil
                </button>
              </div>

          {/* Oylik jurnal */}
          {tab === 'jurnal' && (
          <Card className="p-0">
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
              <div className="flex items-center gap-2">
                <BookOpen className="h-5 w-5 text-brand-600" />
                <h2 className="font-semibold text-slate-800">Jurnal (oylik)</h2>
                <span className="inline-flex items-center gap-1 text-sm text-slate-400">
                  <Users className="h-4 w-4" /> {journalStudents.length} o'quvchi
                </span>
                <span className="inline-flex items-center gap-1 rounded-md bg-emerald-50 px-2 py-0.5 text-sm font-semibold text-emerald-600">
                  {activeCount} faol
                </span>
                <button
                  type="button"
                  onClick={() =>
                    setJournalSort((v) => (v === 'nameAsc' ? 'nameDesc' : v === 'nameDesc' ? 'score' : 'nameAsc'))
                  }
                  title="Tartib: ball / A-Z / Z-A"
                  className="inline-flex shrink-0 items-center gap-1 rounded-lg border border-slate-200 px-2 py-1 text-xs font-medium text-slate-500 transition-colors hover:border-brand-300 hover:text-brand-700"
                >
                  <ArrowUpDown className="h-3.5 w-3.5" />
                  {journalSort === 'score' ? 'Ball' : journalSort === 'nameAsc' ? 'A-Z' : 'Z-A'}
                </button>
              </div>
              <div className="flex flex-wrap items-center gap-1.5">
                {journal?.months.map((m) => (
                  <button
                    key={m}
                    type="button"
                    onClick={() => load(m)}
                    title={monthLabel(m)}
                    className={cn(
                      'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                      journal.month === m
                        ? 'bg-brand-600 text-white'
                        : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                    )}
                  >
                    {monthLabel(m)}
                  </button>
                ))}
              </div>
            </div>

            {!g.courseId ? (
              <p className="px-4 py-12 text-center text-sm text-slate-400">
                Guruhga kurs biriktirilmagan — jurnal yuritib bo'lmaydi.
              </p>
            ) : journalStudents.length === 0 && frozenStudents.length === 0 ? (
              <p className="px-4 py-12 text-center text-sm text-slate-400">
                Bu guruhda o'quvchi yo'q.
              </p>
            ) : (journal?.columns.length ?? 0) === 0 ? (
              <p className="px-4 py-12 text-center text-sm text-slate-400">
                {monthLabel(journal?.month ?? '')} oyida bu guruh kunlariga dars to'g'ri kelmadi.
              </p>
            ) : (
              <>
              {/* Rang izohi — o'quvchi ismi rangi SHU GURUH balansiga qarab (o'qituvchi jurnali bilan bir xil). */}
              <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-slate-100 px-4 py-2 text-[11px] text-slate-400">
                <span className="inline-flex items-center gap-1">
                  <span className={cn('h-2 w-2 rounded-full', balanceDotCls(0))} /> To'lagan
                </span>
                <span className="inline-flex items-center gap-1">
                  <span className={cn('h-2 w-2 rounded-full', balanceDotCls(-1))} /> Qarzdor
                </span>
                <span className="inline-flex items-center gap-1">
                  <span className={cn('h-2 w-2 rounded-full', balanceDotCls(-1, HEAVY_DEBT_MONTHS))} /> 2+ oylik qarz
                </span>
                {/* To'lov nazorati belgisi — balans ranglaridan MUSTAQIL, alohida chip. */}
                <span
                  className="inline-flex items-center gap-1"
                  title="Jurnal boshqaruvi → To'lov nazorati sozlamasi bo'yicha. Muzlatish emas: hisob davom etadi, to'lovdan keyin qator o'z-o'zidan qaytadi. Admin jurnalida hamma ko'rinaveradi."
                >
                  <EyeOff className="h-3 w-3 text-amber-500" /> O'qituvchida ko'rinmaydi
                </span>
              </div>
              <div className="overflow-x-auto">
                <table className="min-w-full border-collapse text-sm">
                  <thead>
                    <tr className="bg-slate-100 text-xs text-slate-500">
                      {/* № ustuni ham STICKY: aks holda o'ngga siljitilganda F.I.SH ustuni
                          uning ustidan surilib, tartib raqami ko'rinmay qolardi. Kengligi QAT'IY
                          (w-10 = 40px) — F.I.SH ustuni aynan shu masofada (`left-10`) turadi. */}
                      <th className="sticky left-0 z-20 w-10 border-b-2 border-r border-slate-200 bg-slate-100 px-1 py-2.5 text-center font-semibold">
                        №
                      </th>
                      <th className="sticky left-10 z-20 border-b-2 border-r-2 border-slate-200 bg-slate-100 px-4 py-2.5 text-left font-semibold">
                        O'quvchi
                      </th>
                      {journal!.columns.map((c) => {
                        const dt = new Date(c.date)
                        const wd = (dt.getDay() + 6) % 7
                        const isToday = c.date === today
                        const isBeforeStart = !!g.startDate && c.date < g.startDate
                        const moved = rescheduledByDate.get(c.date)
                        return (
                          <th
                            key={c.date}
                            className={cn(
                              'border-b-2 border-r border-slate-200 p-0 text-center font-semibold',
                              isBeforeStart ? 'bg-slate-50 text-slate-300' : moved ? 'bg-sky-50 text-sky-700' : isToday ? 'bg-brand-100 text-brand-700' : 'text-slate-500',
                            )}
                          >
                            <button
                              type="button"
                              disabled={isBeforeStart}
                              onClick={() => setBulkDate(c.date)}
                              title={isBeforeStart ? 'Sana guruh yaratilishidan oldin' : moved ? `Ko'chirilgan dars (${formatDate(moved.fromDate)} dan) — davomat / boshqarish` : 'Shu kun uchun hammaga davomat (keldi / kelmadi)'}
                              className={cn(
                                'relative w-full px-2 py-1.5 transition-colors',
                                isBeforeStart ? 'cursor-not-allowed opacity-50' : 'hover:bg-brand-200/40',
                              )}
                            >
                              {moved && (
                                <CalendarClock className="absolute right-0.5 top-0.5 h-3 w-3 text-sky-500" />
                              )}
                              <div className="text-sm">{c.date.slice(8, 10)}</div>
                              <div
                                className={cn(
                                  'text-[10px] font-medium',
                                  isBeforeStart ? 'text-slate-300' : moved ? 'text-sky-500' : isToday ? 'text-brand-500' : 'text-slate-400',
                                )}
                              >
                                {weekdayShort[wd]}
                              </div>
                            </button>
                          </th>
                        )
                      })}
                      <th className="sticky right-0 z-20 border-b-2 border-l-2 border-slate-200 bg-slate-100 px-4 py-2.5 text-center font-semibold text-slate-600">
                        Jami
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {journalStudents.map((st, idx) => {
                      const sb = statusBadge(st.status)
                      return (
                        <tr key={st.studentId} className="bg-white even:bg-slate-50 hover:bg-brand-50">
                          <td className="sticky left-0 z-10 w-10 border-b border-r border-slate-200 bg-inherit px-1 py-1 text-center">
                            <span className="text-xs font-medium text-slate-500">{idx + 1}</span>
                          </td>
                          <td className="sticky left-10 z-10 border-b border-r-2 border-slate-200 bg-inherit px-2 py-1">
                            {/* Rang: 2+ oylik qarz binafsha-pushti (eng og'ir, qizildan USTUN),
                                qarzdor qizil, to'lagan yashil — o'qituvchi jurnali bilan bir xil. */}
                            <div className="flex w-full items-center gap-2 rounded-md px-2 py-1 text-left">
                              <span
                                className={cn('h-2 w-2 shrink-0 rounded-full', balanceDotCls(st.balance, st.debtMonths))}
                                title={balanceTitle(st.balance, st.debtMonths)}
                              />
                              <span className={cn('font-medium', balanceTextCls(st.balance, st.debtMonths))}>
                                {st.fullName}
                              </span>
                              <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-medium', sb.cls)}>
                                {sb.label}
                              </span>
                              {/* To'lov nazorati: o'quvchi O'QITUVCHI jurnalidan olib turilgan.
                                  Admin jurnalida qator O'CHIRILMAYDI — faqat shu belgi qo'shiladi
                                  (balans ranglariga tegmaydi, alohida element). */}
                              {st.paymentHidden && (
                                <span
                                  className="inline-flex shrink-0 items-center gap-1 rounded bg-amber-50 px-1.5 py-0.5 text-[10px] font-medium text-amber-700"
                                  title={paymentHiddenTitle(st.paymentHiddenReason)}
                                >
                                  <EyeOff className="h-3 w-3" />
                                  O'qituvchida ko'rinmaydi
                                </span>
                              )}
                            </div>
                          </td>
                          {journal!.columns.map((c) => {
                            const e = entryMap.get(`${st.studentId}|${c.date}`)
                            const reason = e?.reasonId ? reasonById.get(e.reasonId) : undefined
                            const isToday = c.date === today
                            // O'quvchi guruhda boshlagan sanadan (memberStart) yoki guruh yaratilishidan
                            // OLDINGI darslarga davomat/baho qo'yib bo'lmaydi — katak bloklanadi.
                            const beforeMember = !!st.memberStart && c.date < st.memberStart
                            const isBeforeStart = (!!g.startDate && c.date < g.startDate) || beforeMember
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
                                  'border-b border-r border-slate-100 p-1 text-center',
                                  isBeforeStart ? 'bg-slate-50' : isToday && 'bg-brand-50/30',
                                )}
                              >
                                <button
                                  type="button"
                                  disabled={isBeforeStart}
                                  onClick={() => {
                                    setCellError(null)
                                    setCell({ studentId: st.studentId, studentName: st.fullName, date: c.date })
                                  }}
                                  className={cn(
                                    'flex h-9 w-full min-w-9 items-center justify-center rounded-md text-sm font-semibold transition-colors',
                                    isBeforeStart
                                      ? 'cursor-not-allowed text-slate-200'
                                      : e?.grade != null
                                        ? gradeBadgeCls(e.grade)
                                        : e?.mastery != null
                                          ? masteryInfo.cls
                                          : reason
                                            ? reason.isLate
                                              ? 'bg-amber-50 text-amber-700 hover:bg-amber-100'
                                              : 'bg-red-50 text-red-600 hover:bg-red-100'
                                            : present
                                              ? 'bg-emerald-50 text-emerald-600 hover:bg-emerald-100'
                                              : 'text-slate-300 hover:bg-brand-50',
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
                                          ? '✓'
                                          : '·'}
                                </button>
                              </td>
                            )
                          })}
                          <td className="sticky right-0 z-10 border-b border-l-2 border-slate-200 bg-inherit px-4 py-1 text-center">
                            <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold text-slate-600', st.combinedTotal > 0 ? 'bg-violet-100 text-violet-700' : 'text-slate-400')}>
                              {st.combinedTotal || '—'}
                            </span>
                          </td>
                        </tr>
                      )
                    })}

                    {/* Muzlatilganlar — jurnalga qo'shilmaydi, lekin baho/davomati SAQLANADI; tugma bilan ochiladi/yopiladi */}
                    {frozenStudents.length > 0 && (
                      <>
                        <tr>
                          <td colSpan={3 + journal!.columns.length} className="border-b border-t-2 border-slate-200 bg-slate-50 p-0">
                            <button
                              type="button"
                              onClick={() => setFrozenOpen(!frozenVisible)}
                              className="flex w-full items-center gap-1.5 px-4 py-1.5 text-left text-[11px] font-semibold uppercase tracking-wide text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                            >
                              {frozenVisible ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
                              Muzlatilgan ({frozenStudents.length}) — faqat ko'rish, baho/davomat saqlanadi
                            </button>
                          </td>
                        </tr>
                        {frozenVisible && frozenStudents.map((st) => {
                          const totalGrade = journal!.columns.reduce((sum, c) => {
                            const e = entryMap.get(`${st.studentId}|${c.date}`)
                            return sum + (e?.grade ?? 0)
                          }, 0)
                          return (
                            <tr key={st.studentId} className="bg-slate-50 text-slate-400">
                              <td className="sticky left-0 z-10 w-10 border-b border-r border-slate-200 bg-inherit px-1 py-1 text-center" />
                              <td className="sticky left-10 z-10 border-b border-r-2 border-slate-200 bg-inherit px-2 py-1">
                                {/* Muzlatilgan bo'lsa ham per-guruh BALANS ko'rsatiladi: to'lagan bo'lsa
                                    yashil, qarzi bo'lsa qizil, 2+ oylik qarzda binafsha-pushti (faol
                                    qatorlar bilan bir xil) + "Muzlatilgan" yorlig'i. */}
                                <div className="flex w-full items-center gap-2 rounded-md px-2 py-1 text-left">
                                  <span
                                    className={cn('h-2 w-2 shrink-0 rounded-full', balanceDotCls(st.balance, st.debtMonths))}
                                    title={balanceTitle(st.balance, st.debtMonths)}
                                  />
                                  <span className={cn('font-medium', balanceTextCls(st.balance, st.debtMonths))}>
                                    {st.fullName}
                                  </span>
                                  <span className="inline-flex shrink-0 items-center gap-1 rounded bg-sky-50 px-1.5 py-0.5 text-[10px] font-medium text-sky-700">
                                    <Snowflake className="h-3 w-3" />
                                    Muzlatilgan
                                  </span>
                                </div>
                              </td>
                              {journal!.columns.map((c) => {
                                const e = entryMap.get(`${st.studentId}|${c.date}`)
                                const reason = e?.reasonId ? reasonById.get(e.reasonId) : undefined
                                // Muzlatilgandan KEYINGI o'tilgan darslarda o'quvchi qatnashmagan — avto-"keldi"
                                // qo'yilmaydi (faqat aniq belgilangan e.present yashil bo'ladi). Undan oldingi
                                // real davomat/baho esa faol o'quvchi kabi ko'rinadi.
                                const afterFrozen = !!st.frozenAt && c.date > st.frozenAt
                                // Faol qatorlardagi kabi: guruhga qo'shilishidan (memberStart) yoki guruh
                                // yaratilishidan oldingi darslar hech qachon avto-"keldi" bo'lmaydi.
                                const isBeforeStart =
                                  (!!g.startDate && c.date < g.startDate) || (!!st.memberStart && c.date < st.memberStart)
                                const present =
                                  e?.grade == null && !reason && conductedSet.has(c.date) &&
                                  (e?.present ||
                                    ((!st.presentDefaultFrom || c.date >= st.presentDefaultFrom) && !afterFrozen && !isBeforeStart))
                                const masteryInfo = e?.mastery != null ? masteryDisplay(e.mastery) : { label: '', cls: '' }
                                return (
                                  <td key={c.date} className="border-b border-r border-slate-100 p-1 text-center">
                                    <span
                                      className={cn(
                                        'inline-flex h-7 min-w-7 items-center justify-center rounded px-1 text-sm font-semibold',
                                        e?.grade != null
                                          ? gradeBadgeCls(e.grade)
                                          : e?.mastery != null
                                            ? masteryInfo.cls
                                            : reason
                                              ? reason.isLate
                                                ? 'bg-amber-50 text-amber-700'
                                                : 'bg-red-50 text-red-600'
                                              : present
                                                ? 'bg-emerald-50 text-emerald-600'
                                                : 'text-slate-300',
                                      )}
                                    >
                                      {e?.grade != null
                                        ? e.grade
                                        : e?.mastery != null
                                          ? masteryInfo.label
                                          : reason
                                            ? reason.short || reason.name.slice(0, 2)
                                            : present
                                              ? '✓'
                                              : '·'}
                                    </span>
                                  </td>
                                )
                              })}
                              <td className="sticky right-0 z-10 border-b border-l-2 border-slate-200 bg-inherit px-4 py-1 text-center">
                                <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold text-slate-400', totalGrade > 0 ? 'bg-slate-200 text-slate-600' : '')}>
                                  {totalGrade || '—'}
                                </span>
                              </td>
                            </tr>
                          )
                        })}
                      </>
                    )}
                  </tbody>
                </table>
              </div>
              </>
            )}
          </Card>
          )}

          {/* Davomat — tanlangan oy uchun har o'quvchi davomat foizi (jurnal ma'lumotidan) */}
          {tab === 'davomat' && (
            <Card className="p-0">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
                <div className="flex items-center gap-2">
                  <CalendarCheck className="h-5 w-5 text-brand-600" />
                  <h2 className="font-semibold text-slate-800">Davomat</h2>
                  <span className="text-sm text-slate-400">{monthLabel(journal?.month ?? '')}</span>
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  {journal?.months.map((m) => (
                    <button
                      key={m}
                      type="button"
                      onClick={() => load(m)}
                      title={monthLabel(m)}
                      className={cn(
                        'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                        journal.month === m ? 'bg-brand-600 text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                      )}
                    >
                      {monthLabel(m)}
                    </button>
                  ))}
                </div>
              </div>
              {attendanceRows.length === 0 ? (
                <p className="px-4 py-12 text-center text-sm text-slate-400">O'quvchi yo'q.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="min-w-full border-collapse text-sm">
                    <thead>
                      <tr className="bg-slate-100 text-xs uppercase tracking-wide text-slate-500">
                        <th className="border-b border-slate-200 px-4 py-2.5 text-left font-semibold">O'quvchi</th>
                        <th className="border-b border-slate-200 px-3 py-2.5 text-center font-semibold">O'tilgan dars</th>
                        <th className="border-b border-slate-200 px-3 py-2.5 text-center font-semibold">Qoldirgan</th>
                        <th className="border-b border-slate-200 px-3 py-2.5 text-center font-semibold">Davomat %</th>
                      </tr>
                    </thead>
                    <tbody>
                      {attendanceRows.map((r) => {
                        const sb = statusBadge(r.status)
                        return (
                          <tr key={r.studentId} className="border-b border-slate-100 hover:bg-slate-50">
                            <td className="px-4 py-2.5">
                              <span className="inline-flex items-center gap-2">
                                <span className="font-medium text-slate-800">{r.fullName}</span>
                                <span className={cn('rounded px-1.5 py-0.5 text-[10px] font-medium', sb.cls)}>{sb.label}</span>
                              </span>
                            </td>
                            <td className="px-3 py-2.5 text-center font-mono text-slate-600">{r.held}</td>
                            <td className="px-3 py-2.5 text-center font-mono text-slate-600">{r.absent || '—'}</td>
                            <td className="px-3 py-2.5 text-center">
                              {r.pct == null ? (
                                <span className="text-slate-400">—</span>
                              ) : (
                                <span
                                  className={cn(
                                    'inline-flex min-w-11 items-center justify-center rounded-md px-2 py-0.5 text-sm font-semibold',
                                    r.pct >= 90 ? 'bg-emerald-50 text-emerald-700'
                                      : r.pct >= 75 ? 'bg-amber-50 text-amber-700'
                                        : 'bg-red-50 text-red-600',
                                  )}
                                >
                                  {r.pct}%
                                </span>
                              )}
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}

          {/* Baholash — har darsga mezonlar bo'yicha bajardi/bajarmadi */}
          {tab === 'baholash' && (
            <Card title="Baholash" sub="Har darsga mezonlar bo'yicha o'quvchini belgilang (bajardi / bajarmadi)">
              <GradingSection groupId={id} fetchBoard={getGradingBoard} saveGrade={setGrade} bulkGrade={bulkGrade} />
            </Card>
          )}

          {/* Reyting — o'quvchilarning o'rtacha bahosi va baholash statistikasi (bir yoki bir nechta oy yig'indisi) */}
          {tab === 'reyting' && (
            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-slate-200 bg-white px-4 py-3 shadow-[var(--shadow-1)]">
                <div className="flex items-center gap-2">
                  <TrendingUp className="h-5 w-5 text-brand-600" />
                  <h2 className="font-semibold text-slate-800">Reyting oylari</h2>
                  {ratingMonths.length > 1 && (
                    <span className="rounded-full bg-violet-100 px-2.5 py-0.5 text-xs font-semibold text-violet-700">
                      {ratingMonths.length} oy yig'indisi
                    </span>
                  )}
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  {(journal?.months ?? []).map((m) => (
                    <button
                      key={m}
                      type="button"
                      onClick={() => toggleRatingMonth(m)}
                      title={monthLabel(m)}
                      className={cn(
                        'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                        ratingMonths.includes(m)
                          ? 'bg-brand-600 text-white'
                          : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                      )}
                    >
                      {monthLabel(m)}
                    </button>
                  ))}
                </div>
              </div>
              <RatingsTab data={ratingData} loading={ratingLoading} />
            </div>
          )}

          {/* Imtihonlar — guruh testlari (yaratish/tahrirlash/o'chirish + ball kiritishga o'tish) */}
          {/* Imtihonlar — "Testlar natijalari" bo'limi bilan AYNAN bir xil panel:
              onlayn (bot) va oflayn test shu yerning o'zida yaratiladi. */}
          {tab === 'imtihonlar' && <GroupTestsPanel groupId={id} onOpenTest={(testId) => navigate(`/admin/test-results/${id}/tests/${testId}`)} />}

          {/* ALOQA — guruh o'quvchilarini "Bog'lanish kerak" navbatiga yuborish */}
          {tab === 'aloqa' && canContact && (
            <Card
              title="Bog'lanish kerak"
              sub="O'quvchini tanlab navbatga yuboring — sabab va izoh bilan, bugungi sana bilan."
            >
              <GroupContactTab
                students={(journal?.students ?? []).map((s) => ({
                  id: s.studentId,
                  fullName: s.fullName,
                  status: s.status,
                }))}
                loadReasons={() =>
                  getActionReasons().then((all) =>
                    all.filter((r) => r.category === 'contact').map((r) => ({ id: r.id, label: r.label })),
                  )
                }
                onSend={(ids, reasonId, note) =>
                  createContactRequestsBulk({
                    studentIds: ids,
                    reasonId: reasonId || undefined,
                    note: note || undefined,
                    // Sana YO'Q — talab darhol navbatga tushadi (bugungi ish).
                  })
                }
              />
            </Card>
          )}

          {/* Tarix — guruhga oid barcha o'zgarishlar (to'g'ridan-to'g'ri tahrir + a'zolik amallari) */}
          {tab === 'tarix' && canSeeAudit && (
            <Card title="O'zgarishlar tarixi" sub="Guruh va a'zolik amallari (muzlatish/aktivlashtirish/o'tkazish/chiqarish)">
              <AuditHistoryList filters={{ groupId: id }} emptyLabel="O'zgarishlar tarixi yo'q" />
            </Card>
          )}

          {/* Oylik to'lov yig'ilishi — kim to'ladi/to'lamadi (faqat moliya ruxsati bor xodimga) */}
          {tab === 'tolovlar' && canSeePayments && (
            <GroupPaymentsTab
              groupId={id}
              back={memberBack}
              months={journal?.months ?? []}
              defaultMonth={journal?.month ?? ''}
            />
          )}

          {/* AI tahlil — davomat, muzlatish/ketish, imtihon, to'lov, jurnal: tanqidiy tahlil */}
          {tab === 'ai' && <GroupAiPanel groupId={id} />}

          {/* O'QUV DASTURI — endi ALOHIDA tab (ilgari barcha tablar ostida, jurnal pastida
              turardi va sahifani cho'zib yuborardi). Darsda o'tilgan bandlar + tugatish prognozi. */}
          {tab === 'dastur' && (
            <CurriculumSection
              curr={curr}
              loading={currLoading}
              expanded={currExpanded}
              onToggleTopic={toggleTopic}
              onToggleCover={toggleCover}
              onChangeRevision={changeRevision}
              revSaving={revSaving}
              nextItemId={nextItemId}
            />
          )}
            </div>
          </div>
        </>
      )}

      <JournalCellModal
        open={!!cell}
        studentName={cell?.studentName ?? ''}
        dateLabel={cell ? formatDate(cell.date) : ''}
        date={cell?.date}
        startDate={g?.startDate}
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
        title={bulkDate ? `${formatDate(bulkDate)} — davomat` : 'Davomat'}
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
          <p className="text-sm text-slate-500">
            Shu darsdagi <span className="font-semibold text-slate-700">{journalStudents.length}</span> o'quvchiga
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
              <p className="mb-2 text-sm font-medium text-slate-600">Yoki sabab bilan kelmadi:</p>
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
                <p className="text-sm font-medium text-slate-600">
                  Darsni boshqa kunga ko'chirish (bir martalik)
                </p>
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <label className="mb-1 block text-xs text-slate-500">Yangi sana</label>
                    <input
                      type="date"
                      value={rToDate}
                      onChange={(e) => setRToDate(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs text-slate-500">Vaqt (ixtiyoriy)</label>
                    <input
                      type="time"
                      value={rTime}
                      onChange={(e) => setRTime(e.target.value)}
                      className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
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
                    <CalendarClock className="h-4 w-4" /> {rSaving ? 'Ko\'chirilmoqda...' : 'Ko\'chirish'}
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

      {/* A'zolar ro'yxati "⋮" — muzlatish/aktivlashtirish/sinovga qaytarish/chiqarish (sabab + sana modali) */}
      <ReasonPromptModal
        open={!!rosterReason}
        category={
          rosterReason === 'freeze' ? 'freeze'
          : rosterReason === 'activate' ? 'activate'
          : rosterReason === 'remove' ? rosterRemoveCategory(rosterTarget?.status ?? 'trial')
          : 'return_trial'
        }
        title={
          rosterReason === 'freeze' ? 'Muzlatish'
          : rosterReason === 'activate' ? 'Aktivlashtirish'
          : rosterReason === 'remove' ? "Guruhdan chiqarish"
          : 'Sinov darsiga qaytarish'
        }
        message={
          rosterTarget
            ? rosterReason === 'freeze'
              ? `${rosterTarget.fullName} — shu sanadan boshlab oylik to'lov hisoblanmaydi.`
              : rosterReason === 'activate'
                ? `${rosterTarget.fullName} — shu sanadan boshlab qisman oylik hisoblanadi.`
                : rosterReason === 'remove'
                  ? `${rosterTarget.fullName} ni guruhdan chiqarasizmi?`
                  : `${rosterTarget.fullName} — sinov holatiga qaytariladi (oylik to'lov hisoblanmaydi).`
            : undefined
        }
        confirmLabel={
          rosterReason === 'freeze' ? 'Muzlatish'
          : rosterReason === 'activate' ? 'Aktivlashtirish'
          : rosterReason === 'remove' ? 'Chiqarish'
          : 'Sinovga qaytarish'
        }
        tone={rosterReason === 'freeze' ? 'sky' : rosterReason === 'remove' ? 'red' : 'brand'}
        showDate={rosterReason === 'freeze' || rosterReason === 'activate'}
        showRetentionBonus={rosterReason === 'activate' && canSetBonus}
        defaultDate={rosterDate}
        onConfirm={confirmRosterReason}
        onClose={() => setRosterReason(null)}
      />

      {/* A'zolar ro'yxati "⋮" — boshqa guruhga o'tkazish */}
      {rosterTarget && (
        <TransferGroupModal
          open={rosterTransferOpen}
          onClose={() => setRosterTransferOpen(false)}
          studentId={rosterTarget.studentId}
          studentName={rosterTarget.fullName}
          fromGroupId={id}
          fromGroupName={g?.name ?? ''}
          onDone={() => {
            setRosterTransferOpen(false)
            setRosterTarget(null)
            reloadMembers()
            load(journal?.month)
          }}
        />
      )}

      {/* "⋮" menyudan OMMAVIY muzlatish/aktivlashtirish — sana (va sabab) BIR marta tanlanadi.
          Yakka a'zo modali (`rosterReason`) bilan aralashmasin deb ALOHIDA element. */}
      <ReasonPromptModal
        open={!!groupBulk}
        category={groupBulk === 'freeze' ? 'freeze' : 'activate'}
        title={groupBulk === 'freeze' ? 'Hammasini muzlatish' : 'Hammasini aktivlashtirish'}
        message={
          groupBulk === 'freeze'
            ? `Guruhning ${bulkFreezeTargets.length} ta a'zosi shu sanadan muzlatiladi — oylik to'lov hisoblanmaydi.`
            : `Guruhning ${bulkActivateTargets.length} ta a'zosi shu sanadan aktivlashtiriladi — qisman oylik hisoblanadi.`
        }
        confirmLabel={
          groupBulk === 'freeze'
            ? (groupBulkBusy ? 'Muzlatilyapti…' : 'Muzlatish')
            : (groupBulkBusy ? 'Aktivlashtirilyapti…' : 'Aktivlashtirish')
        }
        tone={groupBulk === 'freeze' ? 'sky' : 'brand'}
        showDate
        defaultDate={today}
        showRetentionBonus={groupBulk === 'activate' && canSetBonus}
        onConfirm={confirmGroupBulk}
        onClose={() => {
          if (!groupBulkBusy) setGroupBulk(null)
        }}
      />

      {/* A'zo qo'shish: avval mavjud o'quvchilardan qidiriladi, topilmasa "Yangi o'quvchi" yaratiladi
          (ClassMembersModal — a'zolarni to'liq boshqarish: qo'shish/qidirish/yaratish/holat). */}
      <ClassMembersModal
        group={membersOpen ? group : null}
        onClose={() => {
          setMembersOpen(false)
          reloadMembers()
          load(journal?.month)
        }}
      />

      {/* Guruhni tahrirlash — sarlavha "⋮" menyusidan. */}
      <ClassFormModal
        open={editOpen}
        onClose={() => setEditOpen(false)}
        onSubmit={handleEditSubmit}
        initial={group}
      />

      {/* Guruhga SMS jo'natish — sarlavha "⋮" menyusidan, faol a'zolarning barchasiga. */}
      <SmsModal open={smsOpen} onClose={() => setSmsOpen(false)} recipients={smsRecipients} />

      {/* Guruhni yakunlash modali (Hybrid: arxivlash + maqsad kursga yangi guruh) */}
      <CompleteAndTransferModal
        open={showCompleteModal}
        onClose={() => setShowCompleteModal(false)}
        groupId={id}
        currentGroupName={journal?.group?.name ?? ''}
        currentCourseId={journal?.group?.courseId ?? ''}
        // Yangi guruhga FAQAT aktivlar ko'chadi — modal ikkala sonni ham ko'rsatadi.
        activeCount={members.filter((m) => m.isActive && m.status === 'active').length}
        inactiveCount={members.filter((m) => m.isActive && m.status !== 'active').length}
        onSuccess={(result) => {
          const coursePart = result.targetCourseName ? ` (${result.targetCourseName} kursi)` : ''
          alert(
            `Tugatildi!\n` +
              `• ${result.certificatesGenerated} ta sertifikat yaratildi\n` +
              `• Eski guruh ${formatDate(result.closeDate)} sanasida yopildi` +
              (result.chargedOldGroup > 0
                ? ` — ${result.chargedOldGroup} o'quvchiga shu sanagacha oylik yozildi\n`
                : '\n') +
              (result.restoredCharges > 0
                ? `• Yopishdan keyingi ${formatMoney(result.restoredCharges)} hisob bekor qilindi\n`
                : '') +
              `• Yangi guruh ochildi${coursePart}\n` +
              `• ${result.enrolledInNew} ta AKTIV o'quvchi yangi guruhga ko'chirildi` +
              (result.activatedInNew > 0
                ? ` (${result.activatedInNew} tasi ${formatDate(result.activateDate)} sanasidan aktiv)\n`
                : '\n') +
              (result.skippedNotActive > 0
                ? `• ${result.skippedNotActive} ta sinovdagi/muzlatilgan a'zo ko'chirilmadi (eski guruhda yakunlandi)\n`
                : '') +
              (result.movedAdvance > 0
                ? `• ${formatMoney(result.movedAdvance)} avans yangi guruhga ko'chirildi`
                : ''),
          )
          window.location.href = `/admin/classes/${result.newGroupId}`
        }}
      />

      {/* Guruhni yopish modali (barcha a'zolarni muzlatish + guruhni arxivga olish) */}
      <CloseGroupModal
        open={showCloseModal}
        onClose={() => setShowCloseModal(false)}
        groupId={id}
        groupName={journal?.group?.name ?? group?.name ?? ''}
        activeMembers={members.filter((m) => m.isActive).length}
        onSuccess={(result) => {
          alert(
            `"${result.groupName}" yopildi.\n` +
              `• ${result.frozenCount} ta o'quvchi ${formatDate(result.freezeDate)} sanasidan muzlatildi\n` +
              (result.trialClosed > 0 ? `• ${result.trialClosed} ta sinovdagi a'zolik yakunlandi\n` : '') +
              (result.restoredCharges > 0
                ? `• Muzlatishdan keyingi ${formatMoney(result.restoredCharges)} hisob bekor qilindi\n`
                : '') +
              '• Guruh arxivga olindi (to\'lov qabul qilinaveradi)',
          )
          navigate('/admin/classes')
        }}
      />
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
  value: ReactNode
  /** Raqam/pul/vaqt qiymatlari uchun mono shrift */
  mono?: boolean
}) {
  return (
    <div className="flex items-start gap-2.5">
      <span className="mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-slate-50 text-slate-400">
        <Icon className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <p className="text-[11px] font-semibold uppercase tracking-wide text-slate-400">{label}</p>
        <p className={cn('break-words text-sm font-semibold text-slate-700', mono && 'font-mono')}>
          {value}
        </p>
      </div>
    </div>
  )
}

// ============================ A'zolar ro'yxati qatori ============================

/** Chap ustundagi bitta a'zolik qatori — bosilsa profilga o'tadi, "⋮" menyu faqat FAOL a'zolarda. */
function MemberRow({
  m, back, canManage, onActivate, onFreeze, onReturn, onTransfer, onRemove,
}: {
  m: GroupMember
  /** Profilga o'tilganda "Orqaga" shu guruhga qaytarsin. */
  back: BackState
  canManage: boolean
  onActivate: () => void
  onFreeze: () => void
  onReturn: () => void
  onTransfer: () => void
  onRemove: () => void
}) {
  // Rang ustuvorligi: chiqqan (line-through) → qarzdor (qizil) → sinov (sariq) → to'lagan (yashil).
  // MUZLATILGAN a'zo ham TO'LOV bo'yicha bo'yaladi (jurnaldagi kabi): to'lagan bo'lsa YASHIL,
  // qarzi bo'lsa qizil — holati esa yonidagi "Muzlatilgan" yorlig'ida ko'rinadi.
  // MUHIM: "aktiv" holat xira kulrang (slate) EMAS, yashil (emerald) — aks holda chiqarilgan (kulrang+
  // chizilgan) bilan vizual chalkashib, aktiv/to'lov qilgan a'zo ham "qora"day ko'rinardi.
  const removed = !m.isActive
  const nameCls = removed
    ? 'text-slate-400 line-through'
    : m.balance < 0
      ? 'text-red-600'
      : m.status === 'trial'
        ? 'text-amber-600'
        : 'text-emerald-700'
  const sb = statusBadge(m.status)

  return (
    <li className="relative">
      <Link
        to={`/admin/students/${m.studentId}`}
        state={back}
        className="flex items-center gap-2 rounded-lg px-2 py-2 pr-9 transition-colors hover:bg-slate-50"
      >
        {removed ? (
          <X className="h-3.5 w-3.5 shrink-0 text-slate-300" />
        ) : (
          <span
            className={cn('h-2 w-2 shrink-0 rounded-full', m.balance < 0 ? 'bg-red-500' : m.status === 'trial' ? 'bg-amber-500' : 'bg-emerald-500')}
            title={m.balance < 0 ? `Qarz (shu guruh): ${formatMoney(m.balance)}` : 'Shu guruh uchun to\'langan'}
          />
        )}
        <span className={cn('min-w-0 flex-1 truncate text-sm font-medium', nameCls)}>{m.fullName}</span>
        {removed ? (
          <span className="shrink-0 text-[11px] text-slate-400">
            {m.leftAt ? formatDate(m.leftAt) : 'Chiqgan'}
          </span>
        ) : (
          <span className={cn('shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium', sb.cls)}>{sb.label}</span>
        )}
      </Link>
      {canManage && !removed && (
        <div className="absolute right-1 top-1/2 -translate-y-1/2" onClick={(e) => e.preventDefault()}>
          <DropdownMenu
            triggerClassName="h-7 w-7 rounded-lg border-transparent"
            items={[
              ...(m.status !== 'active'
                ? [{ label: 'Faollashtirish', icon: CheckCircle2, onClick: onActivate }]
                : [{ label: 'Muzlatish', icon: Snowflake, onClick: onFreeze }]),
              ...(m.status !== 'trial'
                ? [{ label: 'Sinov darsiga qaytarish', icon: RotateCcw, onClick: onReturn }]
                : []),
              { label: "Boshqa guruhga o'tkazish", icon: ArrowLeftRight, onClick: onTransfer },
              { label: 'Guruhdan chiqarish', icon: X, danger: true, onClick: onRemove },
            ]}
          />
        </div>
      )}
    </li>
  )
}

// ============================ Oylik to'lov yig'ilishi (guruh ichida) ============================

/** Oyning oxirgi kuni ("YYYY-MM" → "YYYY-MM-DD"). */
function monthEnd(month: string): string {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  if (!y || !m) return month
  return `${month}-${String(new Date(y, m, 0).getDate()).padStart(2, '0')}`
}

/**
 * Guruh ichidagi "Oylik to'lov yig'ilishi" — tanlangan oy uchun kim to'ladi, kim to'lamadi:
 * hisoblangan/yig'ilgan summa, har o'quvchi bo'yicha to'langan va qarz. Ma'lumot moliya
 * hisobotidan (`/admin/finance/group-payments/{id}`) keladi — shu sabab FAQAT moliya ruxsati
 * bor xodimga ko'rsatiladi (o'qituvchi bu tabni umuman ko'rmaydi).
 */
function GroupPaymentsTab({
  groupId,
  back,
  months,
  defaultMonth,
}: {
  groupId: string
  /** Profilga o'tilganda "Orqaga" shu guruhga qaytarsin. */
  back: BackState
  months: string[]
  defaultMonth: string
}) {
  const fallbackMonth = new Date().toISOString().slice(0, 7)
  const [month, setMonth] = useState(defaultMonth || months[months.length - 1] || fallbackMonth)
  const [report, setReport] = useState<GroupPaymentsReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oy o'zgarganda hisobotni qayta yuklash (maqsadli)
    setLoading(true)
    setError(null)
    getGroupPayments(groupId, `${month}-01`, monthEnd(month))
      .then((r) => alive && setReport(r))
      .catch((e) => alive && setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [groupId, month])

  const monthOptions = months.length > 0 ? months : [month]
  const collectionPct = report && report.billed > 0
    ? Math.round((report.collected / report.billed) * 100)
    : 0

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div className="flex items-center gap-2">
          <Wallet className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Oylik to'lov yig'ilishi</h2>
          <span className="text-sm text-slate-400">kim to'ladi / kim to'lamadi</span>
        </div>
        <select
          value={month}
          onChange={(e) => setMonth(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
        >
          {monthOptions.map((m) => (
            <option key={m} value={m}>
              {monthLabel(m)}
            </option>
          ))}
        </select>
      </div>

      {loading ? (
        <div className="p-6">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : error ? (
        <p className="p-6 text-center text-sm text-red-600">{error}</p>
      ) : !report ? null : (
        <div className="space-y-4 p-4">
          {/* Umumiy ko'rsatkichlar */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <PayKpi label="Hisoblangan" value={formatMoney(report.billed)} />
            <PayKpi label="Yig'ilgan" value={formatMoney(report.collected)} tone="green" sub={`${collectionPct}%`} />
            <PayKpi label="To'ladi" value={`${report.paidCount} ta`} tone="green" />
            <PayKpi label="To'lamadi" value={`${report.unpaidCount} ta`} tone="red" />
          </div>

          {report.rows.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">
              Bu oyda guruhda hisob-kitob yoki o'quvchi yo'q.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="py-2 pr-3 font-medium">O'quvchi</th>
                    <th className="py-2 pr-3 font-medium">Holat</th>
                    <th className="py-2 pr-3 text-right font-medium">Hisoblangan</th>
                    <th className="py-2 pr-3 text-right font-medium">To'langan</th>
                    <th className="py-2 text-right font-medium">Qarz</th>
                  </tr>
                </thead>
                <tbody>
                  {report.rows.map((r) => (
                    <tr key={r.studentId} className="border-b border-slate-50 last:border-0">
                      <td className="py-2 pr-3">
                        <Link
                          to={`/admin/students/${r.studentId}`}
                          state={back}
                          className="font-medium text-slate-700 hover:text-brand-600 hover:underline"
                        >
                          {r.fullName}
                        </Link>
                      </td>
                      <td className="py-2 pr-3">
                        {r.fullyPaid ? (
                          <span className="rounded bg-emerald-50 px-1.5 py-0.5 text-[11px] font-medium text-emerald-700">
                            To'liq to'lagan
                          </span>
                        ) : r.hasPaid ? (
                          <span className="rounded bg-amber-50 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                            Qisman
                          </span>
                        ) : (
                          <span className="rounded bg-red-50 px-1.5 py-0.5 text-[11px] font-medium text-red-600">
                            To'lamagan
                          </span>
                        )}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono text-slate-600">{formatMoney(r.billed)}</td>
                      <td className="py-2 pr-3 text-right font-mono text-emerald-600">{formatMoney(r.collected)}</td>
                      <td className="py-2 text-right font-mono font-semibold text-red-600">
                        {r.debt > 0 ? formatMoney(r.debt) : <span className="text-slate-300">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </Card>
  )
}

/** "Oylik to'lov yig'ilishi" bo'limidagi kichik ko'rsatkich kartasi. */
function PayKpi({
  label,
  value,
  sub,
  tone,
}: {
  label: string
  value: string
  sub?: string
  tone?: 'green' | 'red'
}) {
  return (
    <div className="rounded-xl border border-slate-200 px-3 py-2.5">
      <p className="text-xs text-slate-400">{label}</p>
      <p
        className={cn(
          'mt-0.5 font-mono text-sm font-semibold',
          tone === 'green' ? 'text-emerald-600' : tone === 'red' ? 'text-red-600' : 'text-slate-700',
        )}
      >
        {value}
        {sub && <span className="ml-1 text-xs font-normal text-slate-400">{sub}</span>}
      </p>
    </div>
  )
}

// ============================ O'quv dasturi bo'limi ============================

function CurriculumSection({
  curr, loading, expanded, onToggleTopic, onToggleCover, onChangeRevision, revSaving, nextItemId,
}: {
  curr: GroupCurriculum | null
  loading: boolean
  expanded: Set<string>
  onToggleTopic: (topicId: string) => void
  onToggleCover: (itemId: string, covered: boolean) => void
  onChangeRevision: (delta: number) => void
  revSaving: boolean
  nextItemId: string | null
}) {
  if (loading && !curr) {
    return (
      <Card>
        <Loader label="O'quv dasturi yuklanmoqda..." />
      </Card>
    )
  }
  if (!curr || curr.totalItems === 0 || curr.modules.length === 0) {
    return (
      <Card className="py-10 text-center text-sm text-slate-400">
        Bu guruh kursida o'quv dasturi yo'q.
      </Card>
    )
  }

  const pct = curr.totalItems > 0 ? Math.round((curr.coveredCount / curr.totalItems) * 100) : 0

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div className="flex items-center gap-2">
          <ListChecks className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">O'quv dasturi (darsda o'tilgan)</h2>
        </div>
      </div>

      {/* PROGNOZ KARTASI */}
      <div className="border-b border-slate-100 px-4 py-4">
        {/* Progress bar */}
        <div className="mb-4">
          <div className="mb-1.5 flex items-center justify-between text-sm">
            <span className="font-medium text-slate-600">Bajarildi</span>
            <span className="font-mono font-semibold text-brand-700">
              {curr.coveredCount}/{curr.totalItems} · {pct}%
            </span>
          </div>
          <div className="h-2.5 w-full overflow-hidden rounded-full bg-slate-100">
            <div
              className="h-full rounded-full bg-brand-500 transition-all"
              style={{ width: `${pct}%` }}
            />
          </div>
        </div>

        {/* Stat plitalar */}
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <ForecastTile icon={CheckCircle2} label="O'tilgan">
            <span className="font-mono">{curr.coveredCount}</span>
            <span className="text-slate-400">/{curr.totalItems}</span>
          </ForecastTile>
          <ForecastTile icon={Repeat} label="Takrorlash darslari">
            <span className="font-mono">{curr.revisionLessons}</span>
          </ForecastTile>
          <ForecastTile icon={Flag} label="Qolgan">
            <span className="font-mono">{curr.remainingItems}</span> band
          </ForecastTile>
          <ForecastTile icon={CalendarClock} label="Tugatishga">
            <span className="text-slate-500">~</span>
            <span className="font-mono">{curr.estLessonsLeft}</span> dars
            {curr.estFinishDate && (
              <span className="mt-0.5 block text-xs font-normal text-slate-400">
                ≈ {formatDate(curr.estFinishDate)} da tugaydi
              </span>
            )}
          </ForecastTile>
        </div>

        {/* Takrorlash darsi tugmalari */}
        <div className="mt-4 flex items-center gap-2">
          <button
            type="button"
            disabled={revSaving}
            onClick={() => onChangeRevision(1)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-brand-50 px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:bg-brand-100 disabled:opacity-50"
          >
            <Plus className="h-4 w-4" /> Takrorlash darsi
          </button>
          <button
            type="button"
            disabled={revSaving || curr.revisionLessons <= 0}
            onClick={() => onChangeRevision(-1)}
            title="Oxirgi takrorlash darsini olib tashlash"
            className="inline-flex items-center justify-center rounded-lg border border-slate-200 bg-white p-2 text-slate-500 transition-colors hover:bg-slate-50 disabled:opacity-40"
          >
            <Minus className="h-4 w-4" />
          </button>
          <span className="text-xs text-slate-400">
            Jami darslar: <span className="font-mono">{curr.totalLessons}</span>
            {curr.lessonsPerWeek > 0 && (
              <> · haftasiga <span className="font-mono">{curr.lessonsPerWeek}</span> dars</>
            )}
          </span>
        </div>
      </div>

      {/* DASTUR DARAXTI — modullar → mavzular (default yopiq) */}
      <div className="space-y-4 p-4">
        {curr.modules.map((module) => {
          const mItems = module.topics.flatMap((tp) => tp.items)
          const mCovered = mItems.filter((it) => it.covered).length
          const moduleOpen = expanded.has(module.id)
          return (
            <div key={module.id} className="overflow-hidden rounded-xl border border-slate-300 bg-slate-50/40">
              <button
                type="button"
                onClick={() => onToggleTopic(module.id)}
                className="flex w-full items-center gap-2 bg-slate-100/70 px-3 py-2.5 text-left transition-colors hover:bg-slate-200/60"
              >
                <span className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg text-slate-500">
                  {moduleOpen ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                </span>
                <span className="min-w-0 flex-1 truncate text-sm font-bold text-slate-800">{module.name}</span>
                <span
                  className={cn(
                    'flex-shrink-0 rounded-full px-2.5 py-1 text-xs font-medium',
                    mItems.length > 0 && mCovered === mItems.length
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-slate-200 text-slate-600',
                  )}
                >
                  <span className="font-mono">{mCovered}/{mItems.length}</span>
                </span>
              </button>
              {moduleOpen && (
                <div className="space-y-3 p-3">
                  {module.topics.map((topic) => {
                    const tCovered = topic.items.filter((it) => it.covered).length
                    const open = expanded.has(topic.id)
                    return (
                      <div
                        key={topic.id}
                        className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-[var(--shadow-1)]"
                      >
                        {/* Mavzu sarlavhasi */}
                        <button
                          type="button"
                          onClick={() => onToggleTopic(topic.id)}
                          className="flex w-full items-center gap-2 border-b border-slate-100 bg-slate-50/60 px-3 py-2.5 text-left transition-colors hover:bg-slate-100/60"
                        >
                <span className="flex h-7 w-7 flex-shrink-0 items-center justify-center rounded-lg text-slate-400">
                  {open ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                </span>
                <span className="flex h-8 w-8 flex-shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-600">
                  <BookOpen className="h-4 w-4" />
                </span>
                <span className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-800">
                  {topic.title}
                </span>
                <span
                  className={cn(
                    'flex-shrink-0 rounded-full px-2.5 py-1 text-xs font-medium',
                    topic.items.length > 0 && tCovered === topic.items.length
                      ? 'bg-emerald-50 text-emerald-700'
                      : 'bg-slate-100 text-slate-500',
                  )}
                >
                  <span className="font-mono">{tCovered}/{topic.items.length}</span>
                </span>
              </button>

              {open && (
                <div className="p-3">
                  {topic.note && <p className="mb-2 px-1 text-xs text-slate-400">{topic.note}</p>}
                  {topic.items.length === 0 ? (
                    <p className="px-1 text-xs text-slate-400">Topshiriq yo'q.</p>
                  ) : (
                    <div className="grid grid-cols-2 gap-x-3 gap-y-1">
                      {topic.items.map((item) => {
                        const isNext = item.id === nextItemId
                        return (
                          <label
                            key={item.id}
                            className={cn(
                              'flex cursor-pointer items-start gap-2 rounded-md px-2 py-1.5 transition-colors',
                              item.covered
                                ? 'bg-emerald-50/50'
                                : isNext
                                  ? 'bg-brand-50/60 ring-1 ring-brand-200'
                                  : 'hover:bg-slate-50',
                            )}
                          >
                            <input
                              type="checkbox"
                              checked={item.covered}
                              onChange={() => onToggleCover(item.id, !item.covered)}
                              className="mt-0.5 h-4 w-4 flex-shrink-0 cursor-pointer rounded border-slate-300 text-brand-600 focus:ring-brand-400"
                            />
                            <span
                              className={cn(
                                'min-w-0 flex-1 text-sm',
                                item.covered
                                  ? 'text-slate-400 line-through'
                                  : 'text-slate-700',
                              )}
                            >
                              {item.text}
                              {isNext && (
                                <span className="ml-2 rounded bg-brand-100 px-1.5 py-0.5 text-[10px] font-medium text-brand-700 no-underline">
                                  keyingi
                                </span>
                              )}
                            </span>
                            {item.covered && item.coveredDate && (
                              <span className="ml-auto flex-shrink-0 self-center whitespace-nowrap rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-mono text-slate-400 no-underline">
                                {formatDate(item.coveredDate)}
                              </span>
                            )}
                          </label>
                        )
                      })}
                    </div>
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
    <div className="rounded-xl border border-slate-200 bg-white p-3">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-sm font-semibold text-slate-700">{children}</div>
    </div>
  )
}

// ============================ Reyting bo'limi ============================

function RatingsTab({ data, loading }: { data: RatingMonthData[]; loading: boolean }) {
  if (loading) {
    return <Loader label="Reyting yuklanmoqda..." />
  }

  // Faqat baholash ma'lumoti bor va o'quvchisi bor oylar hisobga olinadi.
  const validEntries = data.filter((d) => d.grading && d.grading.students.length > 0)

  if (validEntries.length === 0) {
    return (
      <Card className="rounded-lg border border-slate-200 bg-white py-12 px-4 text-center text-slate-400">
        <TrendingUp className="mx-auto mb-3 h-8 w-8 opacity-40" />
        <p>Baholash mezonlari topilmadi yoki o'quvchi yo'q.</p>
      </Card>
    )
  }

  // O'quvchilar — tanlangan oylardagi grading.students UNIONi (fullName oxirgi uchragan qiymat).
  const studentNames = new Map<string, string>()
  for (const { grading } of validEntries) {
    for (const s of grading!.students) studentNames.set(s.studentId, s.fullName)
  }

  // Mezonlar — tanlangan oylar criteria UNIONi (order bo'yicha saralab).
  const criteriaById = new Map<string, GradingBoardCriterion>()
  for (const { grading } of validEntries) {
    for (const c of grading!.criteria) if (!criteriaById.has(c.id)) criteriaById.set(c.id, c)
  }
  const criteria = Array.from(criteriaById.values()).sort((a, b) => a.order - b.order)

  // Umumiy imkoniyat — barcha o'quvchiga bir xil, tanlangan oylar (dates × criteria) yig'indisi.
  let totalPossible = 0
  for (const { grading } of validEntries) {
    if (grading) totalPossible += grading.dates.length * grading.criteria.length
  }

  // Har o'quvchi uchun statistikani hisoblash — tanlangan OYLAR bo'yicha yig'indi (agregat).
  const studentStats = Array.from(studentNames.entries())
    .map(([studentId, fullName]) => {
      let journalTotal = 0
      let done = 0
      const criteriaDoneMap = new Map<string, number>()

      for (const { journal, grading } of validEntries) {
        if (journal) {
          const entryMap = new Map((journal.entries ?? []).map((e) => [`${e.studentId}|${e.date}`, e]))
          for (const col of journal.columns) {
            const e = entryMap.get(`${studentId}|${col.date}`)
            if (e?.grade) journalTotal += e.grade
          }
        }
        if (grading) {
          const gs = grading.students.find((s) => s.studentId === studentId)
          const doneKeys = gs?.doneKeys ?? []
          done += doneKeys.length
          for (const key of doneKeys) {
            const [critId] = key.split('|')
            criteriaDoneMap.set(critId, (criteriaDoneMap.get(critId) ?? 0) + 1)
          }
        }
      }

      const percentage = totalPossible > 0 ? Math.round((done / totalPossible) * 100) : 0
      const criteriaStats = criteria.map((crit) => ({
        criterion: crit,
        done: criteriaDoneMap.get(crit.id) ?? 0,
        total: totalCriterionDates(validEntries, crit.id),
      }))

      // Kombindan reyting = jurnal baho + baholash mezonlari yig'indisi
      const combinedRating = journalTotal + done

      return { studentId, fullName, done, totalPossible, percentage, criteriaStats, journalTotal, combinedRating }
    })
    // Kombindan reyting bo'yicha saralash (katta → kichik)
    .sort((a, b) => b.combinedRating - a.combinedRating)

  // O'rtacha bahoni hisoblash
  const avgPercentage =
    studentStats.length > 0
      ? Math.round(studentStats.reduce((s, st) => s + st.percentage, 0) / studentStats.length)
      : 0

  return (
    <div className="space-y-4">
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
      <Card className="rounded-lg border border-slate-200 bg-white p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full border-collapse">
            <thead>
              <tr className="bg-slate-100">
                <th className="border-b border-slate-200 px-4 py-3 text-left text-xs font-semibold uppercase text-slate-500">
                  O'quvchi
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Jurnal
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Bajarildi
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Jami
                </th>
                {criteria.slice(0, 3).map((crit) => (
                  <th
                    key={crit.id}
                    className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500"
                    title={crit.name}
                  >
                    <span className="block max-w-[60px] truncate">{crit.name}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {studentStats.map((stat) => (
                <tr key={stat.studentId} className="border-b border-slate-200 hover:bg-slate-50">
                  <td className="px-4 py-3 text-sm font-medium text-slate-800">
                    {stat.fullName}
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-slate-800">
                    <span className="font-mono">{stat.journalTotal}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-slate-800">
                    <span className="font-mono">{stat.done}</span>
                    <span className="text-slate-400">/{stat.totalPossible}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-bold text-slate-800">
                    <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold', stat.combinedRating > 0 ? 'bg-violet-100 text-violet-700' : 'text-slate-400')}>
                      {stat.combinedRating || '—'}
                    </span>
                  </td>
                  {stat.criteriaStats.slice(0, 3).map((cs) => (
                    <td
                      key={cs.criterion.id}
                      className="px-3 py-3 text-center text-sm font-semibold text-slate-800"
                    >
                      <span className="font-mono text-brand-600">{cs.done}</span>
                      <span className="text-slate-400">/{cs.total}</span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Mezonlar bo'yicha detallar (agar ko'p bo'lsa) — tanlangan oylar bo'yicha agregat */}
      {criteria.length > 3 && (
        <Card className="rounded-lg border border-slate-200 bg-white p-4">
          <h3 className="mb-4 font-semibold text-slate-800">Mezonlar bo'yicha tahlil</h3>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {criteria.map((crit) => {
              let totalDone = 0
              let denom = 0
              for (const { grading } of validEntries) {
                if (!grading || !grading.criteria.some((c) => c.id === crit.id)) continue
                for (const student of grading.students) {
                  for (const key of student.doneKeys) {
                    const [critId] = key.split('|')
                    if (critId === crit.id) totalDone++
                  }
                }
                denom += grading.students.length * grading.dates.length
              }
              const pct = denom > 0 ? Math.round((totalDone / denom) * 100) : 0
              return (
                <div key={crit.id} className="rounded-lg bg-slate-50 p-3">
                  <p className="mb-2 text-sm font-semibold text-slate-800">{crit.name}</p>
                  <div className="flex items-center gap-2">
                    <div className="flex-1">
                      <div className="h-2 overflow-hidden rounded-full bg-slate-200">
                        <div
                          className="h-full rounded-full bg-brand-500 transition-all"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                    <span className="font-mono text-xs font-semibold text-slate-800 w-10 text-right">{pct}%</span>
                  </div>
                  <p className="mt-1 text-xs text-slate-400">
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

/** Bitta mezon bo'yicha — tanlangan oylardagi jami dars sanalari (mezon mavjud bo'lgan oylar bo'yicha). */
function totalCriterionDates(entries: RatingMonthData[], criterionId: string): number {
  let total = 0
  for (const { grading } of entries) {
    if (grading && grading.criteria.some((c) => c.id === criterionId)) total += grading.dates.length
  }
  return total
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
    <div className="rounded-xl border border-slate-200 bg-white p-3">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-lg font-semibold text-slate-800">{value}</div>
    </div>
  )
}
