import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, GraduationCap, CalendarCheck, ClipboardCheck,
  User, Phone, Wallet, BookOpen, MapPin, Cake, CalendarPlus, Percent, IdCard,
  School, Clock, CalendarDays, ChevronRight, History, ListChecks, ChevronDown, Check,
  CalendarClock, Award, Download, LifeBuoy, Sparkles, Pencil, MessageSquare,
  PhoneIncoming, PhoneOutgoing, PhoneMissed, PhoneCall, MessageSquareText,
  Snowflake, CheckCircle2, RotateCcw, ArrowLeftRight, Plus, NotebookText, X,
  StickyNote, Gift, BadgePercent,
  DoorOpen, LogIn, LogOut, RefreshCw, Wifi, WifiOff,
} from 'lucide-react'
import { genderLabels } from '@/config/constants'
import {
  Bar, BarChart, Cell, CartesianGrid, Legend,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { getStudentNotebook, type StudentNotebook } from '@/api/services/studentNotebook'
import { getStudentDiscounts, type StudentDiscountsResponse } from '@/api/services/discounts'
import {
  getStudentCertificates,
  downloadStudentCertificate,
  generateStudentCertificate,
  getStudentSupportFeedback,
  getStudentAiAnalyses,
  getStudentCalls,
  getStudentSms,
  getStudent,
  updateStudent,
  addPayment,
  updateStudentPhoto,
  type StudentCompletedCourse,
  type StudentSupportFeedback,
  type StudentAiAnalysisRecord,
  type StudentCall,
  type StudentSms,
} from '@/api/services/students'
import type { StudentPayload } from '@/api/services/students'
import {
  getStudentGroups, getClasses, freezeMember, activateMember, returnMemberToTrial, addGroupMember,
  removeGroupMember,
} from '@/api/services/classes'
import {
  getSubjectCurriculumTree, getProgress, setProgress, getStudentCoverageLog,
  getStudentAttempts, getAttemptDetail,
  type CoverageLogEntry, type StudentAttempt, type AttemptAnswer,
} from '@/api/services/curriculum'
import { getStudentGradingSummary, type MonthGradingSummary } from '@/api/services/grading'
import {
  getStudentRetention,
  type RetentionReport,
  type RetentionRow,
  type RetentionAward,
  type RetentionState,
  type RetentionStatus,
} from '@/api/services/retentionBonus'
import { kindTitle } from '@/components/exercise/catalog'
import type { ExerciseKind } from '@/components/exercise/model'
import { getTeachers } from '@/api/services/teachers'
import { getStudentTestResults } from '@/api/services/testResults'
import type { Student, StudentGroupMembership, Curriculum, Group, Teacher, StudentTestResult } from '@/types'
import { cn, formatDate, formatDateTime, formatMoney, apiErrorMessage, gradeBadgeCls } from '@/lib/utils'
import { usePerm, useSuperOrGranted } from '@/lib/permissions'
import { readBackState } from '@/lib/nav'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { DropdownMenu } from '@/components/ui/DropdownMenu'
import { Badge, type BadgeTone } from '@/components/ui/Badge'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { PaymentHistoryPanel } from './PaymentHistoryPanel'
import { TeacherReviewsSection } from './TeacherReviewsSection'
import { StudentPhotoDialog } from './StudentPhotoDialog'
import { NeedContactModal } from './contacts/NeedContactModal'
import { StudentNotesThread } from '@/components/students/StudentNotesThread'
import { MonthDayStrip } from '@/components/ui/MonthDayStrip'
import { currentMonth, todayIso } from '@/lib/month'
import {
  getStudentTurnstileHistory,
  syncStudentTurnstile,
  setStudentDevice,
  type StudentTurnstileHistory,
} from '@/api/services/studentTurnstile'
import { getStudentContactRequests, type ContactRequestItem } from '@/api/services/contacts'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { PaymentModal } from './PaymentModal'
import { AiAnalysisModal } from './AiAnalysisModal'
import { AiAnalysisView } from './AiAnalysisView'
import { StudentFormModal } from './StudentFormModal'
import { DiscountSection } from './DiscountSection'
import { studentDiscountLabel } from './discountLabel'
import { ProfileCard } from './profile/ProfileCard'
import { SmsModal } from './SmsModal'
import { CallPickerModal, type CallOption } from '@/components/CallPickerModal'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { TransferGroupModal } from '../classes/TransferGroupModal'
import { StudentJournalModal } from './StudentJournalModal'

const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m
const weekdayShort = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']

/**
 * A'zolik holati belgisi. `yearFreeze` — «Aktiv muzlatish» (yangi o'quv yili): holat baribir
 * "frozen", shuning uchun bu ALOHIDA status emas, o'sha belgining aniqroq yozuvi.
 */
function groupStatusBadge(status: string, yearFreeze?: boolean): { label: string; tone: BadgeTone } {
  switch (status) {
    case 'active':
      return { label: 'Aktiv', tone: 'green' }
    case 'frozen':
      return yearFreeze
        ? { label: 'Aktiv muzlatilgan', tone: 'indigo' }
        : { label: 'Muzlatilgan', tone: 'blue' }
    case 'completed':
      return { label: 'Tugatilgan', tone: 'teal' }
    case 'left':
      return { label: 'Chiqgan', tone: 'default' }
    default:
      return { label: 'Sinov', tone: 'amber' }
  }
}

// Har fan uchun alohida rang (statistika uslubidagi rangli nuqtalar/legend uchun)
const dynColors = [
  '#3b82f6', '#f59e0b', '#34d399', '#f472b6', '#a78bfa', '#22d3ee', '#fb7185', '#a3e635',
  '#ef4444', '#14b8a6', '#eab308', '#8b5cf6',
]
const gridStroke = '#eef0f4'
const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0' }

type Tab =
  | 'guruhlar'
  | 'tolov'
  | 'chegirma'
  | 'dastur'
  | 'baholar'
  | 'fikr'
  | 'testlar'
  | 'sertifikatlar'
  | 'aloqa'
  | 'izohlar'
  | 'turniket'
  | 'bonus'
  | 'ai'

export function StudentDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { can } = usePerm()
  const { user } = useAuth()
  // «Bonus» bo'limi FAQAT admin/superadmin uchun: bu o'qituvchi haqiga taalluqli ma'lumot va
  // endpoint boshqa rolga 403 qaytaradi. `usePerm().can()` bu yerda yaramaydi — u xodimga
  // ("staff") "students" ruxsati berilgan bo'lsa ham true qaytaradi, shuning uchun ROL tekshiriladi.
  const isBonusAllowed = user?.role === 'admin' || user?.role === 'superadmin'
  // «O'qituvchilar haqida fikr» — AYNAN shu darvoza: o'qituvchi haqidagi ichki baholash bo'lgani
  // uchun xodimga (staff) ochilmaydi, server ham faqat admin/superadmin'ga ruxsat beradi.
  const canWriteTeacherReviews = isBonusAllowed
  // RASM — o'quvchini tahrirlash huquqi bilan bir xil (dumaloq avatarni bosib almashtiriladi).
  const canEditPhoto = can('students.list', 'edit')
  // CHEGIRMA — berish/tahrirlash/bekor qilish o'quvchini tahrirlash huquqi bilan bir xil.
  // Ko'rish uchun alohida darvoza YO'Q: sahifaning O'ZI allaqachon `students.list` ostida.
  const canEditDiscount = can('students.list', 'edit')
  // "Bog'lanish kerak" — ALOHIDA ruxsat (`contacts`), o'quvchi tahririga bog'liq emas.
  const canContact = can('contacts', 'create')
  const [contactOpen, setContactOpen] = useState(false)
  /** Shu o'quvchi bo'yicha bog'lanish talablari — "Aloqa" tabida ko'rinadi. */
  const [contacts, setContacts] = useState<ContactRequestItem[]>([])
  /** Ro'yxatni KO'RISH ruxsati — talab ochish (`create`) dan alohida. */
  const canSeeContacts = can('contacts', 'view')
  // «Turniket» tabi — ALOHIDA sahifa ruxsati (`students.turnstile`). Ilgari bu alohida sahifa edi
  // (barcha o'quvchi, bitta kun); endi kesim o'quvchi bo'yicha va profil ichida, lekin darvoza
  // O'SHA kalit: turniketni ko'rmasligi kerak xodimga tab UMUMAN chizilmaydi.
  const canTurnstile = can('students.turnstile', 'view')
  /** Qurilma ID'ni biriktirish/olib tashlash — YOZISH amali, ko'rishdan alohida. */
  const canEditTurnstile = can('students.turnstile', 'edit')

  const [photoOpen, setPhotoOpen] = useState(false)
  /** To'lov cheki — to'lov kiritilgach shu tranzaksiya cheki ochiladi. */
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)
  // Aktivlashtirish oynasidagi «Bonus hisoblansin» ptichkasi — bundan ham QATTIQROQ darvoza:
  // faqat superadmin yoki «Xodimlar va rollar» dan shu ruxsat berilgan xodim (oddiy "admin" emas).
  const canSetBonus = useSuperOrGranted('retentionBonus')
  const [data, setData] = useState<StudentNotebook | null>(null)
  const [groups, setGroups] = useState<StudentGroupMembership[]>([])
  /** groupId → courseId (Group.courseId) — o'quv dasturini olish uchun kurs id'sini topish. */
  const [groupCourse, setGroupCourse] = useState<Record<string, string>>({})
  /** "Jurnalni ko'rish" tugmasi bosilganda — faqat o'qish uchun jurnal modali. */
  const [journalOpen, setJournalOpen] = useState(false)
  const [showAi, setShowAi] = useState(false)
  /** Saqlangan AI tahlillari (eng yangisi birinchi). */
  const [aiRecords, setAiRecords] = useState<StudentAiAnalysisRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [notFound, setNotFound] = useState(false)
  /** O'ng ustundagi faol bo'lim (tab). */
  const [tab, setTab] = useState<Tab>('guruhlar')
  /** Oylik baholash jadvalida tanlangan oy ("YYYY-MM"). */
  /** Fan baholari dinamikasida tanlangan oy ("YYYY-MM"). */
  const [gradeMonth, setGradeMonth] = useState('')
  /** Darslar tarixi — o'tilgan mavzular jadvali (eng yangisi birinchi). */
  const [coverageLog, setCoverageLog] = useState<CoverageLogEntry[]>([])
  /** Baholash xulosa (oylik o'rtacha + jami mezonlar). */
  const [gradingSummary, setGradingSummary] = useState<MonthGradingSummary[]>([])
  /** Test natijalari — barcha guruhlaridan (ball desc kelmaydi, testCol'lar alohida). */
  const [testResults, setTestResults] = useState<StudentTestResult[]>([])
  /** Tugatgan kurslar + sertifikatlar. */
  const [certificates, setCertificates] = useState<StudentCompletedCourse[]>([])
  const [certGenerating, setCertGenerating] = useState<string | null>(null)
  const [supportFeedback, setSupportFeedback] = useState<StudentSupportFeedback[]>([])
  /** Qo'ng'iroqlar tarixi (Local Call). */
  const [calls, setCalls] = useState<StudentCall[]>([])
  const [callsLoading, setCallsLoading] = useState(true)
  /** SMS tarixi (Eskiz + Local, ota-ona/o'quvchi raqamlari bo'yicha). */
  const [sms, setSms] = useState<StudentSms[]>([])
  const [smsLoading, setSmsLoading] = useState(true)

  /**
   * "Qo'ng'iroqlar tarixi" ARALASH lentasi: haqiqiy qo'ng'iroqlar (Local Call) va bog'lanish
   * urinishlarida yozilgan javoblar BITTA vaqt o'qida.
   *
   * <p>Sabab: operator qo'ng'iroq qiladi, keyin navbatda "javobi nima dedi" ni yozadi — bular
   * bitta hodisaning ikki tomoni. Ayri ro'yxatlarda ular bir-biridan uzoqda tushib qolar va
   * "bu qo'ng'iroqda nima gaplashildi?" degan savol javobsiz qolardi.</p>
   *
   * <p>Faqat MATN yozilgan urinishlar qo'shiladi — bo'sh javob lentani suyultirardi.</p>
   */
  const contactEvents = useMemo(
    () =>
      contacts.flatMap((c) =>
        (c.history ?? [])
          .filter((h) => (h.type === 'contact' || h.type === 'note') && h.response)
          .map((h) => ({
            id: h.id,
            at: h.createdAt,
            resultLabel: h.resultLabel,
            nextStatusLabel: h.nextStatusLabel,
            reasonLabel: c.reasonLabel,
            response: h.response,
            actorName: h.actorName,
            isNote: h.type === 'note',
          })),
      ),
    [contacts],
  )

  /** Qo'ng'iroqlar + bog'lanish javoblari, eng yangisi tepada. */
  const callFeed = useMemo(() => {
    const items: { key: string; at: string; call?: StudentCall; note?: (typeof contactEvents)[number] }[] = [
      ...calls.map((c) => ({ key: `call-${c.id}`, at: c.startedAt, call: c })),
      ...contactEvents.map((e) => ({ key: `contact-${e.id}`, at: e.at, note: e })),
    ]
    // ISO satrlar — leksikografik solishtirish vaqt tartibini beradi (formatlar bir xil).
    return items.sort((a, b) => (a.at < b.at ? 1 : a.at > b.at ? -1 : 0))
  }, [calls, contactEvents])
  /** "Guruhlar" kartasi bosilganda — a'zolik boshqaruvi modali (muzlatish/aktivlashtirish/sinovga/almashtirish). */
  const [groupModal, setGroupModal] = useState<StudentGroupMembership | null>(null)
  const [groupActionDate, setGroupActionDate] = useState('')
  /** `yearFreeze` — «Aktiv muzlatish»: o'sha muzlatishning O'ZI, faqat yangi o'quv yili belgisi bilan. */
  const [groupReasonAction, setGroupReasonAction] = useState<'freeze' | 'yearFreeze' | 'return' | 'remove' | 'activate' | null>(null)
  const [groupTransferOpen, setGroupTransferOpen] = useState(false)
  const [groupBusy, setGroupBusy] = useState(false)
  /** "Guruhga qo'shish" — barcha (arxivlanmagan) guruhlar ro'yxati + o'qituvchi→guruh tanlash modali. */
  const [allGroups, setAllGroups] = useState<Group[]>([])
  const [allTeachers, setAllTeachers] = useState<Teacher[]>([])
  const [addGroupOpen, setAddGroupOpen] = useState(false)
  const [addGroupTeacherId, setAddGroupTeacherId] = useState('')
  const [addGroupId, setAddGroupId] = useState('')
  const [addGroupDate, setAddGroupDate] = useState('')
  const [addGroupBusy, setAddGroupBusy] = useState(false)
  /** "Tahrirlash" tugmasi bosilganda — to'liq o'quvchi obyekti (StudentFormModal uchun). */
  const [editing, setEditing] = useState<Student | null>(null)
  /** "Qo'ng'iroq qilish" bosilganda — to'liq o'quvchi obyekti (barcha telefon raqamlari uchun). */
  const [callTarget, setCallTarget] = useState<Student | null>(null)
  const [callOpen, setCallOpen] = useState(false)
  /** "SMS yuborish" bosilganda — SmsModal bitta o'quvchi bilan ochiladi. */
  const [smsTarget, setSmsTarget] = useState<Student | null>(null)
  /** "To'lov qilish" bosilganda — PaymentModal uchun to'liq o'quvchi obyekti. */
  const [paymentTarget, setPaymentTarget] = useState<Student | null>(null)
  /** Saqlangandan keyin sahifa ma'lumotini qayta yuklash uchun (o'zgarsa — pastdagi useEffect qayta ishga tushadi). */
  const [reloadKey, setReloadKey] = useState(0)
  /** Ushlab turish bonusi holati — «Bonus» tabi BIRINCHI ochilganda bir marta yuklanadi. */
  const [bonus, setBonus] = useState<RetentionReport | null>(null)
  const [bonusLoading, setBonusLoading] = useState(false)
  const [bonusError, setBonusError] = useState('')
  /** Bonus ma'lumoti QAYSI o'quvchi uchun yuklangani — o'quvchi almashsa qayta yuklanadi. */
  const [bonusLoadedFor, setBonusLoadedFor] = useState('')
  /**
   * Tab ma'lumoti QAYSI o'quvchi uchun yuklangani (tab kaliti → studentId) — «Bonus» dagi
   * `bonusLoadedFor` bilan bir xil mantiq, faqat bir nechta tab uchun.
   *
   * <p>Sabab: server Fransiyada, foydalanuvchi O'zbekistonda — HAR so'rov ~350-400 ms. Sahifa
   * ochilishida 13 ta so'rov ketardi va ularning ko'pi foydalanuvchi UMUMAN ochmaydigan
   * tablarniki edi. Endi mount'da faqat standart «Guruhlar» tabiga keraklisi so'raladi,
   * qolgani esa o'z tabi BIRINCHI marta ochilganda.</p>
   *
   * <p>Bayroq tufayli tablar orasida u yoq-bu yoq o'tilganda qayta so'ralmaydi. Qiymat —
   * studentId (bayroqning O'ZI emas), shuning uchun o'quvchi almashsa taqqoslash mos kelmaydi
   * va tab ochilganda YANGI o'quvchiniki yuklanadi — `bonusLoadedFor` dagi bilan aynan bir xil.
   * Xaritani ALOHIDA tozalash SHART EMAS va zararli ham: tab effekti bilan bir commit'da
   * tozalansa, so'rov ikki marta ketardi.</p>
   */
  const [tabLoadedFor, setTabLoadedFor] = useState<Partial<Record<Tab, string>>>({})

  /** «Chegirma» tabi — o'quvchining chegirma registri (server TO'LIQ javob qaytaradi). */
  const [discounts, setDiscounts] = useState<StudentDiscountsResponse | null>(null)
  const [discountError, setDiscountError] = useState('')
  /**
   * «Yuklanmoqda» ALOHIDA holat emas, KELTIRIB CHIQARILADI: javob ham, xato ham yo'q ekan —
   * demak so'rov ketmoqda. Sabab: alohida `setLoading(true)` effekt TANASIDA chaqirilishi
   * kerak bo'lardi, bu esa `react-hooks/set-state-in-effect` ni buzadi (registr yuklashda
   * yagona sinxron o'zgarish shu edi).
   */
  const discountLoading = discounts === null && discountError === ''

  // Bonus — faqat tab ochilganda va faqat admin/superadmin uchun so'raladi (aks holda 403 keladi).
  useEffect(() => {
    if (tab !== 'bonus' || !isBonusAllowed || !id || bonusLoadedFor === id) return
    let alive = true
    setBonusLoading(true)
    setBonusError('')
    getStudentRetention(id)
      .then((r) => {
        if (!alive) return
        setBonus(r)
        setBonusLoadedFor(id)
      })
      .catch((err) => {
        if (!alive) return
        setBonusError(apiErrorMessage(err, "Bonus ma'lumotini yuklab bo'lmadi"))
      })
      .finally(() => {
        if (alive) setBonusLoading(false)
      })
    return () => {
      alive = false
    }
  }, [tab, id, isBonusAllowed, bonusLoadedFor])

  // Darslar tarixi (o'tilgan mavzular) — faqat «Dastur» tabi ochilganda.
  useEffect(() => {
    if (tab !== 'dastur' || !id || tabLoadedFor.dastur === id) return
    setTabLoadedFor((p) => ({ ...p, dastur: id }))
    getStudentCoverageLog(id)
      .then(setCoverageLog)
      .catch(() => {})
  }, [tab, id, tabLoadedFor])

  // Baholash xulosasi — faqat «Baholar» tabi ochilganda.
  useEffect(() => {
    if (tab !== 'baholar' || !id || tabLoadedFor.baholar === id) return
    setTabLoadedFor((p) => ({ ...p, baholar: id }))
    getStudentGradingSummary(id)
      .then(setGradingSummary)
      .catch(() => {})
  }, [tab, id, tabLoadedFor])

  /**
   * Chegirma registri — TAB emas, O'QUVCHI bo'yicha yuklanadi.
   *
   * ⚠️ Boshqa tablardan FARQI ATAYIN: chap ustundagi «Chegirma» qatori HAR DOIM ko'rinadi va
   * uning "nechta fanda chegirma bor" sanog'i AYNAN shu registrdan olinadi (`StudentNotebook`
   * da bunday maydon YO'Q). Lazy yuklansa, info qatori "1 ta chegirma" deb turar, o'sha
   * sahifaning «Chegirma» tabi esa "Amaldagi chegirmalar: 3 ta" deb ko'rsatardi.
   * (`tabLoadedFor` naqshi saqlanadi: effekt ichida setState QILINMAYDI.)
   */
  /**
   * Chegirma registrini QAYTA yuklashga majbur qilish: bayroq olib tashlanadi va yuqoridagi
   * effekt o'zi ishga tushadi (so'rov effekt ICHIDA qoladi — `react-hooks/set-state-in-effect`
   * naqshi buzilmasin). Xato bayrog'i ham tozalanadi, aks holda oldingi xato matni yangi
   * javob kelgunicha ekranda turib qolardi.
   */
  const reloadDiscounts = useCallback(() => {
    setDiscountError('')
    setTabLoadedFor((p) => {
      if (p.chegirma === undefined) return p
      const next = { ...p }
      delete next.chegirma
      return next
    })
  }, [])

  useEffect(() => {
    if (!id || tabLoadedFor.chegirma === id) return
    let alive = true
    getStudentDiscounts(id)
      .then((r) => {
        if (!alive) return
        setDiscounts(r)
        setTabLoadedFor((p) => ({ ...p, chegirma: id }))
      })
      .catch((e) => {
        if (!alive) return
        setDiscountError(apiErrorMessage(e, "Chegirma ma'lumotini yuklab bo'lmadi"))
        // Bayroq XATODA ham qo'yiladi — aks holda effekt cheksiz qayta urinardi.
        setTabLoadedFor((p) => ({ ...p, chegirma: id }))
      })
    return () => {
      alive = false
    }
  }, [id, tabLoadedFor])

  // Test natijalari — faqat «Testlar» tabi ochilganda.
  useEffect(() => {
    if (tab !== 'testlar' || !id || tabLoadedFor.testlar === id) return
    setTabLoadedFor((p) => ({ ...p, testlar: id }))
    getStudentTestResults(id)
      .then(setTestResults)
      .catch(() => {})
  }, [tab, id, tabLoadedFor])

  // Tugatgan kurslar va sertifikatlar — faqat «Sertifikatlar» tabi ochilganda.
  useEffect(() => {
    if (tab !== 'sertifikatlar' || !id || tabLoadedFor.sertifikatlar === id) return
    setTabLoadedFor((p) => ({ ...p, sertifikatlar: id }))
    getStudentCertificates(id)
      .then(setCertificates)
      .catch((e) => console.warn('Sertifikatlar yuklanmadi:', e))
  }, [tab, id, tabLoadedFor])

  // Qo'llab-quvvatlash fikrlari — faqat «Fikr» tabi ochilganda.
  useEffect(() => {
    if (tab !== 'fikr' || !id || tabLoadedFor.fikr === id) return
    setTabLoadedFor((p) => ({ ...p, fikr: id }))
    getStudentSupportFeedback(id)
      .then(setSupportFeedback)
      .catch(() => {})
  }, [tab, id, tabLoadedFor])

  // Saqlangan AI tahlillari — faqat «AI» tabi ochilganda (modal ham shu tabdan ochiladi).
  useEffect(() => {
    if (tab !== 'ai' || !id || tabLoadedFor.ai === id) return
    setTabLoadedFor((p) => ({ ...p, ai: id }))
    getStudentAiAnalyses(id)
      .then(setAiRecords)
      .catch(() => {})
  }, [tab, id, tabLoadedFor])

  // «Aloqa» tabi — bog'lanish talablari, qo'ng'iroqlar va SMS lentasi BIR JOYDA ko'rinadi,
  // shuning uchun uchalasi ham shu tab ochilganda birga so'raladi.
  useEffect(() => {
    if (tab !== 'aloqa' || !id || tabLoadedFor.aloqa === id) return
    setTabLoadedFor((p) => ({ ...p, aloqa: id }))
    // Bog'lanish talablari — `contacts` ruxsati bo'lmasa server 403 qaytaradi, shuning uchun
    // umuman so'ramaymiz (konsolda keraksiz xato chiqmasin).
    if (canSeeContacts) getStudentContactRequests(id).then(setContacts).catch(() => setContacts([]))
    setCallsLoading(true)
    getStudentCalls(id)
      .then(setCalls)
      .catch(() => setCalls([]))
      .finally(() => setCallsLoading(false))
    setSmsLoading(true)
    getStudentSms(id)
      .then(setSms)
      .catch(() => setSms([]))
      .finally(() => setSmsLoading(false))
  }, [tab, id, tabLoadedFor, canSeeContacts])

  /* ── «Turniket» tabi — kirish/chiqish tarixi ────────────────────────────────────────── */

  /** Kalendarda tanlangan KUN ("yyyy-MM-dd") — ochilganda BUGUN. */
  const [tsDate, setTsDate] = useState(todayIso())
  /** Kalendar chizig'i ko'rsatayotgan OY ("yyyy-MM"). */
  const [tsMonth, setTsMonth] = useState(currentMonth())
  const [tsData, setTsData] = useState<StudentTurnstileHistory | null>(null)
  const [tsLoading, setTsLoading] = useState(false)
  const [tsSyncing, setTsSyncing] = useState(false)

  /**
   * Tarixni so'rash — kun VA oy birga (bitta so'rovda kunning hodisalari ham, oydagi "faol
   * kunlar" ham keladi: kalendar bo'shab qolmasin).
   */
  const loadTurnstile = useCallback((studentId: string, date: string, month: string) => {
    setTsLoading(true)
    getStudentTurnstileHistory(studentId, date, month)
      .then(setTsData)
      .catch(() => setTsData(null))
      .finally(() => setTsLoading(false))
  }, [])

  // BIRINCHI yuklash — qolgan tablar bilan AYNAN bir xil `tabLoadedFor` naqshi: tab ochilmaguncha
  // so'rov ketmaydi, tablar orasida u yoq-bu yoq o'tilganda esa qayta so'ralmaydi.
  // (Kun/oy almashtirilganda quyidagi ishlovchilar `loadTurnstile` ni O'ZI chaqiradi.)
  useEffect(() => {
    if (tab !== 'turniket' || !id || !canTurnstile || tabLoadedFor.turniket === id) return
    setTabLoadedFor((p) => ({ ...p, turniket: id }))
    loadTurnstile(id, tsDate, tsMonth)
  }, [tab, id, canTurnstile, tabLoadedFor, tsDate, tsMonth, loadTurnstile])

  /** Kalendardan kun tanlash. Bo'sh qiymat (o'sha kunni QAYTA bosish) e'tiborsiz — bu yerda
   *  "kunsiz" holat yo'q: ro'yxat DOIM bitta kunniki. */
  const selectTurnstileDay = (d: string) => {
    if (!d || !id || d === tsDate) return
    setTsDate(d)
    loadTurnstile(id, d, tsMonth)
  }

  /** Oy almashtirish — tanlangan kun ham o'sha oyga ko'chadi (aks holda kalendar bir oyni,
   *  ro'yxat esa boshqasini ko'rsatib turardi). Joriy oyga qaytilsa yana BUGUN tanlanadi. */
  const changeTurnstileMonth = (m: string) => {
    if (!id || m === tsMonth) return
    const d = m === currentMonth() ? todayIso() : `${m}-01`
    setTsMonth(m)
    setTsDate(d)
    loadTurnstile(id, d, m)
  }

  /** «Yangilash» — qurilmadan yangi qaydlarni tortib olib, keyin joriy kunni qayta so'raydi. */
  const onTurnstileSync = async () => {
    if (!id) return
    setTsSyncing(true)
    try {
      const res = await syncStudentTurnstile()
      if (!res.ok && res.message) alert(res.message)
      loadTurnstile(id, tsDate, tsMonth)
    } catch (e) {
      alert(apiErrorMessage(e, 'Sinxronlashda xatolik'))
    } finally {
      setTsSyncing(false)
    }
  }

  /** Qurilma ID'ni biriktirish/olib tashlash (bo'sh qiymat — biriktirishni bekor qiladi). */
  const saveTurnstileDevice = async (value: string) => {
    if (!id) return
    await setStudentDevice(id, value.trim())
    loadTurnstile(id, tsDate, tsMonth)
  }

  // Mount'da FAQAT standart «Guruhlar» tabiga keragi so'raladi — qolgani tab ochilganda
  // (yuqoridagi effektlar): har ortiqcha so'rov O'zbekistondan ~350-400 ms turadi.
  useEffect(() => {
    if (!id) return
    setLoading(true)
    getStudentNotebook(id)
      .then(setData)
      .catch(() => setNotFound(true))
      .finally(() => setLoading(false))
    getStudentGroups(id)
      .then(setGroups)
      .catch(() => {})
  }, [id, reloadKey])

  // O'quvchi ALMASHGANDA (tepadagi qidiruv orqali bir profildan boshqasiga o'tilganda — bir xil route,
  // faqat `id` o'zgaradi) keshlangan TO'LIQ Student obyektlari (callTarget/paymentTarget/smsTarget/editing)
  // eski o'quvchiniki bo'lib qolmasin — aks holda "To'lov qilish"/"SMS"/"Qo'ng'iroq" ESKI o'quvchi ustidan
  // bajariladi (bug: yangi o'quvchini emas, eski o'quvchini chiqarib to'langan deb ko'rsatardi).
  useEffect(() => {
    setCallTarget(null)
    setPaymentTarget(null)
    setSmsTarget(null)
    setEditing(null)
    setCallOpen(false)
    // Bonus ma'lumoti ham eski o'quvchiniki bo'lib qolmasin (yangisi tab ochilganda yuklanadi).
    setBonus(null)
    setBonusError('')
    // Tab ma'lumotlari ham tozalanadi — aks holda yangi o'quvchining tabi ochilganda javob
    // kelguncha ESKI o'quvchining ro'yxati ko'rinib turardi. (`tabLoadedFor` tozalanmaydi —
    // u studentId saqlaydi va o'zi eskiradi, qarang: yuqoridagi izoh.)
    setDiscounts(null)
    setDiscountError('')
    setCoverageLog([])
    setGradingSummary([])
    setTestResults([])
    setCertificates([])
    setSupportFeedback([])
    setAiRecords([])
    setContacts([])
    setCalls([])
    setSms([])
    // Turniket — ma'lumot ham, tanlangan kun/oy ham boshiga qaytadi (yangi o'quvchi uchun
    // "bugun" so'raladi, aks holda avvalgi profildagi sana bilan ochilardi).
    setTsData(null)
    setTsDate(todayIso())
    setTsMonth(currentMonth())
  }, [id])

  /** "Tahrirlash" bosilganda — StudentFormModal uchun TO'LIQ Student kerak (data — StudentNotebook, formaga yaramaydi). */
  const openEdit = () => {
    if (!id) return
    getStudent(id)
      .then(setEditing)
      .catch(() => alert("O'quvchi ma'lumotini yuklab bo'lmadi"))
  }

  /** "SMS yuborish" bosilganda — SmsModal uchun TO'LIQ Student kerak (raqamlar/tokenlar). */
  const openSms = () => {
    if (!id) return
    if (callTarget) {
      setSmsTarget(callTarget)
      return
    }
    getStudent(id)
      .then((s) => {
        setCallTarget(s)
        setSmsTarget(s)
      })
      .catch(() => alert("O'quvchi ma'lumotini yuklab bo'lmadi"))
  }

  /** "Qo'ng'iroq qilish" bosilganda — barcha telefon raqamlari uchun TO'LIQ Student kerak. */
  const openCall = () => {
    if (!id) return
    setCallOpen(true)
    if (callTarget) return
    getStudent(id)
      .then(setCallTarget)
      .catch(() => alert("O'quvchi ma'lumotini yuklab bo'lmadi"))
  }

  /**
   * "To'lov qilish" bosilganda — PaymentModal uchun TO'LIQ Student kerak (balans/guruhlar).
   *
   * ⚠️ KESHDAN OLINMAYDI. Ilgari `callTarget` (qo'ng'iroq uchun bir marta olingan yozuv)
   * qayta ishlatilardi: bitta sessiyada IKKINCHI to'lov kiritilganda oyna to'lovdan OLDINGI
   * «Joriy balans» ni ko'rsatar va «To'lovdan keyingi balans» ham o'shandan hisoblanardi
   * (`handlePayment` faqat `reloadKey` ni oshiradi, keshni emas). Balans — PUL, shuning
   * uchun har ochilishda yangisi so'raladi.
   */
  const openPayment = () => {
    if (!id) return
    getStudent(id)
      .then((s) => {
        setCallTarget(s)
        setPaymentTarget(s)
      })
      .catch(() => alert("O'quvchi ma'lumotini yuklab bo'lmadi"))
  }

  const handlePayment = async (
    amount: number,
    month: string,
    groupId?: string,
    comment?: string,
    method?: string,
    date?: string,
    extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean },
  ) => {
    if (!id) return
    // Xato QAYTA OTILADI — PaymentModal uni o'zi ko'rsatadi (kvitansiya band bo'lsa kartochka
    // + "Baribir saqlash", boshqa xatolarda oddiy xabar). Bu yerda alert qilinmaydi.
    const txId = await addPayment(id, amount, month, groupId, comment, method, date, extra)
    setPaymentTarget(null)
    // Kesh ham eskirdi (balans o'zgardi) — keyingi "To'lov qilish"/"Qo'ng'iroq" yangisini oladi.
    setCallTarget(null)
    setReloadKey((k) => k + 1)
    // CHEK: to'lov saqlangach kvitansiya ochiladi (Kassa bo'limidagi bilan bir xil).
    if (txId) {
      setReceiptAuto(true)
      setReceiptTx(txId)
    }
  }

  /** Guruh a'zoligi ro'yxatini qayta yuklaydi (amal bajarilgach). */
  const reloadGroups = () => {
    if (!id) return
    getStudentGroups(id).then(setGroups).catch(() => {})
  }

  /** Guruh kartasidagi "⋮" menyu — amal boshlanishidan oldin qaysi a'zolikka tegishli ekanini belgilaydi. */
  const openGroupModal = (gr: StudentGroupMembership) => {
    setGroupActionDate(new Date().toISOString().slice(0, 10))
    setGroupModal(gr)
  }

  /** Guruhga oid "chiqarish" sabab kategoriyasi — hozirgi holatga qarab (ClassMembersModal bilan bir xil). */
  const removeGroupCategory = (status: string) =>
    status === 'active' ? 'remove_active' : status === 'frozen' ? 'remove_frozen' : 'remove_trial'

  /** Muzlatish/sinovga qaytarish/chiqarish/aktivlashtirish (sanali) — sabab modali tasdiqlangach. */
  const confirmGroupReason = async (
    reasonId: string | undefined,
    date?: string,
    retentionBonus?: boolean,
  ) => {
    if (!id || !groupModal || !groupReasonAction || groupBusy) return
    setGroupBusy(true)
    try {
      if (groupReasonAction === 'freeze' || groupReasonAction === 'yearFreeze') {
        // AYNAN o'sha endpoint — farq faqat bayroqda (hisob-kitob va holat bir xil).
        await freezeMember(
          groupModal.groupId, id, date ?? groupActionDate, reasonId,
          groupReasonAction === 'yearFreeze',
        )
      } else if (groupReasonAction === 'activate') {
        // Bonus ptichkasi faqat aktivlashtirish oynasida ko'rinadi — qolgan oqimlarda `undefined`.
        await activateMember(groupModal.groupId, id, date ?? groupActionDate, retentionBonus)
      } else if (groupReasonAction === 'remove') {
        await removeGroupMember(groupModal.groupId, id, reasonId)
      } else {
        await returnMemberToTrial(groupModal.groupId, id, reasonId)
      }
      reloadGroups()
      setGroupReasonAction(null)
      setGroupModal(null)
    } catch (err) {
      alert(apiErrorMessage(err, 'Amal bajarilmadi'))
    } finally {
      setGroupBusy(false)
    }
  }

  /** "Guruhga qo'shish" tugmasi — tanlash modalini ochadi (bugungi sana bilan). */
  const openAddGroup = () => {
    setAddGroupTeacherId('')
    setAddGroupId('')
    setAddGroupDate(new Date().toISOString().slice(0, 10))
    setAddGroupOpen(true)
  }

  // Guruhga qo'shish — faqat FAOL a'zolik yo'q guruhlar (studentga hali qo'shilmagan yoki chiqib ketgan).
  const addableGroups = useMemo(
    () => allGroups.filter((g) => !groups.some((gr) => gr.groupId === g.id && gr.isActive)),
    [allGroups, groups],
  )
  // Faqat qo'shish mumkin bo'lgan guruhi bor o'qituvchilar tanlov ro'yxatida ko'rinadi.
  const addGroupTeacherOptions = useMemo(
    () =>
      allTeachers
        .filter((t) => addableGroups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [allTeachers, addableGroups],
  )
  const addGroupOptionsForTeacher = useMemo(
    () => addableGroups.filter((g) => g.teacherId === addGroupTeacherId),
    [addableGroups, addGroupTeacherId],
  )

  const confirmAddGroup = async () => {
    if (!id || !addGroupId || addGroupBusy) return
    setAddGroupBusy(true)
    try {
      await addGroupMember(addGroupId, id, addGroupDate)
      reloadGroups()
      setAddGroupOpen(false)
    } catch (err) {
      alert(apiErrorMessage(err, "Guruhga qo'shib bo'lmadi"))
    } finally {
      setAddGroupBusy(false)
    }
  }

  const applyEdit = (values: StudentPayload) => {
    if (!id) return
    updateStudent(id, values)
      .then(() => setReloadKey((k) => k + 1))
      .catch((e) => alert(e?.response?.data?.message ?? 'Saqlab bo\'lmadi'))
  }

  /**
   * ⚠️ Chegirma bu formadan BOSHQARILMAYDI — «Chegirma» tabidan, HAR FAN uchun alohida
   * beriladi. Shuning uchun "chegirmani joriy oyga qo'llaymizmi?" so'rovi OLIB TASHLANDI:
   * server `PUT /students/{id}` dagi chegirma maydonlarini e'tiborga olmaydi.
   */
  const handleEditSubmit = (values: StudentPayload) => {
    if (!editing) return
    applyEdit(values)
    setEditing(null)
  }

  // O'quv dasturi uchun guruh→kurs id xaritasi. A'zolikda faqat courseName bor (courseId yo'q),
  // shuning uchun guruhlar ro'yxatidan (Group.courseId) groupId orqali kurs id'sini topamiz.
  // Shu ro'yxat "Guruhga qo'shish" modalida ham ishlatiladi.
  useEffect(() => {
    getClasses()
      .then((list) => {
        const map: Record<string, string> = {}
        list.forEach((g) => {
          if (g.courseId) map[g.id] = g.courseId
        })
        setGroupCourse(map)
        setAllGroups(list.filter((g) => !g.isArchived))
      })
      .catch(() => {})
    getTeachers()
      .then((list) => setAllTeachers(list.filter((t) => !t.isArchived)))
      .catch(() => {})
  }, [])

  // Sarlavhadagi guruh/o'qituvchi — data.className/data.homeroomTeacher (Student.ClassName) o'quvchi
  // guruhdan chiqarilganda yoki boshqasiga o'tkazilganda YANGILANMAYDI (legacy maydon, faqat jurnal/
  // chat/hisobot uchun saqlanadi — CLAUDE.md). Shu sabab sarlavhani FAOL M2M a'zoliklardan hisoblaymiz.

  // O'quvchi o'qiydigan ALOHIDA kurslar — faqat FAOL a'zolikdagi guruhlardan, groupId→courseId orqali.
  const studentCourses = useMemo(() => {
    const seen = new Set<string>()
    const out: { courseId: string; courseName: string }[] = []
    groups
      .filter((g) => g.isActive)
      .forEach((g) => {
        const courseId = groupCourse[g.groupId]
        if (!courseId || seen.has(courseId)) return
        seen.add(courseId)
        out.push({ courseId, courseName: g.courseName })
      })
    return out
  }, [groups, groupCourse])

  // O'quvchining barcha oylari — qabul oyidan (yoki eng erta ma'lumot oyidan) joriy/oxirgi oygacha uzluksiz.
  const allMonths = useMemo(() => {
    if (!data) return []
    const present = new Set<string>()
    Object.values(data.grades).forEach((mm) => Object.keys(mm).forEach((k) => present.add(k)))
    const a = data.attendance
    ;[a.missedDays, a.illnessDays, a.missedLessons, a.illnessLessons, a.lateCount].forEach((d) =>
      Object.keys(d).forEach((k) => present.add(k)),
    )
    data.marksTrend.forEach((m) => present.add(m.month))
    const enroll = data.enrollmentDate && data.enrollmentDate.length >= 7 ? data.enrollmentDate.slice(0, 7) : ''
    const cur = new Date().toISOString().slice(0, 7)
    const sorted = [...present].sort()
    const from = [enroll, sorted[0]].filter(Boolean).sort()[0] ?? cur
    const to = [sorted[sorted.length - 1] ?? '', cur].filter(Boolean).sort().slice(-1)[0] ?? from
    return monthRangeList(from, to)
  }, [data])

  /** Barcha oylarda yig'ilgan JAMI ball (bajarilgan baholash mezonlari soni) — o'rtacha emas. */
  const gradingTotalBall = useMemo(
    () => gradingSummary.reduce((sum, m) => sum + m.totalScore, 0),
    [gradingSummary],
  )

  const attendanceChart = useMemo(() => {
    if (!data) return []
    return allMonths.map((m) => ({
      name: monthLabel(m),
      Qoldirgan: data.attendance.missedLessons[m] ?? 0,
      'Kech keldi': data.attendance.lateCount[m] ?? 0,
    }))
  }, [data, allMonths])

  // Standart oy — bahosi bor eng oxirgi oy (bo'lmasa oxirgi oy).
  const lastGradeMonth = useMemo(() => {
    if (!data) return ''
    for (const m of [...allMonths].reverse())
      if (data.subjects.some((s) => data.grades[s.id]?.[m] != null)) return m
    return allMonths[allMonths.length - 1] ?? ''
  }, [data, allMonths])
  useEffect(() => setGradeMonth(lastGradeMonth), [lastGradeMonth])

  // Tanlangan oyda har fan o'rtacha bahosi (bar chart) — fan rangi barqaror (subjects tartibida).
  const monthBars = useMemo(() => {
    if (!data) return []
    return data.subjects
      .map((s, idx) => ({ s, idx }))
      .filter(({ s }) => data.grades[s.id]?.[gradeMonth] != null)
      .map(({ s, idx }) => ({
        name: s.name,
        baho: data.grades[s.id]?.[gradeMonth] ?? 0,
        color: dynColors[idx % dynColors.length],
      }))
  }, [data, gradeMonth])


  const marksChart = useMemo(
    () =>
      data?.marksTrend.map((m) => ({
        name: monthLabel(m.month),
        'Uy vazifa ✓': m.homeworkDone,
        'Uy vazifa ✗': m.homeworkMissed,
        'Xulq ✓': m.behaviorGood,
        'Xulq ✗': m.behaviorBad,
      })) ?? [],
    [data],
  )

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (notFound || !data)
    return (
      <div className="space-y-4">
        <BackLink />
        <Card className="py-16 text-center text-slate-400">O'quvchi topilmadi</Card>
      </div>
    )

  return (
    <div className="space-y-6">
      <BackLink />

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[25%_75%]">
        {/* CHAP USTUN — o'quvchi profili + shaxsiy ma'lumotlar (bitta karta), 40% */}
        <div className="lg:sticky lg:top-4 lg:self-start">
          <Card className="relative space-y-5">
            {/* edutizim profil kartasi: rasm · ism · telefon + nusxa · uch amal tugmasi ·
                ko'rsatkichlar ("To'lash kerak", "Balans", chegirma). Amallar AYNAN eski
                oynalarni ochadi — qoidalar o'zgarmagan. */}
            <ProfileCard
              data={data}
              student={editing}
              discountLabel={studentDiscountLabel({ ...data, discountCount: discounts?.active.length })}
              canEditPhoto={canEditPhoto}
              onPhoto={() => setPhotoOpen(true)}
              onSms={openSms}
              onPay={openPayment}
              onCall={openCall}
              menuItems={[
                { label: 'AI Tahlil', icon: Sparkles, onClick: () => setShowAi(true) },
                ...(canContact
                  ? [{ label: "Bog'lanish kerak", icon: PhoneCall, onClick: () => setContactOpen(true) }]
                  : []),
                ...(can('students.list', 'edit')
                  ? [{ label: 'Tahrirlash', icon: Pencil, onClick: openEdit }]
                  : []),
              ]}
            />

            {/* Shaxsiy ma'lumotlar */}
            <div className="border-t border-slate-100 pt-4">
              <div className="mb-3 flex items-center gap-2">
                <User className="h-5 w-5 text-brand-600" />
                <h2 className="font-semibold text-slate-800">Shaxsiy ma'lumotlar</h2>
              </div>
              <div className="grid grid-cols-2 gap-x-4 gap-y-4">
                <InfoRow icon={User} label="Jinsi" value={genderLabels[data.gender as 'male' | 'female'] ?? data.gender} />
                <InfoRow icon={Cake} label="Tug'ilgan kun" value={data.birthDate ? formatDate(data.birthDate) : '—'} />
                <InfoRow icon={CalendarPlus} label="Qabul sanasi" value={data.enrollmentDate ? formatDate(data.enrollmentDate) : '—'} />
                <InfoRow icon={MapPin} label="Manzil" value={data.address || '—'} />
                <InfoRow icon={GraduationCap} label="Guruh rahbari" value={data.homeroomTeacher || '—'} />
                <InfoRow icon={User} label="Ota-ona" value={data.parentFullName || '—'} />
                <InfoRow icon={Phone} label="Ota-ona telefoni" value={data.parentPhone || '—'} />
                {/* Chegirma HAR FAN uchun alohida bo'lishi mumkin — batafsili «Chegirma» tabida.
                    ⚠️ SANOQ registrdan (`discounts.active`) olinadi: `StudentNotebook` da
                    `discountCount` YO'Q, ya'ni usiz «+N ta fan» qismi HECH QACHON chiqmasdi va
                    bu qator o'sha sahifaning «Chegirma» tabi bilan ZID bo'lardi. */}
                <InfoRow
                  icon={Percent}
                  label="Chegirma"
                  value={
                    studentDiscountLabel({ ...data, discountCount: discounts?.active.length }) ||
                    'Yo\'q'
                  }
                />
              </div>
              {(data.photoUrl || data.parentPassportUrl) && (
                <div className="mt-4 flex flex-wrap gap-4 border-t border-slate-100 pt-4">
                  {data.photoUrl && (
                    <a href={data.photoUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
                      <IdCard className="h-4 w-4" /> O'quvchi rasmi (to'liq)
                    </a>
                  )}
                  {data.parentPassportUrl && (
                    <a href={data.parentPassportUrl} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1.5 text-sm font-medium text-brand-600 hover:underline">
                      <IdCard className="h-4 w-4" /> Ota-ona passporti
                    </a>
                  )}
                </div>
              )}
            </div>
          </Card>
        </div>

        {/* O'NG USTUN — bo'limlar (tab) */}
        <div className="min-w-0 space-y-6">
          <div className="tabs flex-wrap" role="tablist">
            <button type="button" className={cn('tab', tab === 'guruhlar' && 'active')} onClick={() => setTab('guruhlar')}>
              <School className="mr-1 inline h-3.5 w-3.5" /> Guruhlar
            </button>
            <button type="button" className={cn('tab', tab === 'tolov' && 'active')} onClick={() => setTab('tolov')}>
              <History className="mr-1 inline h-3.5 w-3.5" /> To'lov tarixi
            </button>
            {/* Chegirma — pul bilan bog'liq bo'limlar yonma-yon tursin. */}
            <button type="button" className={cn('tab', tab === 'chegirma' && 'active')} onClick={() => setTab('chegirma')}>
              <BadgePercent className="mr-1 inline h-3.5 w-3.5" /> Chegirma
            </button>
            <button type="button" className={cn('tab', tab === 'dastur' && 'active')} onClick={() => setTab('dastur')}>
              <ListChecks className="mr-1 inline h-3.5 w-3.5" /> O'quv dasturi
            </button>
            <button type="button" className={cn('tab', tab === 'baholar' && 'active')} onClick={() => setTab('baholar')}>
              <GraduationCap className="mr-1 inline h-3.5 w-3.5" /> Baholar va davomat
            </button>
            <button type="button" className={cn('tab', tab === 'fikr' && 'active')} onClick={() => setTab('fikr')}>
              <LifeBuoy className="mr-1 inline h-3.5 w-3.5" /> Fikr-mulohaza
            </button>
            <button type="button" className={cn('tab', tab === 'testlar' && 'active')} onClick={() => setTab('testlar')}>
              <ClipboardCheck className="mr-1 inline h-3.5 w-3.5" /> Testlar
            </button>
            <button type="button" className={cn('tab', tab === 'sertifikatlar' && 'active')} onClick={() => setTab('sertifikatlar')}>
              <Award className="mr-1 inline h-3.5 w-3.5" /> Sertifikatlar
            </button>
            <button type="button" className={cn('tab', tab === 'aloqa' && 'active')} onClick={() => setTab('aloqa')}>
              <PhoneCall className="mr-1 inline h-3.5 w-3.5" /> Aloqa
            </button>
            <button type="button" className={cn('tab', tab === 'izohlar' && 'active')} onClick={() => setTab('izohlar')}>
              <StickyNote className="mr-1 inline h-3.5 w-3.5" /> Izohlar
            </button>
            {/* Turniket — alohida sahifa ruxsati (`students.turnstile`) bo'lgan xodimga. */}
            {canTurnstile && (
              <button type="button" className={cn('tab', tab === 'turniket' && 'active')} onClick={() => setTab('turniket')}>
                <DoorOpen className="mr-1 inline h-3.5 w-3.5" /> Turniket
              </button>
            )}
            {/* Bonus — faqat admin/superadmin ko'radi (o'qituvchi haqiga oid ma'lumot). */}
            {isBonusAllowed && (
              <button type="button" className={cn('tab', tab === 'bonus' && 'active')} onClick={() => setTab('bonus')}>
                <Gift className="mr-1 inline h-3.5 w-3.5" /> Bonus
              </button>
            )}
            <button type="button" className={cn('tab', tab === 'ai' && 'active')} onClick={() => setTab('ai')}>
              <Sparkles className="mr-1 inline h-3.5 w-3.5" /> AI Tahlil
            </button>
          </div>

          {/* CHEGIRMA — registr, tarix va oylar bo'yicha haqiqatan qo'llangan summa.
              ⚠️ Har amaldan keyin `reloadKey` oshiriladi: chegirma BALANSGA tegadi, aks holda
              chap ustundagi eski balans ekranda qolib ketardi. */}
          {tab === 'chegirma' && (
            <DiscountSection
              studentId={data.id}
              data={discounts}
              loading={discountLoading}
              error={discountError}
              canEdit={canEditDiscount}
              onChanged={(res) => {
                setDiscounts(res)
                setReloadKey((k) => k + 1)
              }}
            />
          )}

          {/* Bonus — o'quvchini ushlab turish bonusi holati (faqat o'qish uchun) */}
          {tab === 'bonus' && isBonusAllowed && (
            <BonusSection report={bonus} loading={bonusLoading} error={bonusError} />
          )}

          {/* Izohlar — xodim yozadigan erkin eslatmalar (tarix: kim, qachon) */}
          {tab === 'izohlar' && <NotesSection studentId={data.id} />}

          {/* Turniket — SHU o'quvchining kirish/chiqish qaydlari, kun kalendardan tanlanadi */}
          {tab === 'turniket' && canTurnstile && (
            <TurnstileSection
              data={tsData}
              loading={tsLoading}
              syncing={tsSyncing}
              date={tsDate}
              month={tsMonth}
              canEdit={canEditTurnstile}
              onSelectDay={selectTurnstileDay}
              onMonthChange={changeTurnstileMonth}
              onSync={onTurnstileSync}
              onSaveDevice={saveTurnstileDevice}
            />
          )}

          {/* AI Tahlil — saqlangan tahlillar tarixi (kuniga bir marta) */}
          {tab === 'ai' && (
            <AiSection records={aiRecords} onOpen={() => setShowAi(true)} />
          )}

      {/* Guruhlar — o'quvchi bir nechta guruhda bo'lishi mumkin (har biri karta) */}
      {tab === 'guruhlar' && (
      <Section
        title="Guruhlar"
        icon={School}
        action={
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => setJournalOpen(true)}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-600 transition-colors hover:border-brand-300 hover:text-brand-700"
            >
              <NotebookText className="h-4 w-4" /> Jurnalni ko'rish
            </button>
            <button
              type="button"
              onClick={openAddGroup}
              className="inline-flex items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-brand-700"
            >
              <Plus className="h-4 w-4" /> Guruhga qo'shish
            </button>
          </div>
        }
      >
        {groups.length === 0 ? (
          <Empty>Bu o'quvchi hali birorta guruhga qo'shilmagan.</Empty>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2">
            {groups.map((gr) => {
              const sb = groupStatusBadge(
                gr.status === 'completed' ? 'completed' : gr.isActive ? gr.status : 'left',
                gr.yearFreeze,
              )
              // O'qituvchi id'si a'zolikda yo'q — guruhlar ro'yxatidan (allGroups) groupId orqali topamiz (profilga link uchun).
              const grTeacherId = allGroups.find((x) => x.id === gr.groupId)?.teacherId
              return (
                <div
                  key={gr.id}
                  role="button"
                  tabIndex={0}
                  onClick={() => navigate(`/admin/classes/${gr.groupId}`)}
                  onKeyDown={(e) => e.key === 'Enter' && navigate(`/admin/classes/${gr.groupId}`)}
                  className="group relative flex w-full cursor-pointer flex-col gap-2.5 rounded-xl border border-slate-200 p-5 text-left transition-colors hover:border-brand-300 hover:bg-brand-50/30"
                >
                  {gr.isActive && (
                    <div className="absolute right-3 top-3 z-10" onClick={(e) => e.stopPropagation()}>
                      <DropdownMenu
                        items={[
                          ...(gr.status !== 'active'
                            ? [{
                                label: 'Faollashtirish', icon: CheckCircle2,
                                onClick: () => { openGroupModal(gr); setGroupReasonAction('activate') },
                              }]
                            : [
                                {
                                  label: 'Muzlatish', icon: Snowflake,
                                  onClick: () => { openGroupModal(gr); setGroupReasonAction('freeze') },
                                },
                                // «Aktiv muzlatish» — FAQAT superadmin. `can()` bu yerda
                                // yaramaydi: u oddiy admin uchun ham true qaytaradi.
                                ...(user?.role === 'superadmin'
                                  ? [{
                                      label: 'Aktiv muzlatish', icon: CalendarClock,
                                      onClick: () => { openGroupModal(gr); setGroupReasonAction('yearFreeze') },
                                    }]
                                  : []),
                              ]),
                          ...(gr.status !== 'trial'
                            ? [{
                                label: 'Sinov darsiga qaytarish', icon: RotateCcw,
                                onClick: () => { openGroupModal(gr); setGroupReasonAction('return') },
                              }]
                            : []),
                          {
                            label: "Boshqa guruhga o'tkazish", icon: ArrowLeftRight,
                            onClick: () => { openGroupModal(gr); setGroupTransferOpen(true) },
                          },
                          {
                            label: 'Guruhdan chiqarish', icon: X, danger: true,
                            onClick: () => { openGroupModal(gr); setGroupReasonAction('remove') },
                          },
                        ]}
                      />
                    </div>
                  )}
                  <div className="flex flex-col gap-2 pr-8">
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-base font-semibold text-slate-800 group-hover:text-brand-600 group-hover:underline">
                        {gr.groupName}
                      </span>
                      <Badge tone={sb.tone}>{sb.label}</Badge>
                    </div>
                  <p className="flex items-center gap-1.5 text-xs text-slate-400">
                    <CalendarPlus className="h-3.5 w-3.5" /> {formatDate(gr.joinedAt)}
                    {gr.leftAt ? ` – ${formatDate(gr.leftAt)}` : ''}
                  </p>
                  {gr.courseName && (
                    <p className="flex items-center gap-1.5 text-sm text-slate-500">
                      <BookOpen className="h-3.5 w-3.5 text-slate-400" /> {gr.courseName}
                    </p>
                  )}
                  {gr.teacherName && (
                    <p className="flex items-center gap-1.5 text-sm text-slate-500">
                      <User className="h-3.5 w-3.5 text-slate-400" />{' '}
                      {grTeacherId ? (
                        <Link
                          to={`/admin/teachers/${grTeacherId}`}
                          onClick={(e) => e.stopPropagation()}
                          className="hover:text-brand-600 hover:underline"
                        >
                          {gr.teacherName}
                        </Link>
                      ) : (
                        gr.teacherName
                      )}
                    </p>
                  )}
                  {(gr.days.length > 0 || gr.startTime) && (
                    <p className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400">
                      {gr.days.length > 0 && (
                        <span className="inline-flex items-center gap-1">
                          <CalendarDays className="h-3.5 w-3.5" /> {gr.days.map((d) => weekdayShort[d] ?? d).join(', ')}
                        </span>
                      )}
                      {gr.startTime && (
                        <span className="inline-flex items-center gap-1">
                          <Clock className="h-3.5 w-3.5" /> {gr.startTime}{gr.endTime ? `–${gr.endTime}` : ''}
                        </span>
                      )}
                    </p>
                  )}
                  <div className="mt-auto flex items-center justify-between pt-1">
                    <span className="inline-flex items-center gap-1 font-mono text-sm font-medium text-slate-600">
                      <Wallet className="h-3.5 w-3.5 text-slate-400" /> {formatMoney(gr.monthlyFee)}
                    </span>
                    <ChevronRight className="h-4 w-4 text-slate-300 transition-transform group-hover:translate-x-0.5 group-hover:text-brand-500" />
                  </div>
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </Section>
      )}

      {/* To'lov tarixi — a'zolik sanalari (qo'shilgan/aktivlashtirilgan/muzlatilgan) + to'lovlar */}
      {tab === 'tolov' && (
      <>
        {groups.length > 0 && (
          <Section title="A'zolik sanalari" icon={CalendarPlus}>
            <div className="grid gap-3 sm:grid-cols-2">
              {groups.map((gr) => {
                const sb = groupStatusBadge(
                  gr.status === 'completed' ? 'completed' : gr.isActive ? gr.status : 'left',
                  gr.yearFreeze,
                )
                return (
                  <div key={gr.id} className="rounded-xl border border-slate-200 p-3 text-sm">
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-medium text-slate-700">{gr.groupName}</span>
                      <Badge tone={sb.tone}>{sb.label}</Badge>
                    </div>
                    <div className="mt-2 space-y-1 text-xs text-slate-500">
                      <p>
                        Qo'shilgan: <span className="font-mono text-slate-600">{formatDate(gr.joinedAt)}</span>
                      </p>
                      {gr.activatedAt && (
                        <p>
                          Aktivlashtirilgan: <span className="font-mono text-slate-600">{formatDate(gr.activatedAt)}</span>
                        </p>
                      )}
                      {gr.frozenAt && (
                        <p>
                          Muzlatilgan: <span className="font-mono text-slate-600">{formatDate(gr.frozenAt)}</span>
                        </p>
                      )}
                      {gr.leftAt && (
                        <p>
                          Chiqqan: <span className="font-mono text-slate-600">{formatDate(gr.leftAt)}</span>
                        </p>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          </Section>
        )}
        <Card>
          {/* ⚠️ `onChargeEdited` — oylik QO'LDA tahrirlangach chegirma keshi bekor qilinadi:
              server yangi summadan chegirmani qayta hisoblaydi (`discounts.md` §8.5), ya'ni
              «Chegirma» tabidagi jadval aks holda eski raqam bilan qolib ketardi. */}
          <PaymentHistoryPanel
            studentId={data.id}
            onPaid={() => setReloadKey((k) => k + 1)}
            onChargeEdited={reloadDiscounts}
          />
        </Card>
      </>
      )}

      {/* BOG'LANISH KERAK — shu o'quvchi bo'yicha talablar (navbat moduli). Ro'yxat "Aloqa"
          tabida, qo'ng'iroqlar tarixidan YUQORIDA: avval "nima uchun bog'lanish kerak edi",
          keyin "qanday qo'ng'iroqlar bo'lgan". */}
      {tab === 'aloqa' && canSeeContacts && (
        <Section title="Bog'lanish tarixi" icon={PhoneCall}>
          {contacts.length === 0 ? (
            <Empty>Bu o'quvchi bo'yicha bog'lanish talabi ochilmagan.</Empty>
          ) : (
            <ul className="divide-y divide-slate-100">
              {contacts.map((c) => (
                <li key={c.id} className="py-2.5 first:pt-0 last:pb-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <span
                      className={cn(
                        'rounded-md px-2 py-0.5 text-xs font-medium',
                        c.overdue ? 'bg-rose-100 text-rose-700'
                        : c.status === 'new' ? 'bg-amber-100 text-amber-700'
                        : c.status === 'callback' ? 'bg-sky-100 text-sky-700'
                        : c.status === 'done' ? 'bg-emerald-100 text-emerald-700'
                        : 'bg-slate-200 text-slate-600',
                      )}
                    >
                      {c.overdue ? "Muddati o'tgan" : c.statusLabel}
                    </span>
                    <span className="text-sm text-slate-700">{c.reasonLabel || '— sababsiz —'}</span>
                    {c.attemptCount > 0 && (
                      <span className="text-xs text-slate-400">{c.attemptCount} urinish</span>
                    )}
                    {c.status === 'callback' && c.dueDate && (
                      <span className="text-xs text-sky-600">{formatDate(c.dueDate)}</span>
                    )}
                  </div>
                  {/* Talab ochilishidagi izoh (operator uchun "nima haqida gaplashish kerak"). */}
                  {c.note && (
                    <p className="mt-1 text-sm text-slate-500">
                      <span className="text-slate-400">Topshiriq: </span>{c.note}
                    </p>
                  )}

                  {/* Javoblarning O'ZI pastdagi "Qo'ng'iroqlar tarixi" lentasida (qo'ng'iroqlar
                      bilan bitta vaqt o'qida) — bu yerda TAKRORLANMAYDI, aks holda bir xil matn
                      bitta tabda ikki marta chiqardi. Bu bo'lim "nima uchun va qaysi bosqichda"
                      degan savolga javob beradi. */}
                  <p className="mt-1 text-xs text-slate-400">
                    {c.attemptCount > 0
                      ? `Oxirgi harakat: ${formatDateTime(c.lastActionAt || c.createdAt)}`
                      : `Hali bog'lanilmagan — ${formatDateTime(c.createdAt)}`}
                    {c.attemptCount > 0
                      ? c.lastActorName && ` · ${c.lastActorName}`
                      : c.createdBy && ` · ${c.createdBy} ochgan`}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </Section>
      )}

      {/* Qo'ng'iroqlar tarixi (Local Call — agent-telefonlar orqali) */}
      {tab === 'aloqa' && (
      <Section title="Qo'ng'iroqlar tarixi" icon={PhoneCall}>
        {callsLoading ? (
          <Empty>Yuklanmoqda...</Empty>
        ) : callFeed.length === 0 ? (
          <Empty>Bu o'quvchiga hali qo'ng'iroq qilinmagan.</Empty>
        ) : (
          <div className="divide-y divide-slate-100">
            {/* Qo'ng'iroqlar va bog'lanish javoblari BITTA vaqt o'qida — "kim qo'ng'iroq qildi"
                va "nima deyildi" yonma-yon tursin. */}
            {callFeed.map((item) =>
              item.call ? (
                <StudentCallRow key={item.key} call={item.call} />
              ) : (
                <div key={item.key} className="flex gap-3 py-3">
                  <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-brand-50 text-brand-600">
                    <MessageSquareText className="h-4 w-4" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="text-sm font-medium text-slate-700">
                        {item.note!.isNote ? 'Izoh' : "Bog'lanildi"}
                      </span>
                      {item.note!.resultLabel && (
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-xs text-slate-600">
                          {item.note!.resultLabel}
                        </span>
                      )}
                      {item.note!.nextStatusLabel && (
                        <span className="text-xs text-slate-400">→ {item.note!.nextStatusLabel}</span>
                      )}
                      {item.note!.reasonLabel && (
                        <span className="text-xs text-slate-400">· {item.note!.reasonLabel}</span>
                      )}
                    </div>
                    <p className="mt-0.5 text-sm text-slate-600">{item.note!.response}</p>
                    <p className="mt-0.5 text-xs text-slate-400">
                      {formatDateTime(item.note!.at)}
                      {item.note!.actorName && ` · ${item.note!.actorName}`}
                    </p>
                  </div>
                </div>
              ),
            )}
          </div>
        )}
      </Section>
      )}

      {/* SMS tarixi (Eskiz + Local — o'quvchi/ota-ona raqamlariga yuborilgan) */}
      {tab === 'aloqa' && (
      <Section title="SMS tarixi" icon={MessageSquare}>
        {smsLoading ? (
          <Empty>Yuklanmoqda...</Empty>
        ) : sms.length === 0 ? (
          <Empty>Bu o'quvchiga hali SMS yuborilmagan.</Empty>
        ) : (
          <div className="divide-y divide-slate-100">
            {sms.map((s) => (
              <StudentSmsRow key={s.id} sms={s} />
            ))}
          </div>
        )}
      </Section>
      )}

      {/* Tugatgan kurslar va sertifikatlar */}
      {tab === 'sertifikatlar' && (
      <Section
        title="Tugatgan kurslar va sertifikatlar"
        icon={Award}
      >
        {certificates.length === 0 && studentCourses.length === 0 ? (
          <p className="text-sm text-slate-400">Sertifikat yo'q va faol kurs topilmadi.</p>
        ) : (
          <>
            {/* Mavjud sertifikatlar */}
            {certificates.length > 0 && (
              <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {certificates.map((c) => {
                  const revoked = c.status === 'revoked'
                  return (
                    <div
                      key={c.certificateId}
                      className="flex flex-col gap-2 rounded-xl border border-slate-200 bg-gradient-to-br from-amber-50/60 to-slate-50 p-4"
                    >
                      <div className="flex items-start justify-between gap-2">
                        <span className="inline-flex items-center gap-1.5 font-semibold text-slate-800">
                          <Award className="h-4 w-4 shrink-0 text-amber-500" />
                          {c.courseName || 'Kurs'}
                        </span>
                        <Badge tone={revoked ? 'red' : 'green'}>
                          {revoked ? 'Bekor qilingan' : 'Faol'}
                        </Badge>
                      </div>
                      {c.groupName && (
                        <p className="flex items-center gap-1.5 text-xs text-slate-500">
                          <School className="h-3.5 w-3.5 text-slate-400" /> {c.groupName}
                        </p>
                      )}
                      <p className="flex items-center gap-1.5 text-xs text-slate-500">
                        <CalendarPlus className="h-3.5 w-3.5 text-slate-400" /> Berilgan: {formatDate(c.issuedAt)}
                      </p>
                      {c.expiresAt && (
                        <p className="flex items-center gap-1.5 text-xs text-slate-400">
                          <CalendarClock className="h-3.5 w-3.5" /> Muddati: {formatDate(c.expiresAt)}
                        </p>
                      )}
                      <button
                        type="button"
                        onClick={() =>
                          downloadStudentCertificate(data.id, c.certificateId, c.fileName).catch(() =>
                            alert('Sertifikatni yuklab bo\'lmadi'),
                          )
                        }
                        className="mt-auto inline-flex items-center justify-center gap-1.5 rounded-lg border border-brand-200 bg-white px-3 py-2 text-sm font-medium text-brand-700 transition-colors hover:border-brand-300 hover:bg-brand-50"
                      >
                        <Download className="h-4 w-4" /> Yuklab olish
                      </button>
                    </div>
                  )
                })}
              </div>
            )}
            {/* Qo'lda sertifikat yaratish (faol kurslar bo'yicha) */}
            {studentCourses.length > 0 && (
              <div>
                <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                  Qo'lda sertifikat yaratish
                </p>
                <div className="flex flex-wrap gap-2">
                  {studentCourses.map((sc) => (
                    <button
                      key={sc.courseId}
                      type="button"
                      disabled={certGenerating === sc.courseId}
                      onClick={async () => {
                        setCertGenerating(sc.courseId)
                        try {
                          const cert = await generateStudentCertificate(data.id, sc.courseId)
                          setCertificates((prev) => {
                            const exists = prev.find((c) => c.certificateId === cert.certificateId)
                            return exists ? prev : [cert, ...prev]
                          })
                        } catch (e: unknown) {
                          alert('Sertifikat yaratishda xato: ' + (e instanceof Error ? e.message : String(e)))
                        } finally {
                          setCertGenerating(null)
                        }
                      }}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm font-medium text-amber-800 transition-colors hover:bg-amber-100 disabled:opacity-50"
                    >
                      <Award className="h-4 w-4" />
                      {certGenerating === sc.courseId ? 'Yaratilmoqda...' : sc.courseName + ' sertifikat'}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </>
        )}
      </Section>

      )}

      {/* O'quv dasturi (checklist) — har kurs uchun daraja → mavzu → band, bajarilganini belgilash */}
      {tab === 'dastur' && (
      <Section title="O'quv dasturi (checklist)" icon={ListChecks}>
        {studentCourses.length === 0 ? (
          <Empty>O'quvchi hech qaysi kursga biriktirilmagan</Empty>
        ) : (
          <div className="grid grid-cols-1 items-start gap-5 lg:grid-cols-2">
            {studentCourses.map((c) => (
              <CourseCurriculum
                key={c.courseId}
                courseId={c.courseId}
                fallbackName={c.courseName}
                studentId={data.id}
              />
            ))}
          </div>
        )}
      </Section>

      )}

      {/* Ilovada ishlagan topshiriqlari — mashq/test natijalari + javob tafsiloti */}
      {tab === 'dastur' && <AttemptsSection studentId={data.id} />}

      {/* Darslar tarixi — guruh jurnalida belgilangan o'tilgan mavzular, eng yangisi birinchi */}
      {tab === 'dastur' && (
      <Section title="Darslar tarixi (o'tilgan mavzular)" icon={CalendarClock}>
        {coverageLog.length === 0 ? (
          <Empty>Hali dars o'tilmagan</Empty>
        ) : (
          <ul className="space-y-2">
            {coverageLog.map((e, i) => (
              <li
                key={`${e.date}-${i}`}
                className="flex items-start gap-3 rounded-lg border border-slate-100 bg-white px-3 py-2.5 transition-colors hover:border-brand-200 hover:bg-brand-50/30"
              >
                <span className="mt-0.5 shrink-0 rounded-md bg-brand-50 px-2 py-1 font-mono text-xs font-semibold text-brand-700">
                  {formatDate(e.date)}
                </span>
                <div className="min-w-0 flex-1">
                  {e.isRevision ? (
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge tone="amber">Takrorlash</Badge>
                      <span className="text-sm text-slate-600">
                        {e.courseName}
                        {e.groupName ? ` · ${e.groupName}` : ''}
                      </span>
                    </div>
                  ) : (
                    <>
                      <p className="break-words text-sm font-semibold text-slate-800">{e.topicTitle}</p>
                      {e.itemText && (
                        <p className="mt-0.5 break-words text-sm text-slate-500">{e.itemText}</p>
                      )}
                      <p className="mt-1 text-xs text-slate-400">
                        {e.courseName}
                        {e.groupName ? ` · ${e.groupName}` : ''}
                      </p>
                    </>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Section>

      )}

      {/* Stat kartalar — Baholar va davomat */}
      {tab === 'baholar' && (
      <div className="grid gap-4 sm:grid-cols-2">
        <StatCard
          label="O'rtacha baho"
          value={data.avgGrade || '—'}
          icon={GraduationCap}
          iconBg="bg-brand-50"
          iconColor="text-brand-600"
        />
        <StatCard
          label="Davomat"
          value={data.conducted > 0 ? `${data.attendancePct}%` : '—'}
          hint={data.conducted > 0 ? `${data.attended} / ${data.conducted} dars` : undefined}
          icon={CalendarCheck}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
      </div>
      )}

      {/* Diagrammalar */}
      {tab === 'baholar' && (
      <div className="grid gap-6 lg:grid-cols-2">
        <Section title="Davomat (oy bo'yicha)" icon={CalendarCheck}>
          <ResponsiveContainer width="100%" height={280}>
            <BarChart data={attendanceChart} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
              <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
              <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} />
              <Tooltip contentStyle={tooltipStyle} />
              <Legend />
              <Bar dataKey="Qoldirgan" fill="#dc2626" radius={[6, 6, 0, 0]} />
              <Bar dataKey="Kech keldi" fill="#f59e0b" radius={[6, 6, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </Section>

        {marksChart.length > 0 && (
          <Section title="Uy vazifa va xulq (oylik)" icon={BookOpen}>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={marksChart} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} />
                <Tooltip contentStyle={tooltipStyle} />
                <Legend />
                <Bar dataKey="Uy vazifa ✓" fill="#16a34a" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Uy vazifa ✗" fill="#dc2626" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Xulq ✓" fill="#1f47f5" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Xulq ✗" fill="#f59e0b" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </Section>
        )}
      </div>
      )}

      {/* Fan baholari dinamikasi — oy tanlanadi, har fan o'rtacha bahosi bar chartda */}
      {tab === 'baholar' && data.subjects.length > 0 && (
        <Section title="Fan baholari dinamikasi (oy bo'yicha)" icon={GraduationCap}>
          <div className="mb-4 flex flex-wrap gap-1.5">
            {allMonths.map((m) => (
              <button
                key={m}
                type="button"
                onClick={() => setGradeMonth(m)}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-sm font-medium transition-colors',
                  gradeMonth === m
                    ? 'bg-brand-600 text-white'
                    : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                )}
              >
                {monthLabel(m)}
              </button>
            ))}
          </div>

          {monthBars.length === 0 ? (
            <Empty>Bu oyda baho yo'q</Empty>
          ) : (
            <ResponsiveContainer width="100%" height={340}>
              <BarChart data={monthBars} margin={{ top: 16, right: 20, left: 8, bottom: 8 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                <XAxis
                  dataKey="name"
                  tickLine={false}
                  axisLine={false}
                  tick={axisTick}
                  interval={0}
                  angle={-20}
                  textAnchor="end"
                  height={70}
                />
                <YAxis domain={[0, 5]} ticks={[1, 2, 3, 4, 5]} tickLine={false} axisLine={false} width={28} tick={axisTick} />
                <Tooltip contentStyle={tooltipStyle} cursor={{ fill: 'rgba(0,0,0,0.04)' }} />
                <Bar dataKey="baho" name="O'rtacha baho" radius={[6, 6, 0, 0]} maxBarSize={52}>
                  {monthBars.map((b) => (
                    <Cell key={b.name} fill={b.color} />
                  ))}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          )}
        </Section>
      )}

      {/* Baholar matritsasi (fan × oy) */}
      {tab === 'baholar' && (
      <Section title="Baholar (fan × oy)" icon={GraduationCap}>
        {data.subjects.length === 0 ? (
          <Empty>Fan yo'q</Empty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Fan</th>
                  {allMonths.map((m) => (
                    <th key={m} className="whitespace-nowrap px-3 py-2 text-center">{monthLabel(m)}</th>
                  ))}
                  <th className="px-3 py-2 text-center">O'rtacha</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.subjects.map((s) => {
                  const byM = data.grades[s.id] ?? {}
                  const vals = Object.values(byM)
                  const avg = vals.length ? Math.round((vals.reduce((a, b) => a + b, 0) / vals.length) * 10) / 10 : null
                  return (
                    <tr key={s.id} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2 font-medium text-slate-700">{s.name}</td>
                      {allMonths.map((m) => (
                        <td key={m} className="px-3 py-2 text-center">
                          {byM[m] != null ? <span className={gradeCls(byM[m])}>{byM[m]}</span> : <span className="text-slate-300">—</span>}
                        </td>
                      ))}
                      <td className="px-3 py-2 text-center font-mono font-semibold text-slate-800">{avg ?? '—'}</td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Section>
      )}

      {/* Davomat sabablari */}
      {tab === 'baholar' && (
      <Section title="Davomat sabablari" icon={CalendarCheck}>
        {data.reasons.length === 0 ? (
          <Empty>Davomat belgilari yo'q</Empty>
        ) : (
          <div className="flex flex-wrap gap-2">
            {data.reasons.map((r) => (
              <span
                key={r.reasonId}
                className={cn(
                  'inline-flex items-center gap-1 rounded-md px-2 py-1 text-sm font-medium',
                  r.isLate ? 'bg-amber-50 text-amber-700' : 'bg-red-50 text-red-600',
                )}
              >
                {r.name} <span className="font-semibold">×{r.count}</span>
              </span>
            ))}
          </div>
        )}
      </Section>
      )}

      {/* O'QUVCHINING O'QITUVCHI(LAR)I HAQIDAGI FIKRI — har guruh uchun alohida blok.
          Faqat admin/superadmin yozadi va ko'radi (server ham shu rolda cheklaydi);
          o'qituvchiga bu matnlar KO'RSATILMAYDI — ular AI tahlil uchun manba. */}
      {tab === 'fikr' && canWriteTeacherReviews && id && (
        <Section title="O'qituvchilar haqida fikr" icon={MessageSquare}>
          <TeacherReviewsSection studentId={id} />
        </Section>
      )}

      {/* Support feedback — support o'qituvchining o'tilgan darslari (guruhsiz) */}
      {tab === 'fikr' && supportFeedback.length > 0 && (
        <Section title="Support feedback" icon={LifeBuoy}>
          <div className="space-y-2.5">
            {supportFeedback.map((f, i) => (
              <div
                key={i}
                className="rounded-xl border border-slate-100 bg-slate-50/60 p-3"
              >
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">
                    <LifeBuoy className="h-4 w-4 text-teal-600" />
                    {f.teacherName || 'Support'}
                  </span>
                  <span className="flex items-center gap-1 font-mono text-xs text-slate-500">
                    <CalendarClock className="h-3.5 w-3.5" />
                    {formatDate(f.date)} · {f.startTime}–{f.endTime}
                  </span>
                </div>
                {f.topic && (
                  <p className="mt-1.5 text-sm font-medium text-slate-700">{f.topic}</p>
                )}
                {f.notes && <p className="mt-0.5 text-sm text-slate-500">{f.notes}</p>}
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Yig'ilgan ballar — har oyda nechta baholash mezoni bajarilgani (o'rtacha EMAS, JAMI ball) */}
      {tab === 'baholar' && gradingSummary.length > 0 && (
        <Section title="Yig'ilgan ballar" icon={ClipboardCheck}>
          <div className="mb-3 flex items-baseline gap-2 rounded-lg bg-gradient-to-r from-brand-50 to-slate-50 px-4 py-3">
            <span className="text-sm font-medium text-slate-600">Jami yig'ilgan ball:</span>
            <span className="font-mono text-2xl font-bold text-brand-700">{gradingTotalBall}</span>
            <span className="text-xs text-slate-400">
              {gradingSummary.length} oy · bajarilgan baholash mezonlari
            </span>
          </div>
          <div className="flex flex-wrap gap-2">
            {gradingSummary.map((month) => (
              <div
                key={month.month}
                className="flex flex-col gap-1 rounded-lg border border-slate-200 bg-gradient-to-br from-brand-50 to-slate-50 px-4 py-3"
              >
                <p className="text-xs font-medium uppercase tracking-wide text-slate-500">
                  {monthLabel(month.month)}
                </p>
                <div className="flex items-baseline gap-2">
                  <span className="font-mono text-lg font-semibold text-slate-800">
                    {month.totalScore}
                  </span>
                  <span className="text-xs text-slate-400">ball</span>
                </div>
                <p className="text-xs text-slate-600">
                  <span className="font-mono font-semibold">{month.criteriaCount}</span> ta mezon
                </p>
              </div>
            ))}
          </div>
        </Section>
      )}

      {/* Testlar natijalari — barcha guruhlaridan */}
      {tab === 'testlar' && (
      <Section title="Testlar natijalari" icon={ClipboardCheck}>
        {testResults.length === 0 ? (
          <Empty>Test natijalari yo'q</Empty>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Test nomi</th>
                  <th className="px-3 py-2">Guruh</th>
                  <th className="px-3 py-2">Sana</th>
                  <th className="px-3 py-2 text-center">Ball</th>
                  <th className="px-3 py-2 text-center">O'rin</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {testResults.map((t) => (
                  <tr key={t.testId} className="hover:bg-slate-50/60">
                    <td className="px-3 py-2 font-medium text-slate-700">{t.name}</td>
                    <td className="px-3 py-2 text-slate-500">{t.groupName}</td>
                    <td className="px-3 py-2 text-slate-500">{formatDate(t.date)}</td>
                    <td className="px-3 py-2 text-center font-mono font-semibold text-slate-800">
                      {t.score != null ? `${t.score}/${t.maxScore}` : <span className="text-slate-300">—</span>}
                    </td>
                    <td className="px-3 py-2 text-center font-mono font-semibold text-slate-800">
                      {t.rank > 0 ? `${t.rank}/${t.total}` : <span className="text-slate-300">—</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Section>
      )}

        </div>
      </div>

      <StudentJournalModal
        studentId={journalOpen ? data.id : null}
        onClose={() => setJournalOpen(false)}
      />

      <PaymentModal student={paymentTarget} onClose={() => setPaymentTarget(null)} onSubmit={handlePayment} />

      {/* TO'LOV CHEKI — to'lov kiritilgach avtomatik ochiladi va bosib chiqarish dialogini
          chaqiradi (Kassa/Moliya bo'limlaridagi bilan bir xil xatti-harakat). */}
      <ReceiptModal
        txId={receiptTx}
        autoPrint={receiptAuto}
        onClose={() => {
          setReceiptTx(null)
          setReceiptAuto(false)
        }}
      />


      <CallPickerModal
        open={callOpen}
        onClose={() => setCallOpen(false)}
        title={data.fullName}
        studentId={data.id}
        numbers={
          callTarget
            ? dedupeCallOptions([
                { label: "O'z raqami", number: callTarget.phone ?? '' },
                { label: 'Ota-ona', number: callTarget.parentPhone },
                { label: 'Otasi', number: callTarget.fatherPhone ?? '' },
                { label: 'Onasi', number: callTarget.motherPhone ?? '' },
              ])
            : data.parentPhone
              ? [{ label: 'Ota-ona', number: data.parentPhone }]
              : []
        }
      />

      <AiAnalysisModal
        open={showAi}
        onClose={() => setShowAi(false)}
        studentId={data.id}
        studentName={data.fullName}
        records={aiRecords}
        onGenerated={(rec) =>
          setAiRecords((prev) => [rec, ...prev.filter((r) => r.id !== rec.id && r.date !== rec.date)])
        }
        /* Oyna "⋮" menyusidan ochilganda ro'yxatni O'ZI yuklaydi — sahifadagi «AI Tahlil»
           tabi ham o'sha ma'lumot bilan yangilansin (ikki xil holat qolmasin). */
        onLoaded={(recs) => setAiRecords(recs)}
      />

      <StudentFormModal
        open={!!editing}
        onClose={() => setEditing(null)}
        onSubmit={handleEditSubmit}
        initial={editing}
      />

      {/* "Bog'lanish kerak" — sabab so'raladi va o'quvchi navbatga tushadi. */}
      <NeedContactModal
        open={contactOpen}
        students={[{ id: id ?? '', fullName: data.fullName }]}
        onClose={() => setContactOpen(false)}
        onCreated={() => {
          if (id) getStudentContactRequests(id).then(setContacts).catch(() => {})
        }}
      />

      {/* O'quvchi rasmi — dumaloq avatarni bosganda ochiladi (kamera yoki fayl). */}
      <StudentPhotoDialog
        open={photoOpen}
        currentUrl={data.photoUrl ?? null}
        // Rasm hali yo'q bo'lsa darhol kamera yoqiladi — shunisi tezroq.
        startWithCamera={!data.photoUrl}
        onClose={() => setPhotoOpen(false)}
        onSaved={async (url) => {
          if (!id) return
          await updateStudentPhoto(id, url)
          setData((d) => (d ? { ...d, photoUrl: url } : d))
        }}
      />

      <SmsModal
        open={!!smsTarget}
        onClose={() => setSmsTarget(null)}
        recipients={smsTarget ? [smsTarget] : []}
      />

      {/* Muzlatish / aktivlashtirish / sinovga qaytarish / chiqarish — sabab (va sana) tanlash modali */}
      <ReasonPromptModal
        open={!!groupReasonAction}
        category={
          groupReasonAction === 'freeze' || groupReasonAction === 'yearFreeze' ? 'freeze'
          : groupReasonAction === 'activate' ? 'activate'
          : groupReasonAction === 'remove' ? removeGroupCategory(groupModal?.status ?? 'trial')
          : 'return_trial'
        }
        title={
          groupReasonAction === 'freeze' ? 'Muzlatish'
          : groupReasonAction === 'yearFreeze' ? 'Aktiv muzlatish'
          : groupReasonAction === 'activate' ? 'Aktivlashtirish'
          : groupReasonAction === 'remove' ? "Guruhdan chiqarish"
          : 'Sinovga qaytarish'
        }
        message={
          groupModal
            ? groupReasonAction === 'freeze'
              ? `${groupModal.groupName} — shu sanadan boshlab oylik to'lov hisoblanmaydi.`
              : groupReasonAction === 'yearFreeze'
              ? `${groupModal.groupName} — shu sanadan muzlatiladi va «yangi o'quv yiliga o'tish» deb belgilanadi. Hisob-kitob oddiy muzlatish bilan bir xil.`
              : groupReasonAction === 'activate'
                ? `${groupModal.groupName} — shu sanadan boshlab oylik to'lov hisoblanadi.`
                : groupReasonAction === 'remove'
                  ? `${groupModal.groupName} guruhidan chiqarilsinmi?`
                  : `${groupModal.groupName} — sinov holatiga qaytariladi (oylik to'lov hisoblanmaydi).`
            : undefined
        }
        confirmLabel={
          groupReasonAction === 'freeze' ? 'Muzlatish'
          : groupReasonAction === 'yearFreeze' ? 'Aktiv muzlatish'
          : groupReasonAction === 'activate' ? 'Aktivlashtirish'
          : groupReasonAction === 'remove' ? 'Chiqarish'
          : 'Sinovga qaytarish'
        }
        tone={groupReasonAction === 'freeze' || groupReasonAction === 'yearFreeze' ? 'sky' : groupReasonAction === 'remove' ? 'red' : 'brand'}
        showDate={groupReasonAction === 'freeze' || groupReasonAction === 'yearFreeze' || groupReasonAction === 'activate'}
        showRetentionBonus={groupReasonAction === 'activate' && canSetBonus}
        defaultDate={groupActionDate}
        onConfirm={confirmGroupReason}
        onClose={() => setGroupReasonAction(null)}
      />

      {/* Guruhni almashtirish — eski guruh muzlaydi, yangisi aktivlashadi. */}
      {groupModal && (
        <TransferGroupModal
          open={groupTransferOpen}
          onClose={() => setGroupTransferOpen(false)}
          studentId={id ?? ''}
          studentName={data.fullName}
          fromGroupId={groupModal.groupId}
          fromGroupName={groupModal.groupName}
          onDone={() => {
            setGroupTransferOpen(false)
            setGroupModal(null)
            reloadGroups()
          }}
        />
      )}

      {/* "Guruhga qo'shish" — o'quvchini boshqa (yangi) guruhga a'zo qilish (sinov holatida boshlanadi). */}
      <Modal
        open={addGroupOpen}
        onClose={() => !addGroupBusy && setAddGroupOpen(false)}
        size="sm"
        title="Guruhga qo'shish"
        footer={
          <>
            <Button variant="secondary" onClick={() => setAddGroupOpen(false)} disabled={addGroupBusy}>
              Bekor
            </Button>
            <Button onClick={confirmAddGroup} disabled={!addGroupId || addGroupBusy}>
              <Plus className="h-4 w-4" /> {addGroupBusy ? 'Qo\'shilmoqda...' : "Qo'shish"}
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">O'qituvchi</span>
              <select
                value={addGroupTeacherId}
                onChange={(e) => {
                  // O'qituvchi o'zgarsa, oldingi guruh tanlovi tozalanadi (boshqa o'qituvchiga tegishli edi).
                  setAddGroupTeacherId(e.target.value)
                  setAddGroupId('')
                }}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              >
                <option value="">— tanlanmagan —</option>
                {addGroupTeacherOptions.map((t) => (
                  <option key={t.id} value={t.id}>
                    {t.fullName}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <span className="mb-1 block text-sm font-medium text-slate-600">Guruh</span>
              <select
                value={addGroupId}
                disabled={!addGroupTeacherId}
                onChange={(e) => setAddGroupId(e.target.value)}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400 disabled:bg-slate-50 disabled:text-slate-400"
              >
                <option value="">{addGroupTeacherId ? '— guruh tanlang —' : "— avval o'qituvchini tanlang —"}</option>
                {addGroupOptionsForTeacher.map((g) => (
                  <option key={g.id} value={g.id}>
                    {g.name}
                  </option>
                ))}
              </select>
            </div>
          </div>
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Qo'shilgan sana</span>
            <input
              type="date"
              value={addGroupDate}
              onChange={(e) => setAddGroupDate(e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
          </div>
          <p className="text-xs text-slate-400">
            Qo'shilganda a'zolik "sinov" holatida boshlanadi — oylik to'lov faqat aktivlashtirilgandan
            keyin hisoblanadi.
          </p>
        </div>
      </Modal>
    </div>
  )
}

/** Bitta kurs o'quv dasturi: o'z progress bari + yopilgan darajalar (har biri x/y), band checklisti. */
function CourseCurriculum({
  courseId,
  fallbackName,
  studentId,
}: {
  courseId: string
  fallbackName: string
  studentId: string
}) {
  const [curriculum, setCurriculum] = useState<Curriculum | null>(null)
  /** Bajarilgan band id'lari (optimistik). */
  const [done, setDone] = useState<Set<string>>(new Set())
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(false)
    Promise.all([getSubjectCurriculumTree(courseId), getProgress(courseId, studentId)])
      .then(([c, ids]) => {
        if (!alive) return
        setCurriculum(c)
        setDone(new Set(ids))
      })
      .catch(() => {
        if (alive) setError(true)
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [courseId, studentId])

  // Optimistik toggle — xatoda asl holatga qaytaramiz.
  const toggle = (itemId: string, next: boolean) => {
    setDone((prev) => {
      const copy = new Set(prev)
      if (next) copy.add(itemId)
      else copy.delete(itemId)
      return copy
    })
    setProgress(studentId, itemId, next).catch(() => {
      setDone((prev) => {
        const copy = new Set(prev)
        if (next) copy.delete(itemId)
        else copy.add(itemId)
        return copy
      })
    })
  }

  const name = curriculum?.name || fallbackName

  if (loading) {
    return (
      <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4">
        <p className="text-sm font-semibold text-slate-700">{name}</p>
        <div className="mt-3 space-y-2">
          <div className="h-3 w-full animate-pulse rounded bg-slate-200/70" />
          <div className="h-3 w-2/3 animate-pulse rounded bg-slate-200/70" />
        </div>
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4">
        <p className="text-sm font-semibold text-slate-700">{name}</p>
        <p className="mt-2 text-sm text-slate-400">O'quv dasturini yuklab bo'lmadi</p>
      </div>
    )
  }

  const allItems = (curriculum?.modules ?? []).flatMap((m) =>
    m.topics.flatMap((t) => t.lessons.flatMap((l) => l.items)),
  )
  if (allItems.length === 0) {
    return (
      <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4">
        <p className="text-sm font-semibold text-slate-700">{name}</p>
        <p className="mt-2 text-sm text-slate-400">O'quv dasturi kiritilmagan</p>
      </div>
    )
  }

  const totalDone = allItems.filter((it) => done.has(it.id)).length
  const total = allItems.length
  const pct = total ? Math.round((totalDone / total) * 100) : 0

  return (
    <div className="rounded-2xl border border-slate-100 bg-slate-50/40 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">
          <BookOpen className="h-4 w-4 text-brand-600" /> {name}
        </p>
        <span className="font-mono text-xs font-medium text-slate-500">
          {totalDone}/{total} · {pct}%
        </span>
      </div>
      <div className="mt-2 h-2 w-full overflow-hidden rounded-full bg-slate-200/70">
        <div
          className="h-full rounded-full bg-brand-500 transition-all"
          style={{ width: `${pct}%` }}
        />
      </div>
      <div className="mt-3 space-y-2">
        {curriculum!.modules.map((module) => (
          <ModuleBlock key={module.id} module={module} done={done} onToggle={toggle} />
        ))}
      </div>
    </div>
  )
}

/** Yopiladigan modul: ichida mavzular (har biri o'z x/y hisobi bilan). */
function ModuleBlock({
  module,
  done,
  onToggle,
}: {
  module: Curriculum['modules'][number]
  done: Set<string>
  onToggle: (itemId: string, next: boolean) => void
}) {
  const [open, setOpen] = useState(false)
  const items = module.topics.flatMap((t) => t.lessons.flatMap((l) => l.items))
  const mDone = items.filter((it) => done.has(it.id)).length
  const mTotal = items.length
  const complete = mTotal > 0 && mDone === mTotal

  return (
    <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-2 px-3 py-2.5 text-left transition-colors hover:bg-slate-50"
      >
        <ChevronDown
          className={cn('h-4 w-4 shrink-0 text-slate-400 transition-transform', open && 'rotate-180')}
        />
        <span className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-800">{module.name}</span>
        <span
          className={cn(
            'shrink-0 rounded-md px-2 py-0.5 font-mono text-xs font-semibold',
            complete ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
          )}
        >
          {mDone}/{mTotal}
        </span>
      </button>
      {open && (
        <div className="space-y-2 border-t border-slate-100 bg-slate-50/40 px-2 py-2">
          {module.topics.length === 0 ? (
            <p className="px-1 text-xs text-slate-400">Mavzu yo'q</p>
          ) : (
            module.topics.map((topic) => (
              <TopicBlock key={topic.id} topic={topic} done={done} onToggle={onToggle} />
            ))
          )}
        </div>
      )}
    </div>
  )
}

/** Yopiladigan mavzu: o'z x/y hisobi bilan; ochilganda band checklisti. */
function TopicBlock({
  topic,
  done,
  onToggle,
}: {
  topic: Curriculum['modules'][number]['topics'][number]
  done: Set<string>
  onToggle: (itemId: string, next: boolean) => void
}) {
  const [open, setOpen] = useState(false)
  const items = topic.lessons.flatMap((l) => l.items)
  const tDone = items.filter((it) => done.has(it.id)).length
  const tTotal = items.length
  const complete = tTotal > 0 && tDone === tTotal

  return (
    <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="flex w-full items-center gap-2 px-3 py-2.5 text-left transition-colors hover:bg-slate-50"
      >
        <ChevronDown
          className={cn('h-4 w-4 shrink-0 text-slate-400 transition-transform', open && 'rotate-180')}
        />
        <span className="min-w-0 flex-1 truncate text-sm font-medium text-slate-700">{topic.title}</span>
        <span
          className={cn(
            'shrink-0 rounded-md px-2 py-0.5 font-mono text-xs font-semibold',
            complete ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500',
          )}
        >
          {tDone}/{tTotal}
        </span>
      </button>
      {open && (
        <div className="border-t border-slate-100 px-3 py-3">
          {items.length === 0 ? (
            <p className="text-xs text-slate-400">Topshiriq yo'q</p>
          ) : (
            <div className="grid grid-cols-2 gap-x-3 gap-y-1">
              {items.map((item) => {
                const checked = done.has(item.id)
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => onToggle(item.id, !checked)}
                    className="flex w-full items-start gap-2.5 rounded-lg px-2 py-1.5 text-left transition-colors hover:bg-slate-50"
                  >
                    <span
                      className={cn(
                        'mt-0.5 flex h-4 w-4 shrink-0 items-center justify-center rounded border transition-colors',
                        checked
                          ? 'border-brand-500 bg-brand-500 text-white'
                          : 'border-slate-300 bg-white text-transparent',
                      )}
                    >
                      <Check className="h-3 w-3" strokeWidth={3} />
                    </span>
                    <span
                      className={cn(
                        'min-w-0 flex-1 text-sm',
                        checked ? 'text-slate-400 line-through' : 'text-slate-700',
                      )}
                    >
                      {item.text}
                      {item.note && (
                        <span className="ml-1 text-xs text-slate-400">— {item.note}</span>
                      )}
                    </span>
                  </button>
                )
              })}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

/**
 * "Orqaga" havolasi — profil QAYERDAN ochilganiga qarab. Guruh sahifasidan kirilgan bo'lsa
 * (`Link state` da `BackState` keladi) o'sha GURUHGA qaytaradi, aks holda o'quvchilar ro'yxatiga.
 */
function BackLink() {
  const { backTo, backLabel } = readBackState(useLocation().state, {
    backTo: '/admin/students',
    backLabel: "O'quvchilar ro'yxati",
  })
  return (
    <Link
      to={backTo}
      className="inline-flex items-center gap-1.5 text-sm font-medium text-slate-500 hover:text-slate-800"
    >
      <ArrowLeft className="h-4 w-4" /> {backLabel}
    </Link>
  )
}

/** Soniyani mm:ss ko'rinishига aylantiradi. */
function fmtDur(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${m}:${String(s).padStart(2, '0')}`
}

/** Qo'ng'iroqlar tarixidagi bitta qator — yo'nalish ikonasi, vaqt, davomiylik, agent, audio belgisi. */
function StudentCallRow({ call }: { call: StudentCall }) {
  // Yo'nalish + javob holati bo'yicha ikonka/rang: javobsiz kiruvchi — qizil (PhoneMissed),
  // javob berilgan kiruvchi — yashil, chiquvchi — ko'k.
  const missed = call.direction === 'incoming' && !call.answered
  const Icon = missed ? PhoneMissed : call.direction === 'incoming' ? PhoneIncoming : PhoneOutgoing
  const iconColor = missed ? 'text-red-500' : call.direction === 'incoming' ? 'text-emerald-500' : 'text-sky-500'

  return (
    <div className="flex items-center gap-3 py-2.5">
      <Icon className={cn('h-4 w-4 flex-shrink-0', iconColor)} />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm">
          <span className="font-medium text-slate-700">{formatDateTime(call.startedAt)}</span>
          {call.answered && call.durationSec > 0 ? (
            <span className="font-mono text-xs text-slate-500">{fmtDur(call.durationSec)}</span>
          ) : (
            <span className="text-xs font-medium text-red-500">javobsiz</span>
          )}
          {call.hasAudio && (
            <span className="rounded bg-brand-50 px-1.5 py-0.5 text-[11px] font-medium text-brand-600">audio</span>
          )}
        </div>
        <div className="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-slate-400">
          <span className="font-mono">{call.phoneNumber}</span>
          {call.handler && <span>· {call.handler}</span>}
        </div>
      </div>
    </div>
  )
}

/** SMS holatini guruhlaydi (HistoryTab.tsx dagi bilan bir xil mantiq). */
function smsStatusInfo(status: string): { label: string; tone: BadgeTone } {
  const s = (status || '').toUpperCase()
  if (s === 'DELIVRD' || s === 'DELIVERED' || s === 'YUBORILDI') return { label: 'Yetkazildi', tone: 'green' }
  if (s === 'WAITING' || s === 'NEW' || s === 'ACCEPTED' || s === 'STORED')
    return { label: 'Kutilmoqda', tone: 'amber' }
  return { label: 'Yetkazilmadi', tone: 'red' }
}

/** SMS tarixidagi bitta qator — matn, holat, manba (Eskiz/Local), raqam, vaqt. */
function StudentSmsRow({ sms }: { sms: StudentSms }) {
  const st = smsStatusInfo(sms.status)
  return (
    <div className="flex items-start gap-3 py-2.5">
      <MessageSquare className="mt-0.5 h-4 w-4 flex-shrink-0 text-slate-400" />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-sm">
          <span className="font-medium text-slate-700">{formatDateTime(sms.createdAt)}</span>
          <Badge tone={st.tone}>{st.label}</Badge>
          <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[11px] font-medium text-slate-500">
            {sms.provider === 'local' ? 'Local' : 'Eskiz'}
          </span>
        </div>
        <p className="mt-1 whitespace-pre-wrap break-words text-sm text-slate-600">{sms.message}</p>
        <div className="mt-0.5 text-xs text-slate-400">
          <span className="font-mono">{sms.phoneNumber}</span>
        </div>
      </div>
    </div>
  )
}

/* ============================================================================
   TOPSHIRIQ NATIJALARI — o'quvchi ilovada (o'quvchi portalida) ishlagan sillabus
   topshiriqlari. Har ishlash alohida URINISH bo'lib saqlanadi (CourseItemAttempt),
   shuning uchun bir topshiriq bir necha marta ishlansa o'sish dinamikasi ko'rinadi.
   Qatorga bosilsa — har savolga nima javob bergani modalda ochiladi.
   ============================================================================ */

/** Bo'lim turi yorlig'i (jadval "Tur" ustuni). */
const SECTION_LABEL: Record<string, string> = {
  exercise: 'Mashq',
  test: 'Test',
  view: "Ko'rildi",
}

/** Sekundni "3 daq 20 s" ko'rinishida. */
function humanSec(sec: number): string {
  if (sec <= 0) return '—'
  if (sec < 60) return `${sec} s`
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return s ? `${m} daq ${s} s` : `${m} daq`
}

/** Ball nisbatiga qarab rang (baholar jadvalidagi mantiq bilan bir xil). */
function scoreTone(pct: number): BadgeTone {
  if (pct >= 85) return 'green'
  if (pct >= 50) return 'amber'
  return 'red'
}

function AttemptsSection({ studentId }: { studentId: string }) {
  const [rows, setRows] = useState<StudentAttempt[]>([])
  const [stats, setStats] = useState({ itemCount: 0, attemptCount: 0, gradedCount: 0, avgScorePct: 0, totalMinutes: 0 })
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<'all' | 'exercise' | 'test' | 'view'>('all')
  /** Ochilgan urinish tafsiloti (modal). */
  const [detail, setDetail] = useState<{ attempt: StudentAttempt; answers: AttemptAnswer[] } | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    let alive = true
    setLoading(true)
    getStudentAttempts(studentId)
      .then((d) => {
        if (!alive) return
        setRows(d.attempts)
        setStats({
          itemCount: d.itemCount, attemptCount: d.attemptCount,
          gradedCount: d.gradedCount, avgScorePct: d.avgScorePct, totalMinutes: d.totalMinutes,
        })
      })
      .catch(() => alive && setRows([]))
      .finally(() => alive && setLoading(false))
    return () => { alive = false }
  }, [studentId])

  const shown = useMemo(
    () => (filter === 'all' ? rows : rows.filter((r) => r.section === filter)),
    [rows, filter],
  )

  const openDetail = async (a: StudentAttempt) => {
    if (a.answerCount === 0) return
    setDetailLoading(true)
    try {
      setDetail(await getAttemptDetail(a.id))
    } catch {
      setDetail({ attempt: a, answers: [] })
    } finally {
      setDetailLoading(false)
    }
  }

  const tabs: Array<{ key: typeof filter; label: string }> = [
    { key: 'all', label: `Hammasi (${rows.length})` },
    { key: 'exercise', label: `Mashq (${rows.filter((r) => r.section === 'exercise').length})` },
    { key: 'test', label: `Test (${rows.filter((r) => r.section === 'test').length})` },
    { key: 'view', label: `Ko'rilgan (${rows.filter((r) => r.section === 'view').length})` },
  ]

  return (
    <Section title="Topshiriq natijalari (ilovada ishlagan)" icon={ClipboardCheck}>
      {loading ? (
        <Loader />
      ) : rows.length === 0 ? (
        <Empty>O'quvchi hali ilovada birorta topshiriq ishlamagan</Empty>
      ) : (
        <>
          <div className="mb-4 grid gap-3 sm:grid-cols-4">
            <StatCard label="Ishlangan topshiriq" value={String(stats.itemCount)} icon={ListChecks} />
            <StatCard label="Jami urinish" value={String(stats.attemptCount)} icon={History} />
            <StatCard
              label="O'rtacha natija"
              value={stats.gradedCount > 0 ? `${stats.avgScorePct}%` : '—'}
              hint={stats.gradedCount > 0 ? `${stats.gradedCount} ta baholangan urinish` : undefined}
              icon={Percent}
            />
            <StatCard label="Sarflangan vaqt" value={`${stats.totalMinutes} daq`} icon={Clock} />
          </div>

          <div className="mb-3 flex flex-wrap gap-2">
            {tabs.map((t) => (
              <button
                key={t.key}
                type="button"
                onClick={() => setFilter(t.key)}
                className={cn(
                  'rounded-full border px-3 py-1 text-xs font-semibold transition-colors',
                  filter === t.key
                    ? 'border-brand-300 bg-brand-50 text-brand-700'
                    : 'border-slate-200 text-slate-500 hover:border-brand-200 hover:text-brand-600',
                )}
              >
                {t.label}
              </button>
            ))}
          </div>

          {shown.length === 0 ? (
            <Empty>Bu turda natija yo'q</Empty>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-200 text-xs uppercase text-slate-400">
                  <tr>
                    <th className="px-3 py-2">Sana</th>
                    <th className="px-3 py-2">Topshiriq</th>
                    <th className="px-3 py-2">Tur</th>
                    <th className="px-3 py-2 text-center">Urinish</th>
                    <th className="px-3 py-2 text-center">Natija</th>
                    <th className="px-3 py-2 text-center">Vaqt</th>
                    <th className="px-3 py-2" />
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {shown.map((a) => (
                    <tr key={a.id} className="hover:bg-slate-50/60">
                      <td className="whitespace-nowrap px-3 py-2 text-slate-500">{formatDateTime(a.finishedAt)}</td>
                      <td className="px-3 py-2">
                        <p className="font-semibold text-slate-800">{a.itemText}</p>
                        <p className="text-xs text-slate-400">
                          {[a.moduleName, a.topicTitle, a.lessonTitle].filter(Boolean).join(' · ')}
                          {a.groupName ? ` — ${a.groupName}` : ''}
                        </p>
                      </td>
                      <td className="px-3 py-2">
                        <Badge tone={a.section === 'view' ? 'default' : 'violet'}>
                          {SECTION_LABEL[a.section] ?? a.section}
                        </Badge>
                        {a.exerciseKind && (
                          <p className="mt-1 text-xs text-slate-400">{kindTitle(a.exerciseKind as ExerciseKind)}</p>
                        )}
                      </td>
                      <td className="px-3 py-2 text-center text-slate-500">{a.attemptNo}</td>
                      <td className="px-3 py-2 text-center">
                        {a.total > 0 ? (
                          <Badge tone={scoreTone(a.scorePct)}>
                            {a.correct}/{a.total} · {a.scorePct}%
                          </Badge>
                        ) : (
                          <span className="text-slate-300">—</span>
                        )}
                      </td>
                      <td className="whitespace-nowrap px-3 py-2 text-center text-slate-500">{humanSec(a.durationSec)}</td>
                      <td className="px-3 py-2 text-right">
                        {a.answerCount > 0 && (
                          <Button variant="ghost" onClick={() => openDetail(a)} disabled={detailLoading}>
                            Batafsil
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {detail && (
        <Modal open onClose={() => setDetail(null)} title={detail.attempt.itemText} size="lg">
          <div className="mb-3 flex flex-wrap items-center gap-2 text-xs text-slate-500">
            <Badge tone={detail.attempt.section === 'view' ? 'default' : 'violet'}>
              {SECTION_LABEL[detail.attempt.section] ?? detail.attempt.section}
            </Badge>
            <span>{detail.attempt.attemptNo}-urinish</span>
            <span>·</span>
            <span>{formatDateTime(detail.attempt.finishedAt)}</span>
            <span>·</span>
            <span>{humanSec(detail.attempt.durationSec)}</span>
            {detail.attempt.total > 0 && (
              <Badge tone={scoreTone(detail.attempt.scorePct)}>
                {detail.attempt.correct}/{detail.attempt.total} · {detail.attempt.scorePct}%
              </Badge>
            )}
          </div>

          {detail.answers.length === 0 ? (
            <Empty>Javob tafsiloti saqlanmagan</Empty>
          ) : (
            <ul className="space-y-2">
              {detail.answers.map((ans) => (
                <li
                  key={ans.index}
                  className={cn(
                    'rounded-lg border px-3 py-2.5',
                    ans.ok ? 'border-emerald-100 bg-emerald-50/40' : 'border-red-100 bg-red-50/40',
                  )}
                >
                  <div className="flex items-start gap-2">
                    <span className="mt-0.5 shrink-0 rounded-md bg-white px-2 py-0.5 font-mono text-xs font-semibold text-slate-500">
                      {ans.index + 1}
                    </span>
                    <div className="min-w-0 flex-1">
                      {ans.prompt && <p className="break-words text-sm font-semibold text-slate-800">{ans.prompt}</p>}
                      <p className="mt-1 break-words text-sm">
                        <span className="text-slate-400">Javobi: </span>
                        <span className={ans.ok ? 'font-semibold text-emerald-700' : 'font-semibold text-red-600'}>
                          {ans.answer || '—'}
                        </span>
                      </p>
                      {/* To'g'ri javob faqat XATO bo'lganda ko'rsatiladi — to'g'ri javobda ortiqcha. */}
                      {!ans.ok && ans.expected && (
                        <p className="mt-0.5 break-words text-sm">
                          <span className="text-slate-400">To'g'ri javob: </span>
                          <span className="font-semibold text-slate-700">{ans.expected}</span>
                        </p>
                      )}
                    </div>
                    <span className="shrink-0 text-xs text-slate-400">{ans.sec > 0 ? humanSec(ans.sec) : ''}</span>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </Modal>
      )}
    </Section>
  )
}

function Section({
  title,
  icon: Icon,
  children,
  action,
}: {
  title: string
  icon: typeof GraduationCap
  children: React.ReactNode
  action?: React.ReactNode
}) {
  return (
    <Card>
      <div className="mb-4 flex items-center gap-2">
        <Icon className="h-5 w-5 text-brand-600" />
        <h2 className="font-semibold text-slate-800">{title}</h2>
        {action && <div className="ml-auto">{action}</div>}
      </div>
      {children}
    </Card>
  )
}

const Empty = ({ children }: { children: React.ReactNode }) => (
  <p className="py-8 text-center text-sm text-slate-400">{children}</p>
)

/**
 * O'quvchi izohlari — xodim yozadigan erkin eslatmalar (ota-ona bilan suhbat, to'lov kelishuvi, ...).
 *
 * Ro'yxat/yozish/tahrirning O'ZI `StudentNotesThread` da — AYNAN o'sha komponent
 * "O'quvchilar → Izohlarga javoblar" sahifasida ham ishlatiladi (nusxa YO'Q, qoida bir joyda).
 */
function NotesSection({ studentId }: { studentId: string }) {
  return (
    <Section title="Izohlar" icon={StickyNote}>
      <StudentNotesThread studentId={studentId} />
    </Section>
  )
}

/** ISO vaqtdan "yyyy-MM-dd HH:mm" (oxirgi sinxronizatsiya uchun). */
const syncLabel = (iso: string) => (iso && iso.length >= 16 ? `${iso.slice(0, 10)} ${iso.slice(11, 16)}` : '—')

/**
 * TURNIKET — SHU o'quvchining kirish/chiqish qaydlari (turniket/FaceID qurilmasidan).
 *
 * <p>Ilgari bu alohida sahifa edi va BARCHA o'quvchini BITTA kun uchun ko'rsatardi. Lekin
 * amaldagi savol deyarli har doim bitta odam haqida ("shu bola qachon kelgan, qachon
 * ketgan"), shuning uchun ro'yxat profil ichiga ko'chdi: kesim — o'quvchi, kun esa oylik
 * kalendardan tanlanadi (bog'lanish navbati va izohlar sahifasidagi AYNAN shu komponent).</p>
 *
 * <p>Kalendarda kunlar SON bilan emas, NUQTA bilan belgilanadi: server `activeDays` ni beradi,
 * ya'ni "qayd bor/yo'q" — har katakka "1" yozib qo'yish aldamchi bo'lardi.</p>
 */
function TurnstileSection({
  data,
  loading,
  syncing,
  date,
  month,
  canEdit,
  onSelectDay,
  onMonthChange,
  onSync,
  onSaveDevice,
}: {
  data: StudentTurnstileHistory | null
  loading: boolean
  syncing: boolean
  date: string
  month: string
  canEdit: boolean
  onSelectDay: (date: string) => void
  onMonthChange: (month: string) => void
  onSync: () => void
  onSaveDevice: (deviceUserId: string) => Promise<void>
}) {
  /** Qurilma ID inputi — `null` bo'lsa tahrir boshlanmagan (serverdagi qiymat ko'rinadi). */
  const [draft, setDraft] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState('')

  const device = data?.deviceUserId ?? ''
  const save = async () => {
    if (draft === null || draft.trim() === device.trim()) {
      setDraft(null)
      return
    }
    setSaving(true)
    setErr('')
    try {
      await onSaveDevice(draft)
      setDraft(null)
    } catch (e) {
      setErr(apiErrorMessage(e, "Qurilma ID'ni saqlashda xatolik"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Section
      title="Turniket"
      icon={DoorOpen}
      action={
        <Button variant="secondary" onClick={onSync} disabled={syncing}>
          <RefreshCw className={cn('h-4 w-4', syncing && 'animate-spin')} />
          {syncing ? 'Yangilanmoqda...' : 'Yangilash'}
        </Button>
      }
    >
      {/* Integratsiya holati + oxirgi sinxronizatsiya */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        {data?.enabled ? (
          <Badge tone="green">
            <Wifi className="h-3.5 w-3.5" /> Turniket yoqilgan
          </Badge>
        ) : (
          <Link to="/admin/settings/turnstile" className="hover:opacity-80">
            <Badge>
              <WifiOff className="h-3.5 w-3.5" /> Turniket o'chiq — sozlash
            </Badge>
          </Link>
        )}
        {data?.lastSync && (
          <span className="text-xs text-slate-400">Oxirgi sinx: {syncLabel(data.lastSync)}</span>
        )}
      </div>

      {/* Qurilma ID — bo'sh bo'lsa qaydlar UMUMAN kelmaydi, shuning uchun eng tepada */}
      <div className="mb-4 rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <div className="min-w-0">
            <p className="text-xs text-slate-400">Qurilma ID (employeeNo)</p>
            {canEdit ? (
              <input
                value={draft ?? device}
                onChange={(e) => setDraft(e.target.value)}
                onBlur={save}
                onKeyDown={(e) => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
                disabled={saving}
                placeholder="ID..."
                title="Turniket qurilmasidagi raqam (employeeNo). Bo'sh qoldirilsa biriktirish bekor qilinadi."
                className="mt-1 w-36 rounded-md border border-slate-200 bg-white px-2 py-1 font-mono text-sm text-slate-700 outline-none focus:border-brand-400 disabled:opacity-50"
              />
            ) : (
              <p className="mt-1 font-mono text-sm font-medium text-slate-700">{device || '—'}</p>
            )}
          </div>
          {!device && (
            <p className="text-xs text-amber-600">
              Bu o'quvchiga turniket qurilmasi biriktirilmagan — qaydlar yig'ilmaydi.
              {canEdit && " Qurilmadagi raqamni yozing."}
            </p>
          )}
        </div>
        {err && <p className="mt-2 text-xs font-medium text-red-500">{err}</p>}
      </div>

      {/* Kun tanlash — oylik kalendar chizig'i (nuqta = o'sha kuni qayd bor) */}
      <MonthDayStrip
        month={month}
        onMonthChange={onMonthChange}
        selected={date}
        onSelect={onSelectDay}
        marked={data?.activeDays ?? []}
        hint="Nuqta bilan belgilangan kunlarda turniketdan o'tish qayd etilgan. Kunni bosing — o'sha kunning qaydlari chiqadi."
      />

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : !data ? (
        <Empty>Turniket qaydlarini yuklab bo'lmadi</Empty>
      ) : !data.enabled ? (
        <Empty>
          Turniket integratsiyasi o'chirilgan — qaydlar yig'ilmayapti.{' '}
          <Link to="/admin/settings/turnstile" className="font-medium text-brand-600 hover:underline">
            Sozlamalar
          </Link>
        </Empty>
      ) : !device ? (
        <Empty>Qurilma biriktirilmagan — ko'rsatadigan qayd yo'q</Empty>
      ) : (
        <>
          {/* Kunning jamlanmasi */}
          <div className="mt-4 grid grid-cols-3 gap-3">
            <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
              <div className="font-mono text-2xl font-semibold text-slate-800">{data.passes}</div>
              <div className="text-xs text-slate-400">O'tishlar soni</div>
            </div>
            <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
              <div className="font-mono text-2xl font-semibold text-emerald-600">{data.firstIn || '—'}</div>
              <div className="text-xs text-slate-400">Birinchi kirish</div>
            </div>
            <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
              <div className="font-mono text-2xl font-semibold text-slate-500">{data.lastOut || '—'}</div>
              <div className="text-xs text-slate-400">Oxirgi chiqish</div>
            </div>
          </div>

          {/* Kunning hodisalari */}
          {data.events.length === 0 ? (
            <Empty>{formatDate(date)} kuni turniketdan o'tish qayd etilmagan</Empty>
          ) : (
            <div className="mt-2 divide-y divide-slate-100">
              {data.events.map((ev, i) => {
                const isIn = ev.direction === 'in'
                const Icon = isIn ? LogIn : LogOut
                return (
                  <div key={`${ev.time}-${i}`} className="flex items-center gap-3 py-2.5">
                    <Icon className={cn('h-4 w-4 shrink-0', isIn ? 'text-emerald-500' : 'text-slate-400')} />
                    <span className="font-mono text-sm font-semibold text-slate-700">{ev.time}</span>
                    <Badge tone={isIn ? 'green' : 'default'}>{isIn ? 'Kirdi' : 'Chiqdi'}</Badge>
                    <span className="min-w-0 truncate text-xs text-slate-400">{ev.deviceName || '—'}</span>
                  </div>
                )
              })}
            </div>
          )}
        </>
      )}
    </Section>
  )
}

/** O'quvchi sahifasidagi "AI Tahlil" bo'limi — saqlangan tahlillar (tarix) + tanlangani diagrammalar bilan. */
function AiSection({
  records,
  onOpen,
}: {
  records: StudentAiAnalysisRecord[]
  onOpen: () => void
}) {
  const [selId, setSelId] = useState<string | null>(null)
  const sel = records.find((r) => r.id === selId) ?? records[0] ?? null
  return (
    <Section
      title="AI Tahlil"
      icon={Sparkles}
      action={
        <button
          type="button"
          onClick={onOpen}
          className="inline-flex items-center gap-1.5 rounded-lg bg-gradient-to-r from-brand-600 to-violet-500 px-3 py-1.5 text-xs font-semibold text-white transition-all hover:opacity-90 active:translate-y-px"
        >
          <Sparkles className="h-3.5 w-3.5" /> {records.length ? 'Yangi tahlil' : 'Tahlil qilish'}
        </button>
      }
    >
      {records.length === 0 ? (
        <Empty>
          Hali AI tahlil qilinmagan. "Tahlil qilish" tugmasini bosing — AI o'quvchining barcha ma'lumotlarini
          (baholar, davomat, uy vazifa, xulq, balans) tahlil qilib, diagrammalar va tavsiyalar beradi.
        </Empty>
      ) : (
        <div className="space-y-4">
          {records.length > 1 && (
            <div className="flex flex-wrap gap-2">
              {records.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => setSelId(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors',
                    sel?.id === r.id
                      ? 'border-brand-300 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                  )}
                >
                  {formatDate(r.date)} · <span className="font-mono">{r.overallScore}</span>
                </button>
              ))}
            </div>
          )}
          {sel && <AiAnalysisView record={sel} />}
        </div>
      )}
    </Section>
  )
}

/* ============================================================================
   BONUS — o'quvchini ushlab turish bonusi. FAQAT ko'rsatish uchun: bonus berish,
   bekor qilish va siklni qayta boshlash "O'quvchilar → Bonus hisoboti" sahifasida.
   Oylik holatlar hech qayerda saqlanmaydi — server har so'rovda qayta hisoblaydi.
   ============================================================================ */

/** Oy katagining belgisi va tushuntirishi (bonus hisobotidagi ko'rinishning shu sahifaga mos nusxasi). */
const BONUS_STATE_UI: Record<RetentionState, { icon: string; title: string; cls: string }> = {
  paid: { icon: '✅', title: "To'liq — o'qidi va to'ladi (sanoqqa kiradi)", cls: 'bg-emerald-50 text-emerald-700' },
  debt: { icon: '⏳', title: "Qarzdor — o'qidi, lekin to'lov hali yo'q (sikl uzilmaydi)", cls: 'bg-amber-50 text-amber-700' },
  nocharge: { icon: '📄', title: 'Hisob yozilmagan — sanoqqa kirmaydi (sikl uzilmaydi)', cls: 'bg-violet-50 text-violet-700' },
  frozen: { icon: '❄️', title: "Muzlatilgan — sanoq to'xtaydi, oyna cho'ziladi", cls: 'bg-sky-50 text-sky-700' },
  gone: { icon: '🚪', title: "A'zolik yo'q — sanoq to'xtaydi", cls: 'bg-slate-100 text-slate-500' },
}

/** Qator (fan) holati — badge yorlig'i va rangi. */
const BONUS_STATUS_UI: Record<RetentionStatus, { label: string; tone: BadgeTone }> = {
  notstarted: { label: 'Boshlanmagan', tone: 'amber' },
  progress: { label: "Yo'lda", tone: 'default' },
  ready: { label: 'Tayyor', tone: 'green' },
  broken: { label: 'Uzildi', tone: 'red' },
  blocked: { label: 'Bonus berilgan', tone: 'amber' },
}

function BonusSection({
  report,
  loading,
  error,
}: {
  report: RetentionReport | null
  loading: boolean
  error: string
}) {
  const rows = report?.rows ?? []
  const settings = report?.settings

  // Bonuslar HAR QATORDA (fan bo'yicha) qaytadi — id bo'yicha takrorlanmasin, eng yangisi tepada.
  const awards = useMemo(() => {
    const map = new Map<string, RetentionAward>()
    rows.forEach((r) => r.awards.forEach((a) => map.set(a.id, a)))
    return [...map.values()].sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
  }, [rows])
  // JAMIga faqat BERILGAN bonuslar kiradi — bekor qilinganlari hisoblanmaydi.
  const given = awards.filter((a) => a.status === 'given')
  const givenTotal = given.reduce((s, a) => s + a.totalAmount, 0)

  if (loading)
    return (
      <Section title="Bonus" icon={Gift}>
        <Loader label="Yuklanmoqda..." />
      </Section>
    )

  if (error)
    return (
      <Section title="Bonus" icon={Gift}>
        <p className="py-8 text-center text-sm text-red-600">{error}</p>
      </Section>
    )

  return (
    <div className="space-y-6">
      <Section title="Bonus — o'quvchini ushlab turish" icon={Gift}>
        <div className="grid gap-4 sm:grid-cols-3">
          <StatCard
            label="Bonus olganmi?"
            value={given.length > 0 ? 'Ha' : "Yo'q"}
            hint={
              given.length > 0
                ? `${given.length} ta bonus · ${formatMoney(givenTotal)}`
                : 'Hali bonus berilmagan'
            }
            icon={Award}
            iconBg={given.length > 0 ? 'bg-emerald-50' : 'bg-slate-100'}
            iconColor={given.length > 0 ? 'text-emerald-600' : 'text-slate-400'}
          />
          <StatCard
            label="Bonus tizimida"
            value={rows.length > 0 ? 'Ha' : "Yo'q"}
            hint={
              rows.length > 0
                ? `${rows.length} ta fan bo'yicha kuzatilmoqda`
                : 'Ptichka yoqilmagan'
            }
            icon={ListChecks}
            iconBg={rows.length > 0 ? 'bg-brand-50' : 'bg-slate-100'}
            iconColor={rows.length > 0 ? 'text-brand-600' : 'text-slate-400'}
          />
          <StatCard
            label="Talab qilinadigan muddat"
            value={settings ? `${settings.monthsRequired} oy` : '—'}
            hint={settings ? `Ruxsat etilgan tanaffus: ${settings.maxGapMonths} oy` : undefined}
            icon={CalendarClock}
            iconBg="bg-indigo-50"
            iconColor="text-indigo-600"
          />
        </div>

        {rows.length === 0 ? (
          <Empty>
            Bu o'quvchi bonus tizimiga kiritilmagan. O'quvchi formasidagi «Ushlab turish bonusi»
            ptichkasini yoqsangiz, sanoq shu yerda ko'rina boshlaydi.
          </Empty>
        ) : (
          <div className="mt-4 rounded-xl border border-slate-100 bg-slate-50/60 p-3">
            <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
              Qaysi oydan sanaladi
            </p>
            <div className="space-y-1.5">
              {rows.map((r) => (
                <div
                  key={r.courseId}
                  className="flex flex-wrap items-center justify-between gap-2 text-sm"
                >
                  <span className="font-medium text-slate-700">
                    {r.courseName || "Fan ko'rsatilmagan"}
                  </span>
                  <span className="text-slate-500">
                    {r.startMonth
                      ? `${monthLabel(r.startMonth)} dan · ${r.cycleNo}-sikl`
                      : 'Boshlanish oyi kiritilmagan'}
                  </span>
                </div>
              ))}
            </div>
          </div>
        )}
      </Section>

      {/* Har FAN uchun alohida sikl — guruh/o'qituvchi, sanoq va oylik kataklar */}
      {rows.length > 0 && (
        <Section title="Fanlar bo'yicha sanoq" icon={BookOpen}>
          {/* Oy kataklari izohi */}
          <div className="mb-4 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
            {(Object.keys(BONUS_STATE_UI) as RetentionState[]).map((s) => (
              <span key={s}>
                {BONUS_STATE_UI[s].icon} {BONUS_STATE_UI[s].title}
              </span>
            ))}
          </div>
          <div className="space-y-4">
            {rows.map((r) => (
              <BonusCourseCard key={r.courseId} row={r} />
            ))}
          </div>
        </Section>
      )}

      {/* Berilgan bonuslar tarixi — barcha fanlar bo'yicha, eng yangisi tepada */}
      {awards.length > 0 && (
        <Section
          title="Berilgan bonuslar tarixi"
          icon={History}
          action={
            <span className="text-sm text-slate-500">
              Jami: <span className="font-mono font-semibold text-slate-700">{formatMoney(givenTotal)}</span>
            </span>
          }
        >
          <div className="space-y-3">
            {awards.map((a) => {
              const cancelled = a.status === 'cancelled'
              return (
                <div
                  key={a.id}
                  className={cn(
                    'rounded-xl border border-slate-200 p-3',
                    cancelled && 'bg-slate-50/60 opacity-60',
                  )}
                >
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <span className="inline-flex items-center gap-1.5 text-sm font-semibold text-slate-800">
                      <Award className="h-4 w-4 shrink-0 text-amber-500" />
                      {a.courseName || "Fan ko'rsatilmagan"}
                    </span>
                    <div className="flex items-center gap-2">
                      <span
                        className={cn(
                          'font-mono text-sm font-semibold',
                          cancelled ? 'text-slate-400 line-through' : 'text-emerald-700',
                        )}
                      >
                        {formatMoney(a.totalAmount)}
                      </span>
                      {cancelled && <Badge tone="red">bekor qilingan</Badge>}
                    </div>
                  </div>
                  <p className="mt-1 text-xs text-slate-500">
                    {a.cycleNo}-sikl · {monthLabel(a.periodFrom)} — {monthLabel(a.periodTo)}
                  </p>
                  {a.shares.length > 0 && (
                    <div className="mt-1.5 flex flex-wrap gap-1.5">
                      {a.shares.map((s) => (
                        <span
                          key={s.teacherId}
                          className="inline-flex items-center gap-1 rounded-md bg-slate-100 px-2 py-0.5 text-xs text-slate-600"
                        >
                          <Link
                            to={`/admin/teachers/${s.teacherId}`}
                            className="font-medium hover:text-brand-600 hover:underline"
                          >
                            {s.teacherName}
                          </Link>
                          <span className="font-mono">{formatMoney(s.amount)}</span>
                          <span className="text-slate-400">· {s.months} oy</span>
                        </span>
                      ))}
                    </div>
                  )}
                  <p className="mt-1.5 text-xs text-slate-400">
                    {formatDateTime(a.createdAt)}
                    {a.givenBy ? ` · bergan: ${a.givenBy}` : ''}
                    {a.note ? ` · ${a.note}` : ''}
                  </p>
                  {cancelled && a.cancelReason && (
                    <p className="mt-1 text-xs text-red-600">Bekor qilish sababi: {a.cancelReason}</p>
                  )}
                </div>
              )
            })}
          </div>
        </Section>
      )}
    </div>
  )
}

/** Bitta FAN bo'yicha sikl kartochkasi: guruh/o'qituvchi, sanoq progressi va oylik kataklar. */
function BonusCourseCard({ row }: { row: RetentionRow }) {
  const st = BONUS_STATUS_UI[row.status]
  const pct = row.required > 0 ? Math.min(100, Math.round((row.counted / row.required) * 100)) : 0

  return (
    <div className="rounded-2xl border border-slate-200 p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-sm font-semibold text-slate-800">
          <BookOpen className="h-4 w-4 text-brand-600" /> {row.courseName || "Fan ko'rsatilmagan"}
        </p>
        <div className="flex items-center gap-2">
          {row.isArchived && <Badge tone="red">arxivda</Badge>}
          {st && <Badge tone={st.tone}>{st.label}</Badge>}
        </div>
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500">
        <span className="inline-flex items-center gap-1">
          <School className="h-3.5 w-3.5 text-slate-400" />
          {row.groups.length === 0
            ? '—'
            : row.groups.map((g, i) => (
                <span key={g.id}>
                  {i > 0 && ', '}
                  <Link to={`/admin/classes/${g.id}`} className="hover:text-brand-600 hover:underline">
                    {g.name}
                  </Link>
                </span>
              ))}
        </span>
        <span className="inline-flex items-center gap-1">
          <User className="h-3.5 w-3.5 text-slate-400" />
          {row.teachers.length === 0
            ? '—'
            : row.teachers.map((t, i) => (
                <span key={t.id}>
                  {i > 0 && ', '}
                  <Link to={`/admin/teachers/${t.id}`} className="hover:text-brand-600 hover:underline">
                    {t.name}
                  </Link>
                </span>
              ))}
        </span>
        {row.days && (
          <span className="inline-flex items-center gap-1">
            <CalendarDays className="h-3.5 w-3.5 text-slate-400" /> {row.days}
          </span>
        )}
      </div>

      {/* Sanoq — nechta oy yig'ilgan / nechta kerak */}
      <div className="mt-3">
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
          <span className="text-slate-500">
            {row.startMonth
              ? `${monthLabel(row.startMonth)} dan · ${row.cycleNo}-sikl`
              : 'Boshlanish oyi kiritilmagan'}
          </span>
          <span className="font-mono font-semibold text-slate-600">
            {row.counted}/{row.required}
          </span>
        </div>
        <div className="mt-1.5 h-2 w-full overflow-hidden rounded-full bg-slate-200/70">
          <div className="h-full rounded-full bg-brand-500 transition-all" style={{ width: `${pct}%` }} />
        </div>
      </div>

      {row.statusNote && <p className="mt-2 text-xs text-slate-400">{row.statusNote}</p>}

      {/* Oylik kataklar — belgi ustiga kursor olib borilsa tushuntirish chiqadi */}
      {row.months.length > 0 && (
        <div className="mt-3 flex flex-wrap items-start gap-1">
          {row.months.map((m) => (
            <div
              key={m.month}
              title={`${monthLabel(m.month)} — ${BONUS_STATE_UI[m.state].title}${
                m.teacherName ? `\nO'qituvchi: ${m.teacherName}` : ''
              }${
                m.state === 'debt'
                  ? `\nHisoblangan: ${formatMoney(m.charged)} · To'langan: ${formatMoney(m.paid)}`
                  : ''
              }${m.counted ? '\nSanoqqa kirdi (+1)' : ''}`}
              className={cn('w-14 rounded-md px-1 py-1 text-center', BONUS_STATE_UI[m.state].cls)}
            >
              <div className="text-sm leading-none">{BONUS_STATE_UI[m.state].icon}</div>
              <div className="mt-0.5 text-[10px] leading-tight opacity-70">{m.month.slice(5)}</div>
              <div className="truncate text-[10px] leading-tight opacity-60">
                {m.teacherName ? m.teacherName.split(' ')[0] : ''}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function InfoRow({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof GraduationCap
  label: string
  value: string
}) {
  return (
    <div className="flex items-start gap-2.5">
      <Icon className="mt-0.5 h-4 w-4 shrink-0 text-slate-400" />
      <div className="min-w-0">
        <p className="text-xs text-slate-400">{label}</p>
        <p className="break-words text-sm font-medium text-slate-700">{value}</p>
      </div>
    </div>
  )
}

/** "yyyy-MM" dan "yyyy-MM" gacha (inklyuziv) uzluksiz oylar ro'yxati. */
function monthRangeList(from: string, to: string): string[] {
  if (!from || !to || from > to) return from ? [from] : []
  const out: string[] = []
  let y = Number(from.slice(0, 4))
  let m = Number(from.slice(5, 7))
  const ty = Number(to.slice(0, 4))
  const tm = Number(to.slice(5, 7))
  while (y < ty || (y === ty && m <= tm)) {
    out.push(`${y}-${String(m).padStart(2, '0')}`)
    m++
    if (m > 12) {
      m = 1
      y++
    }
  }
  return out
}

/** Bo'sh raqamlarni tashlab, bir xil raqamni faqat birinchi label bilan qoldiradi (CallPickerModal uchun). */
function dedupeCallOptions(options: CallOption[]): CallOption[] {
  const seen = new Set<string>()
  return options.filter((o) => {
    if (!o.number || seen.has(o.number)) return false
    seen.add(o.number)
    return true
  })
}

function gradeCls(g: number): string {
  return cn('inline-flex h-6 min-w-6 items-center justify-center rounded px-1 text-sm font-semibold', gradeBadgeCls(g))
}

