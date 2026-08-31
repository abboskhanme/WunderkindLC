import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import {
  ArrowLeft,
  ArrowUpRight,
  ChevronLeft,
  ChevronRight,
  Users,
  TrendingUp,
  Wallet,
  Info,
  Trophy,
  Medal,
  Award,
  Plus,
  Archive,
  Sparkles,
  MessageSquareQuote,
  Camera,
  UserCheck,
} from 'lucide-react'
import type {
  Credentials,
  Group,
  GroupSalaryLine,
  MonthSalary,
  SalaryLedger,
  Subject,
  Teacher,
  TeacherPerformance,
  TeacherRating,
  TeacherRatingRow,
} from '@/types'
import {
  getTeachers,
  getTeacherCredentials,
  getTeacherPerformanceSingle,
  getTeacherRating,
  getSalaryLedger,
  getSalaryMonth,
  resetTeacherPassword,
  saveGroupSalaries,
  updateTeacherPhoto,
  type GroupSalaryItem,
} from '@/api/services/teachers'
import { createTransaction } from '@/api/services/finance'
import { getClasses } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { genderLabels, formatMonth } from '@/config/constants'
import { formatDate, formatMoney, cn, apiErrorMessage } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { CredentialsBox } from '@/components/ui/CredentialsBox'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { PhotoDialog } from '@/components/media/PhotoDialog'
import { TeacherBonusPanel } from '@/components/retention/TeacherBonusPanel'
import { TeacherAiPanel } from './TeacherAiPanel'
import { TeacherReviewsFeed } from './TeacherReviewsFeed'
import {
  getTeacherRetentionBonuses,
  type TeacherRetentionSummary,
} from '@/api/services/retentionBonus'

type Tab = 'info' | 'groups' | 'rating' | 'salary' | 'bonus' | 'performance' | 'fikrlar' | 'ai'

const weekdayShort = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']

function RetentionBar({ value }: { value: number }) {
  const pct = Math.min(100, Math.max(0, value))
  const color = pct >= 80 ? 'bg-emerald-500' : pct >= 50 ? 'bg-amber-400' : 'bg-red-500'
  return (
    <div className="flex items-center gap-2">
      <div className="h-1.5 w-20 overflow-hidden rounded-full bg-slate-100">
        <div className={cn('h-full rounded-full', color)} style={{ width: `${pct}%` }} />
      </div>
      <span className="w-10 font-mono text-xs text-slate-700">{value}%</span>
    </div>
  )
}

function scoreColor(score: number) {
  if (score >= 80) return 'text-emerald-700 bg-emerald-50'
  if (score >= 50) return 'text-amber-700 bg-amber-50'
  return 'text-red-700 bg-red-50'
}

function scoreDot(score: number) {
  if (score >= 80) return 'bg-emerald-500'
  if (score >= 50) return 'bg-amber-400'
  return 'bg-red-500'
}

export function TeacherDetailPage() {
  const { id } = useParams<{ id: string }>()

  const [teacher, setTeacher] = useState<Teacher | null>(null)
  const [groups, setGroups] = useState<Group[]>([])
  /** TUGATILGAN (arxivlangan) guruhlar — shu o'qituvchining; alohida bo'limda ko'rinadi va ochiladi. */
  const [archivedGroups, setArchivedGroups] = useState<Group[]>([])
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [loading, setLoading] = useState(true)
  const [tab, setTab] = useState<Tab>('info')

  // «Fikrlar» tabi — o'quvchilardan yig'ilgan, o'qituvchi haqidagi ichki baholash. FAQAT
  // admin/superadmin (server ham shu rolda cheklaydi); xodimga (staff) ko'rsatilmaydi.
  const { user } = useAuth()
  // O'zgarishlar tarixi — alohida `audit` ruxsati (admin/superadmin uchun har doim true).
  const { can } = usePerm()
  const canSeeAudit = can('audit', 'view')
  // Rasm — o'quvchi sahifasidagi bilan AYNAN bir xil oqim: avatarni bosish → kamera/fayl.
  const canEditPhoto = can('teachers.list', 'edit')
  const [photoOpen, setPhotoOpen] = useState(false)
  const canSeeReviews = user?.role === 'admin' || user?.role === 'superadmin'

  // Performance
  const [perf, setPerf] = useState<TeacherPerformance | null>(null)
  const [perfLoading, setPerfLoading] = useState(false)

  // Reyting
  const [rating, setRating] = useState<TeacherRating | null>(null)
  const [ratingLoading, setRatingLoading] = useState(false)
  const [ratingError, setRatingError] = useState<string | null>(null)
  // Reyting OY kesimi: '' = Umumiy (barcha vaqt) — STANDART, ya'ni tugmaga tegmagan
  // foydalanuvchi uchun hech narsa o'zgarmaydi. Oy tanlansa "yyyy-MM".
  const [ratingMonth, setRatingMonth] = useState('')
  // Mavjud oylar serverdan keladi (kelajakdagi oy ro'yxatda YO'Q). Alohida state'da saqlanadi —
  // oy almashganda `rating` vaqtincha null bo'ladi, navigatsiya esa ekrandan yo'qolmasin.
  const [ratingMonths, setRatingMonths] = useState<string[]>([])
  // Qaysi (o'qituvchi, oy) yuklanganini eslab qolamiz — takroriy so'rov bo'lmasin.
  const ratingLoadedRef = useRef<string | null>(null)

  // Joriy oy kaliti ("YYYY-MM") — stat karta va oy tanlagichi uchun bir manba.
  const currentMonthKey = useMemo(() => {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
  }, [])
  // O'tgan oy — "guruh yopildi, oylik hisoblanmadi" holatida eng ko'p qaraladigan oy.
  const prevMonthKey = useMemo(() => {
    const d = new Date()
    d.setDate(1)
    d.setMonth(d.getMonth() - 1)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
  }, [])

  // Maosh bo'limi ARXIV guruhlarni ham qamrab oladi — yopilgan guruh uchun o'tgan oylarda
  // hisoblangan maosh ko'rinib tursin va sozlamasini tuzatish mumkin bo'lsin.
  const salaryGroups = useMemo(() => [...groups, ...archivedGroups], [groups, archivedGroups])

  // Salary
  const [salaryLoading, setSalaryLoading] = useState(false)
  const [salaryLedger, setSalaryLedger] = useState<SalaryLedger | null>(null)
  // Per-guruh editor mount kaliti — saqlangach yangi qiymatlar bilan qayta tiklash uchun.
  const [salaryVersion, setSalaryVersion] = useState(0)

  // Tanlangan oy tafsiloti + to'lov qilish formasi
  const [selMonth, setSelMonth] = useState(currentMonthKey)
  const [selMonthData, setSelMonthData] = useState<MonthSalary | null>(null)
  const [selMonthLoading, setSelMonthLoading] = useState(false)
  const [dedOpen, setDedOpen] = useState(false)
  const [payOpen, setPayOpen] = useState(false)
  const [payAmount, setPayAmount] = useState(0)
  // "Qaysi oy uchun" — standart tanlangan oy, lekin formada O'ZGARTIRISH mumkin
  // (masalan, iyul maoshi 5-avgustda berilsa: oy = iyul, sana = 5-avgust).
  const [payMonth, setPayMonth] = useState(currentMonthKey)
  const [payInfo, setPayInfo] = useState<MonthSalary | null>(null)
  const [payInfoLoading, setPayInfoLoading] = useState(false)
  const [payDate, setPayDate] = useState(() => new Date().toISOString().slice(0, 10))
  const [payNote, setPayNote] = useState('')
  const [payBusy, setPayBusy] = useState(false)
  const [payError, setPayError] = useState<string | null>(null)

  // Oxirgi 12 oy (joriy oydan orqaga) — tanlagich uchun, alohida so'rovsiz.
  const monthOptions = useMemo(() => {
    const arr: string[] = []
    let [y, m] = currentMonthKey.split('-').map(Number)
    for (let i = 0; i < 12; i++) {
      arr.push(`${y}-${String(m).padStart(2, '0')}`)
      m -= 1
      if (m === 0) {
        m = 12
        y -= 1
      }
    }
    return arr
  }, [currentMonthKey])

  // Tanlangan oy oxirgi 12 oydan tashqarida bo'lsa (eski oyga maosh berilganda) — ro'yxatga qo'shamiz
  const monthChoices = useMemo(
    () => (monthOptions.includes(selMonth) ? monthOptions : [selMonth, ...monthOptions]),
    [monthOptions, selMonth],
  )

  // Tanlangan oyning TUSHUM ko'rsatkichlari (o'qituvchi guruhlari bo'yicha).
  // Manfiy farq = ortiqcha to'langan (avans), shuning uchun `Math.max` bilan kesilmaydi.
  const tuitionDebt =
    (selMonthData?.tuitionCharged ?? 0) - (selMonthData?.tuitionCollected ?? 0)
  const collectedPct =
    (selMonthData?.tuitionCharged ?? 0) > 0
      ? Math.round(
          ((selMonthData?.tuitionCollected ?? 0) / (selMonthData?.tuitionCharged ?? 1)) * 100,
        )
      : 0

  // JAMI MAOSH HISOB-KITOBI — sahifa tepasidagi asosiy javob: "beriladigan · berildi · qoldi".
  // Yagona "jami hisoblangan" raqami savolga javob BERMAYDI: u har oy yig'ilib boraveradi va
  // qarz bor-yo'qligi ko'rinmaydi. Uchala raqam faqat yonma-yon turganda ma'noga ega.
  // Manba — `SalaryLedger` (server): `remaining = totalExpected − totalPaid`, ya'ni bu yerda
  // hech narsa qayta hisoblanmaydi (Moliya modalidagi raqamlar bilan bir xil bo'lsin).
  const salaryTotals = useMemo(() => {
    const expected = salaryLedger?.totalExpected ?? 0
    const paid = salaryLedger?.totalPaid ?? 0
    const remaining = salaryLedger?.remaining ?? 0
    // Manfiy qoldiq = ORTIQCHA berilgan (avans) — foizni 100 dan oshirmaymiz.
    const pct = expected > 0 ? Math.min(100, Math.max(0, Math.round((paid / expected) * 100))) : 0
    // Qarzli oylar — "qoldiq qaysi oylardan yig'ilgan" savoliga qisqa javob.
    // 0.5 so'm — yaxlitlash chidami (foizli maoshda tiyinlar chiqadi).
    const unpaidMonths = (salaryLedger?.months ?? []).filter((m) => m.remaining > 0.5).length
    return { expected, paid, remaining, pct, unpaidMonths }
  }, [salaryLedger])

  // Davr yorlig'i — "bu raqamlar QAYSI oylar bo'yicha" (hisob o'qituvchining maosh boshlanish
  // oyidan boshlanadi, ya'ni davr har o'qituvchida turlicha).
  const salaryPeriodLabel = useMemo(() => {
    const ms = salaryLedger?.months ?? []
    if (ms.length === 0) return ''
    const first = ms[0].month
    const last = ms[ms.length - 1].month
    const range = first === last ? formatMonth(first) : `${formatMonth(first)} — ${formatMonth(last)}`
    return `${range} · ${ms.length} oy`
  }, [salaryLedger])

  const loadSelMonth = (month: string) => {
    if (!id) return
    setSelMonthLoading(true)
    setDedOpen(false)
    getSalaryMonth(id, month)
      .then(setSelMonthData)
      .catch(() => setSelMonthData(null))
      .finally(() => setSelMonthLoading(false))
  }

  const [payType, setPayType] = useState<'all' | 'main' | 'substitute'>('all')

  /** Maosh izohi — tanlangan OY va TURI bo'yicha. */
  const salaryNoteFor = (month: string, type: 'all' | 'main' | 'substitute' = 'all') => {
    if (!teacher) return ''
    const typeLabel = type === 'substitute' ? "O'rinbosarlik haqi" : type === 'main' ? 'Asosiy maosh' : 'Umumiy maosh'
    return `Oylik maosh (${typeLabel}) — ${teacher.fullName} (${formatMonth(month)})`
  }

  const openPayForm = () => {
    setPayMonth(selMonth)
    setPayInfo(selMonthData)
    setPayType('all')
    setPayAmount(Math.max(0, selMonthData?.remaining ?? 0))
    setPayDate(new Date().toISOString().slice(0, 10))
    setPayNote(salaryNoteFor(selMonth, 'all'))
    setPayError(null)
    setPayOpen(true)
  }

  const handlePayTypeChange = (type: 'all' | 'main' | 'substitute') => {
    setPayType(type)
    setPayNote(salaryNoteFor(payMonth, type))
    if (!payInfo) return
    const subFee = payInfo.substituteFee ?? 0
    if (type === 'substitute') {
      // Qoldiq bilan CHEKLANADI — oylik to'liq to'langan bo'lsa o'rinbosarlik haqi ustiga
      // qo'shilib, ortiqcha to'lov bo'lib ketardi (ledger buni keyin ajrata olmaydi).
      setPayAmount(Math.min(subFee, Math.max(0, payInfo.remaining)))
    } else if (type === 'main') {
      setPayAmount(Math.max(0, payInfo.remaining - subFee))
    } else {
      setPayAmount(Math.max(0, payInfo.remaining))
    }
  }

  /** Formada oy o'zgarganda — o'sha oyning qoldig'i va izohi ham yangilanadi. */
  const changePayMonth = (month: string) => {
    setPayMonth(month)
    setPayNote(salaryNoteFor(month, payType))
    if (!id || !month) return
    setPayInfoLoading(true)
    getSalaryMonth(id, month)
      .then((m) => {
        setPayInfo(m)
        const subFee = m?.substituteFee ?? 0
        // ⚠️ Yangi oyda o'rinbosarlik haqi bo'lmasa TURNI ham qaytaramiz: "O'rinbosarlik haqi"
        // varianti faqat `substituteFee > 0` bo'lganda render bo'ladi, ya'ni tanlov o'sha holicha
        // qolsa select BO'SH ko'rinar, summa esa jimgina 0 ga tushardi (sababi yozilmagan).
        const type = subFee > 0 ? payType : 'all'
        if (type !== payType) {
          setPayType('all')
          setPayNote(salaryNoteFor(month, 'all'))
        }
        if (type === 'substitute') {
          // Qoldiq bilan cheklaymiz — oylik allaqachon to'langan bo'lsa ortiqcha berilmasin.
          setPayAmount(Math.min(subFee, Math.max(0, m?.remaining ?? 0)))
        } else if (type === 'main') {
          setPayAmount(Math.max(0, (m?.remaining ?? 0) - subFee))
        } else {
          setPayAmount(Math.max(0, m?.remaining ?? 0))
        }
      })
      .catch(() => {
        setPayInfo(null)
        setPayAmount(0)
      })
      .finally(() => setPayInfoLoading(false))
  }

  const submitPay = async () => {
    if (!id || !payMonth) return
    setPayBusy(true)
    setPayError(null)
    try {
      await createTransaction({
        date: payDate,
        direction: 'expense',
        category: 'salary',
        amount: payAmount,
        teacherId: id,
        // QAYSI OY UCHUN — formada tanlangan oy (sanadan mustaqil)
        month: payMonth,
        note: payNote.trim() || undefined,
      })
      // Natija ko'rinsin: yuqoridagi panel to'lov qilingan OYga o'tadi
      const m = await getSalaryMonth(id, payMonth)
      setSelMonth(payMonth)
      setSelMonthData(m)
      reloadSalary()
      setPayOpen(false)
    } catch (err) {
      setPayError(apiErrorMessage(err, "To'lov qilib bo'lmadi"))
    } finally {
      setPayBusy(false)
    }
  }

  const reloadSalary = () => {
    if (!id) return
    setSalaryLoading(true)
    Promise.all([getSalaryLedger(id), getClasses(true)])
      .then(([ledger, classes]) => {
        setSalaryLedger(ledger)
        const mine = classes.filter((c) => c.teacherId === id)
        setGroups(mine.filter((c) => !c.isArchived))
        setArchivedGroups(mine.filter((c) => c.isArchived))
        setSalaryVersion((v) => v + 1)
      })
      .finally(() => setSalaryLoading(false))
  }

  // Ushlab turish bonuslari — tab ochilganda bir marta yuklanadi.
  const [bonuses, setBonuses] = useState<TeacherRetentionSummary | null>(null)
  const [bonusLoading, setBonusLoading] = useState(false)

  // Credentials
  const [credentials, setCredentials] = useState<Credentials | null>(null)

  useEffect(() => {
    if (!id) return
    Promise.all([getTeachers(), getClasses(true), getSubjects()])
      .then(([teachers, classes, subs]) => {
        const t = teachers.find((x) => x.id === id) ?? null
        setTeacher(t)
        // Arxivlangan (tugatilgan) guruhlar ham keladi — faol va tugatilgan alohida ko'rsatiladi.
        const mine = classes.filter((c) => c.teacherId === id)
        setGroups(mine.filter((c) => !c.isArchived))
        setArchivedGroups(mine.filter((c) => c.isArchived))
        setSubjects(subs)
      })
      .finally(() => setLoading(false))
  }, [id])

  useEffect(() => {
    if (!id || !teacher) return
    getTeacherCredentials(id)
      .then(setCredentials)
      .catch(() => setCredentials(null))
  }, [id, teacher])

  useEffect(() => {
    if (tab !== 'performance' || !id || perf) return
    setPerfLoading(true)
    getTeacherPerformanceSingle(id)
      .then(setPerf)
      .finally(() => setPerfLoading(false))
  }, [tab, id, perf])

  useEffect(() => {
    if (tab !== 'bonus' || !id || bonuses) return
    setBonusLoading(true)
    getTeacherRetentionBonuses(id)
      .then(setBonuses)
      .catch(() => setBonuses({ total: 0, count: 0, items: [] }))
      .finally(() => setBonusLoading(false))
  }, [tab, id, bonuses])

  useEffect(() => {
    if (tab !== 'rating' || !id) return
    const key = `${id}|${ratingMonth}`
    if (ratingLoadedRef.current === key) return
    ratingLoadedRef.current = key
    setRatingLoading(true)
    setRating(null)
    setRatingError(null)
    getTeacherRating(id, ratingMonth || undefined)
      .then((r) => {
        setRating(r)
        if (r.months?.length) setRatingMonths(r.months)
      })
      .catch((e) => {
        // Xato bo'lsa kalitni tozalaymiz — oyni qayta tanlaganda so'rov YANA yuboriladi.
        ratingLoadedRef.current = null
        setRatingError(apiErrorMessage(e, "Reytingni yuklab bo'lmadi"))
      })
      .finally(() => setRatingLoading(false))
  }, [tab, id, ratingMonth])

  useEffect(() => {
    if (tab !== 'salary' || !id || salaryLedger) return
    reloadSalary()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, id, salaryLedger])

  useEffect(() => {
    if (tab !== 'salary' || !id) return
    loadSelMonth(selMonth)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, id, selMonth])

  // --- Reyting: oy navigatsiyasi (sof hisob, hook emas) ---
  // Ro'yxat serverdan eng eskisidan JORIY oygacha keladi, ya'ni "keyingi" tugmasi joriy oyda
  // to'xtaydi — kelajakdagi bo'sh oyga o'tib bo'lmaydi.
  const ratingIdx = ratingMonth ? ratingMonths.indexOf(ratingMonth) : -1
  const canRatingPrev = ratingMonths.length > 0 && (ratingMonth === '' || ratingIdx > 0)
  const canRatingNext = ratingMonth !== '' && ratingIdx >= 0 && ratingIdx < ratingMonths.length - 1
  /** delta: -1 = oldingi oy, +1 = keyingi oy. «Umumiy»dan orqaga bosilsa — ENG YANGI (joriy) oy. */
  const gotoRatingMonth = (delta: number) => {
    if (ratingMonths.length === 0) return
    if (ratingMonth === '') {
      if (delta < 0) setRatingMonth(ratingMonths[ratingMonths.length - 1])
      return
    }
    const i = ratingIdx + delta
    if (i >= 0 && i < ratingMonths.length) setRatingMonth(ratingMonths[i])
  }

  if (loading)
    return (
      <div className="flex min-h-[300px] items-center justify-center">
        <Loader label="Yuklanmoqda..." />
      </div>
    )

  if (!teacher)
    return (
      <Card>
        <div className="state">
          <h4>O'qituvchi topilmadi</h4>
          <p>
            <Link to="/admin/teachers" className="text-brand-600 hover:underline">
              Ro'yxatga qaytish
            </Link>
          </p>
        </div>
      </Card>
    )

  const subjectNames = teacher.subjectIds
    .map((sid) => subjects.find((s) => s.id === sid)?.name)
    .filter(Boolean)
    .join(', ')

  return (
    <div>
      <PageHeader
        title={teacher.fullName}
        sub={
          <span className="inline-flex flex-wrap items-center gap-2">
            <Link
              to="/admin/teachers"
              className="inline-flex items-center gap-1 text-xs text-slate-400 hover:text-brand-600"
            >
              <ArrowLeft className="h-3.5 w-3.5" />
              O'qituvchilar ro'yxati
            </Link>
            {/* VAQTINCHA AKTIV EMAS — tizimga kira olmaydi. Arxiv EMAS: paroli va tarixi joyida.
                Qaytarish — "O'qituvchilar" ro'yxatidagi qulf tugmasi. */}
            {teacher.isBlocked && (
              <span
                className="rounded bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700"
                title={teacher.blockNote || undefined}
              >
                Vaqtincha aktiv emas — tizimga kira olmaydi
              </span>
            )}
          </span>
        }
      />

      {/* Tabs */}
      <div className="tabs mb-4" role="tablist">
        <button
          type="button"
          className={cn('tab', tab === 'info' && 'active')}
          onClick={() => setTab('info')}
        >
          <Info className="mr-1 inline h-3.5 w-3.5" />
          Ma'lumot
        </button>
        <button
          type="button"
          className={cn('tab', tab === 'groups' && 'active')}
          onClick={() => setTab('groups')}
        >
          <Users className="mr-1 inline h-3.5 w-3.5" />
          Guruhlar ({groups.length}
          {archivedGroups.length > 0 ? ` + ${archivedGroups.length} arxiv` : ''})
        </button>
        <button
          type="button"
          className={cn('tab', tab === 'rating' && 'active')}
          onClick={() => setTab('rating')}
        >
          <Trophy className="mr-1 inline h-3.5 w-3.5" />
          Reyting
        </button>
        <button
          type="button"
          className={cn('tab', tab === 'salary' && 'active')}
          onClick={() => setTab('salary')}
        >
          <Wallet className="mr-1 inline h-3.5 w-3.5" />
          Maosh
        </button>
        <button
          type="button"
          className={cn('tab', tab === 'bonus' && 'active')}
          onClick={() => setTab('bonus')}
        >
          <Award className="mr-1 inline h-3.5 w-3.5" />
          Bonus
        </button>
        <button
          type="button"
          className={cn('tab', tab === 'performance' && 'active')}
          onClick={() => setTab('performance')}
        >
          <TrendingUp className="mr-1 inline h-3.5 w-3.5" />
          Performance
        </button>
        {canSeeReviews && (
          <button
            type="button"
            className={cn('tab', tab === 'fikrlar' && 'active')}
            onClick={() => setTab('fikrlar')}
          >
            <MessageSquareQuote className="mr-1 inline h-3.5 w-3.5" />
            Fikrlar
          </button>
        )}
        <button
          type="button"
          className={cn('tab', tab === 'ai' && 'active')}
          onClick={() => setTab('ai')}
        >
          <Sparkles className="mr-1 inline h-3.5 w-3.5" />
          AI tahlil
        </button>
      </div>

      {/* INFO TAB */}
      {tab === 'info' && (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Card title="Shaxsiy ma'lumotlar">
            <div className="mb-4 flex items-center gap-3">
              {/* DUMALOQ avatar — bosilganda rasm oynasi ochiladi (rasm yo'q bo'lsa darhol
                  kamera). O'quvchi sahifasidagi xatti-harakat bilan bir xil. */}
              <button
                type="button"
                onClick={() => canEditPhoto && setPhotoOpen(true)}
                disabled={!canEditPhoto}
                title={canEditPhoto ? (teacher.photoUrl ? 'Rasmni almashtirish' : "Rasm qo'shish") : undefined}
                className={cn(
                  'group relative flex h-14 w-14 shrink-0 items-center justify-center overflow-hidden rounded-full bg-brand-50 text-lg font-semibold text-brand-600',
                  canEditPhoto && 'cursor-pointer ring-offset-2 transition hover:ring-2 hover:ring-brand-300',
                )}
              >
                {teacher.photoUrl ? (
                  <img src={teacher.photoUrl} alt="" className="h-full w-full object-cover" />
                ) : (
                  teacher.fullName
                    .split(' ')
                    .filter(Boolean)
                    .slice(0, 2)
                    .map((s) => s[0]?.toUpperCase())
                    .join('')
                )}
                {canEditPhoto && (
                  <span className="absolute inset-0 flex items-center justify-center bg-slate-900/45 text-white opacity-0 transition-opacity group-hover:opacity-100">
                    <Camera className="h-5 w-5" />
                  </span>
                )}
              </button>
              <div>
                <div className="text-base font-semibold text-slate-800">{teacher.fullName}</div>
                <div className="text-xs text-slate-400">{genderLabels[teacher.gender]}</div>
              </div>
              <Badge tone={teacher.salaryMode === 'percent' ? 'blue' : 'violet'} dot className="ml-auto">
                {teacher.salaryMode === 'percent' ? 'Foiz' : "Qat'iy"}
              </Badge>
            </div>
            <InfoRow label="Tug'ilgan kun" value={teacher.birthDate ? formatDate(teacher.birthDate) : '—'} />
            <InfoRow label="Manzil" value={teacher.address || '—'} />
            <InfoRow label="Telefon" value={teacher.phone || '—'} mono />
            <InfoRow
              label="Fanlar"
              value={subjectNames || '—'}
            />
            <InfoRow
              label="Umumiy maosh sozlamasi"
              mono
              value={
                teacher.salaryMode === 'percent'
                  ? `Foiz — guruh to'lovining ${teacher.salaryPercent ?? 0}%i`
                  : `Qat'iy summa — ${formatMoney(teacher.salary)}`
              }
            />
            <InfoRow
              label="Maosh hisoblanadi"
              value={
                teacher.salaryStartDate
                  ? `${formatDate(teacher.salaryStartDate)} dan`
                  : teacher.salaryStartMonth
                    ? `${formatMonth(teacher.salaryStartMonth)} dan`
                    : "eng birinchi to'lov oyidan"
              }
            />
          </Card>

          <Card title="Tizim akkaunti">
            <CredentialsBox
              credentials={credentials}
              onReset={async () => {
                const c = await resetTeacherPassword(teacher.id)
                setCredentials(c)
              }}
            />
          </Card>
        </div>
      )}

      {/* GROUPS TAB */}
      {tab === 'groups' && (
        <div className="space-y-4">
          {groups.length === 0 ? (
            <Card>
              <div className="state">
                <h4>Faol guruh biriktirilmagan</h4>
                <p>
                  {archivedGroups.length > 0
                    ? `Bu o'qituvchida ${archivedGroups.length} ta tugatilgan guruh bor — pastda ko'rishingiz mumkin.`
                    : "Bu o'qituvchiga hech qanday faol guruh biriktirilmagan."}
                </p>
              </div>
            </Card>
          ) : (
            <Card tight>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Guruh</th>
                      <th className="px-4 py-3">Kurs</th>
                      <th className="px-4 py-3">Xona</th>
                      <th className="px-4 py-3">Kunlar</th>
                      <th className="px-4 py-3">Vaqt</th>
                      <th className="px-4 py-3">Oylik</th>
                      <th className="px-4 py-3 text-right">Amallar</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {groups.map((g) => (
                      <tr key={g.id} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">
                          <Link
                            to={`/admin/classes/${g.id}`}
                            className="text-inherit hover:text-brand-600 hover:underline"
                          >
                            {g.name}
                          </Link>
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {subjects.find((s) => s.id === g.courseId)?.name ?? '—'}
                        </td>
                        <td className="px-4 py-3 text-slate-600">{g.room || '—'}</td>
                        <td className="px-4 py-3">
                          {g.days && g.days.length > 0 ? (
                            <div className="flex flex-wrap gap-1">
                              {g.days.map((d) => (
                                <span
                                  key={d}
                                  className="rounded bg-brand-50 px-1.5 py-0.5 text-xs font-medium text-brand-700"
                                >
                                  {weekdayShort[d] ?? d}
                                </span>
                              ))}
                            </div>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="px-4 py-3 font-mono text-xs text-slate-600">
                          {g.startTime && g.endTime ? `${g.startTime}–${g.endTime}` : '—'}
                        </td>
                        <td className="px-4 py-3 font-mono text-slate-700">
                          {g.monthlyFee ? formatMoney(g.monthlyFee) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <Link
                            to={`/admin/classes/${g.id}`}
                            className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
                          >
                            Ko'rish
                            <ArrowUpRight className="h-3 w-3" />
                          </Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}

          {/* TUGATILGAN (arxivlangan) guruhlar — ma'lumotlari saqlanadi, ochib ko'rish mumkin. */}
          {archivedGroups.length > 0 && (
            <Card tight>
              <div className="flex items-center gap-2 px-4 pt-4 pb-2">
                <Archive className="h-4 w-4 text-slate-400" />
                <h3 className="font-semibold text-slate-700">Tugatilgan guruhlar</h3>
                <span className="text-sm text-slate-400">{archivedGroups.length}</span>
              </div>
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Guruh</th>
                      <th className="px-4 py-3">Kurs</th>
                      <th className="px-4 py-3">Kunlar</th>
                      <th className="px-4 py-3">Vaqt</th>
                      <th className="px-4 py-3">Tugatilgan</th>
                      <th className="px-4 py-3 text-right">Amallar</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {archivedGroups.map((g) => (
                      <tr key={g.id} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-700">
                          <Link
                            to={`/admin/classes/${g.id}`}
                            className="text-inherit hover:text-brand-600 hover:underline"
                          >
                            {g.name}
                          </Link>
                          <span className="ml-2 rounded bg-amber-100 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                            arxiv
                          </span>
                        </td>
                        <td className="px-4 py-3 text-slate-600">
                          {subjects.find((s) => s.id === g.courseId)?.name ?? '—'}
                        </td>
                        <td className="px-4 py-3">
                          {g.days && g.days.length > 0 ? (
                            <div className="flex flex-wrap gap-1">
                              {g.days.map((d) => (
                                <span
                                  key={d}
                                  className="rounded bg-slate-100 px-1.5 py-0.5 text-xs font-medium text-slate-600"
                                >
                                  {weekdayShort[d] ?? d}
                                </span>
                              ))}
                            </div>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="px-4 py-3 font-mono text-xs text-slate-600">
                          {g.startTime && g.endTime ? `${g.startTime}–${g.endTime}` : '—'}
                        </td>
                        <td className="px-4 py-3 text-slate-500">
                          {g.archivedAt ? formatDate(g.archivedAt) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <Link
                            to={`/admin/classes/${g.id}`}
                            className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-slate-50 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
                          >
                            Ko'rish
                            <ArrowUpRight className="h-3 w-3" />
                          </Link>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
        </div>
      )}

      {/* RATING TAB */}
      {tab === 'rating' && (
        <div className="space-y-4">
          {/* Oy navigatsiyasi — «Umumiy» (standart, barcha vaqt) yoki bitta oy kesimi.
              Yuklanish paytida ham ekranda qoladi (oylar alohida state'da). */}
          <Card tight>
            <div className="flex flex-wrap items-center justify-center gap-2 px-4 py-3">
              <button
                type="button"
                onClick={() => setRatingMonth('')}
                className={cn(
                  'rounded-lg px-3 py-1.5 text-sm font-medium transition',
                  ratingMonth === ''
                    ? 'bg-brand-600 text-white shadow-sm'
                    : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                )}
              >
                Umumiy
              </button>
              <button
                type="button"
                onClick={() => gotoRatingMonth(-1)}
                disabled={!canRatingPrev}
                title="Oldingi oy"
                className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <span className="min-w-[120px] text-center text-sm font-semibold text-slate-700">
                {ratingMonth ? formatMonth(ratingMonth) : 'Umumiy'}
              </span>
              <button
                type="button"
                onClick={() => gotoRatingMonth(1)}
                disabled={!canRatingNext}
                title="Keyingi oy"
                className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
            <p className="px-4 pb-3 text-center text-xs text-slate-400">
              {ratingMonth
                ? `Faqat ${formatMonth(ratingMonth)} oyidagi baho, mezon va davomat hisoblanadi`
                : "Barcha vaqt bo'yicha — oy kesimi uchun strelkalardan foydalaning"}
            </p>
          </Card>

          {ratingLoading || !rating ? (
            ratingError ? (
              <Card>
                <div className="state">
                  <h4>Xatolik</h4>
                  <p>{ratingError}</p>
                </div>
              </Card>
            ) : (
              <Card>
                <Loader label="Yuklanmoqda..." />
              </Card>
            )
          ) : rating.rows.length === 0 ? (
            <Card>
              <div className="state">
                <h4>Reyting uchun ma'lumot yo'q</h4>
                <p>
                  {ratingMonth
                    ? `${formatMonth(ratingMonth)} oyida bu o'qituvchi guruhlarida baho/mezon kiritilmagan.`
                    : "Bu o'qituvchi guruhlarida hali baho/mezon kiritilmagan."}
                </p>
              </div>
            </Card>
          ) : (
            <>
              {/* KPI cards */}
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                <StatCard label="Guruhlar" value={rating.groupsCount} icon={Users} />
                <StatCard
                  label="O'quvchilar"
                  value={rating.studentsCount}
                  icon={Users}
                  iconBg="bg-sky-50"
                  iconColor="text-sky-600"
                />
                <StatCard
                  label="O'rtacha ball"
                  value={rating.averageBall}
                  icon={Trophy}
                  iconBg="bg-amber-50"
                  iconColor="text-amber-600"
                />
              </div>

              {/* TOP-3 podium */}
              {rating.rows.length > 0 && (
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  {rating.rows.slice(0, 3).map((row) => (
                    <PodiumCard key={row.studentId} row={row} />
                  ))}
                </div>
              )}

              {/* To'liq jadval */}
              <Card tight title="To'liq reyting">
                <p className="px-4 pt-3 text-xs text-slate-400">
                  Ball = jurnal baholari yig'indisi + bajarilgan baholash mezonlari
                </p>
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>O'rin</th>
                        <th>O'quvchi</th>
                        <th>Guruh</th>
                        <th className="num">Jurnal</th>
                        <th className="num">Mezon</th>
                        <th className="num">Ball</th>
                        <th className="num">O'rtacha</th>
                        <th className="num">Davomat</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rating.rows.map((row) => (
                        <tr key={row.studentId}>
                          <td>
                            <RankBadge rank={row.rank} />
                          </td>
                          <td>
                            <Link
                              to={`/admin/students/${row.studentId}`}
                              className="font-medium text-slate-800 hover:text-brand-600 hover:underline"
                            >
                              {row.fullName}
                            </Link>
                          </td>
                          <td>
                            <div className="flex flex-wrap gap-1">
                              {row.groups
                                .split(',')
                                .map((g) => g.trim())
                                .filter(Boolean)
                                .map((g, i) => (
                                  <Badge key={i} tone="violet">
                                    {g}
                                  </Badge>
                                ))}
                            </div>
                          </td>
                          <td className="num">{row.journalTotal}</td>
                          <td className="num">{row.criteriaDone}</td>
                          <td className="num">
                            <div className="font-mono text-sm font-bold text-slate-800">{row.ball}</div>
                            <div className="mt-1 h-1 w-16 overflow-hidden rounded-full bg-slate-100">
                              <div
                                className="h-full rounded-full bg-brand-500"
                                style={{
                                  width: `${
                                    rating.rows[0].ball > 0
                                      ? Math.max(4, Math.round((row.ball / rating.rows[0].ball) * 100))
                                      : 0
                                  }%`,
                                }}
                              />
                            </div>
                          </td>
                          <td className="num">{row.average}</td>
                          <td className="num">
                            <span
                              className={cn(
                                'font-mono text-sm font-medium',
                                row.attendance == null
                                  ? 'text-slate-400'
                                  : row.attendance >= 90
                                    ? 'text-emerald-600'
                                    : row.attendance >= 75
                                      ? 'text-amber-600'
                                      : 'text-red-600',
                              )}
                            >
                              {row.attendance == null ? '—' : `${row.attendance}%`}
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </Card>
            </>
          )}
        </div>
      )}

      {/* SALARY TAB */}
      {tab === 'salary' && (
        <div className="space-y-4">
          {/* ══ JAMI HISOB-KITOB — sahifaning eng tepasidagi javob:
                "o'qituvchiga jami qancha beriladi · qanchasi berildi · qanchasi qoldi".
                Ilgari bu yerda faqat "Jami hisoblangan" va "Jami berildi" turardi — raqam har oy
                yig'ilib borardi, QOLDIQ esa umuman yo'q edi: ya'ni "qarzimiz bormi va qancha"
                degan asosiy savolga javob berish uchun oylar jadvalini qo'lda qo'shib chiqish
                kerak edi. Qolgan ma'lumotlar (oy kesimi, tushum, tarix) shu kartadan PASTDA. */}
          <Card>
            {salaryLoading && !salaryLedger ? (
              <Loader label="Yuklanmoqda..." />
            ) : (
              <>
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="text-sm font-semibold text-slate-700">Jami hisob-kitob</p>
                  {salaryPeriodLabel && (
                    <span className="text-xs text-slate-400">{salaryPeriodLabel}</span>
                  )}
                </div>

                <div className="mt-3 grid gap-3 sm:grid-cols-3">
                  <TotalTile
                    label="O'qituvchiga jami beriladigan"
                    value={formatMoney(salaryTotals.expected)}
                    /* Jurnalga bog'langan maoshda "beriladigan" ALLAQACHON ushlanma ayrilgan
                       summa — buni yozib qo'ymasak, raqam hisobdan kam ko'rinardi. */
                    hint={
                      salaryLedger?.journalLinked && (salaryLedger?.totalDeduction ?? 0) > 0
                        ? `jurnal ushlanmasi ayrilgan: −${formatMoney(salaryLedger?.totalDeduction ?? 0)}`
                        : undefined
                    }
                  />
                  <TotalTile
                    label="Berildi (tushdi)"
                    value={formatMoney(salaryTotals.paid)}
                    valueClass="text-emerald-600"
                    hint={
                      salaryLedger && salaryLedger.payments.length > 0
                        ? `${salaryLedger.payments.length} ta to'lov`
                        : "hali to'lov yo'q"
                    }
                  />
                  <TotalTile
                    /* Manfiy qoldiq = ortiqcha berilgan (avans) — "+" bilan va yashil. */
                    label={salaryTotals.remaining < 0 ? 'Ortiqcha berilgan' : 'Qoldi'}
                    value={
                      salaryTotals.remaining < 0
                        ? `+${formatMoney(-salaryTotals.remaining)}`
                        : formatMoney(salaryTotals.remaining)
                    }
                    valueClass={
                      salaryTotals.remaining > 0
                        ? 'text-red-600'
                        : salaryTotals.remaining < 0
                          ? 'text-emerald-600'
                          : 'text-slate-600'
                    }
                    hint={
                      salaryTotals.remaining > 0
                        ? `${salaryTotals.unpaidMonths} oy to'liq berilmagan`
                        : salaryTotals.remaining < 0
                          ? 'keyingi oylardan ayriladi'
                          : "qarz yo'q"
                    }
                    hintClass={salaryTotals.remaining > 0 ? 'text-red-500' : 'text-slate-400'}
                  />
                </div>

                {salaryTotals.expected > 0 && (
                  <div className="mt-3 flex items-center gap-2">
                    <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                      <div
                        className={cn(
                          'h-full rounded-full',
                          salaryTotals.remaining > 0 ? 'bg-amber-500' : 'bg-emerald-500',
                        )}
                        style={{ width: `${salaryTotals.pct}%` }}
                      />
                    </div>
                    <span className="font-mono text-xs text-slate-500">
                      {salaryTotals.pct}% berildi
                    </span>
                  </div>
                )}
              </>
            )}
          </Card>

          {/* Qolgan ko'rsatkichlar — jamlanmadan PASTDA */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            <StatCard label="Guruhlar soni" value={groups.length} icon={Users} />
            <StatCard
              label="Joriy oy hisoblandi"
              value={formatMoney(
                salaryLedger?.months.find((m) => m.month === currentMonthKey)?.expected ?? 0,
              )}
              icon={Wallet}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
            {/* O'TGAN OY — guruh yopilgach eng ko'p qaraladigan raqam: "o'tgan oy uchun qancha
                hisoblangan edi". Guruh arxivlangan bo'lsa ham saqlanadi va ko'rinaveradi. */}
            <StatCard
              label="O'tgan oy hisoblandi"
              value={formatMoney(
                salaryLedger?.months.find((m) => m.month === prevMonthKey)?.expected ?? 0,
              )}
              icon={Wallet}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
            />
          </div>

          {/* OY TANLASH + TANLANGAN OY TAFSILOTI + TO'LOV QILISH */}
          <Card>
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium text-slate-600">Oy:</span>
                <select
                  value={selMonth}
                  onChange={(e) => setSelMonth(e.target.value)}
                  className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm focus:border-brand-400 focus:outline-none"
                >
                  {monthChoices.map((m) => (
                    <option key={m} value={m}>
                      {formatMonth(m)}
                    </option>
                  ))}
                </select>
              </div>
              <button
                type="button"
                onClick={openPayForm}
                className="inline-flex items-center gap-1.5 rounded-lg bg-brand-600 px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-brand-700"
              >
                <Plus className="h-4 w-4" />
                To'lov qilish
              </button>
            </div>

            {selMonthLoading ? (
              <div className="mt-4">
                <Loader label="Yuklanmoqda..." />
              </div>
            ) : !selMonthData ? (
              <p className="mt-4 text-sm text-slate-400">
                Bu oy uchun maosh ma'lumoti yo'q.
              </p>
            ) : (
              <div className="mt-4 space-y-4">
                {/* 1) TUSHUM — o'qituvchining guruhlaridan shu oyda kutilgan va kelgan pul.
                    Maosh rejimidan (foiz/qat'iy) QAT'I NAZAR ko'rsatiladi: admin uchun "shu
                    o'qituvchi shu oyda markazga qancha pul keltirdi" savoli maosh hisobidan
                    alohida. Guruhi yo'q o'qituvchida bo'lim umuman chizilmaydi (hammasi 0). */}
                {salaryGroups.length > 0 && (
                  <div>
                    <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                      Guruhlar tushumi
                    </p>
                    <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
                      <MonthTile
                        label="Bo'lishi kerak"
                        value={formatMoney(selMonthData.tuitionCharged ?? 0)}
                        hint="O'quvchilarga shu oy uchun hisoblangan (chegirma ayrilgan)"
                      />
                      <MonthTile
                        label="Tushum bo'ldi"
                        value={formatMoney(selMonthData.tuitionCollected ?? 0)}
                        valueClass="text-emerald-600"
                        hint="Shu oy uchun to'langan pul (vozvrat ayrilgan)"
                      />
                      <MonthTile
                        label="Yig'ilmagan"
                        value={
                          tuitionDebt < 0
                            ? `+${formatMoney(-tuitionDebt)}`
                            : formatMoney(tuitionDebt)
                        }
                        valueClass={tuitionDebt > 0 ? 'text-red-600' : 'text-emerald-600'}
                        /* Oy boshida hisob hali yozilmagan bo'lishi mumkin (oylik hisoblash fon
                           xizmati har 12 soatda ishlaydi), o'quvchi esa oldindan to'lagan bo'ladi —
                           bu "avans" emas, shuni ochiq yozamiz, aks holda raqam xato ko'rinardi. */
                        hint={
                          (selMonthData.tuitionCharged ?? 0) === 0 &&
                          (selMonthData.tuitionCollected ?? 0) > 0
                            ? "Bu oy uchun hisob hali yozilmagan"
                            : tuitionDebt > 0
                              ? "Shu oy bo'yicha qarz"
                              : tuitionDebt < 0
                                ? "Ortiqcha to'langan (avans)"
                                : "Hammasi yig'ilgan"
                        }
                      />
                    </div>
                    {(selMonthData.tuitionCharged ?? 0) > 0 && (
                      <div className="mt-2 flex items-center gap-2">
                        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                          <div
                            className="h-full rounded-full bg-emerald-500"
                            style={{ width: `${Math.min(100, Math.max(0, collectedPct))}%` }}
                          />
                        </div>
                        <span className="font-mono text-xs text-slate-500">
                          {collectedPct}% yig'ildi
                        </span>
                      </div>
                    )}
                  </div>
                )}

                {/* 2) MAOSH — shu oy uchun hisoblangan, berilgan va qoldiq. */}
                <div>
                  <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                    Maosh
                  </p>
                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                    <MonthTile
                      label="Hisoblandi"
                      value={formatMoney(selMonthData.expected)}
                      /* Foizli maoshda pul hali kelmagan oyda "Hisoblandi" 0 chiqadi — yonida
                         "hammasi to'lansa qancha bo'lishi" turadi (oylar jadvalidagi bilan bir xil). */
                      hint={
                        (selMonthData.potentialExpected ?? 0) > selMonthData.expected
                          ? `hisob bo'yicha ${formatMoney(selMonthData.potentialExpected ?? 0)}`
                          : undefined
                      }
                      hintClass="text-amber-600"
                    />
                    <MonthTile
                      label="Berildi"
                      value={formatMoney(selMonthData.paid)}
                      valueClass="text-emerald-600"
                    />
                    <MonthTile
                      label="Qoldi"
                      value={
                        selMonthData.remaining < 0
                          ? `+${formatMoney(-selMonthData.remaining)}`
                          : formatMoney(selMonthData.remaining)
                      }
                      valueClass={
                        selMonthData.remaining > 0 ? 'text-red-600' : 'text-slate-600'
                      }
                    />
                    <div className="flex flex-col items-center justify-center rounded-xl border border-slate-100 px-3 py-2.5 text-center">
                      <p className="text-xs text-slate-400">Holat</p>
                      <div className="mt-1">
                        <Badge
                          tone={
                            selMonthData.status === 'paid'
                              ? 'green'
                              : selMonthData.status === 'partial'
                                ? 'amber'
                                : 'default'
                          }
                          dot
                        >
                          {selMonthData.status === 'paid'
                            ? "To'liq"
                            : selMonthData.status === 'partial'
                              ? 'Qisman'
                              : "To'lanmadi"}
                        </Badge>
                      </div>
                    </div>
                  </div>
                </div>

                {/* 3) O'rinbosarlik haqi (bo'lsa — bu o'qituvchi o'rinbosar bo'lgan) */}
                {selMonthData && (selMonthData.substituteFee ?? 0) > 0 && (
                  <div className="flex items-center justify-between rounded-lg border border-emerald-200 bg-emerald-50 px-3.5 py-2.5 text-xs text-emerald-800 font-medium">
                    <span className="flex items-center gap-2">
                      <UserCheck className="h-4 w-4 text-emerald-600" />
                      <span>O'rinbosarlik haqi (boshqa o'qituvchilar o'rniga o'tilgan darslar uchun):</span>
                    </span>
                    <span className="font-mono text-sm font-bold text-emerald-700">
                      +{formatMoney(selMonthData.substituteFee ?? 0)}
                    </span>
                  </div>
                )}

                {/* 4) O'rinbosar ushlanmasi (bo'lsa — bu o'qituvchining guruhida o'rinbosar dars o'tgan) */}
                {selMonthData && (selMonthData.substituteDeduction ?? 0) > 0 && (
                  <div className="flex items-center justify-between rounded-lg border border-amber-200 bg-amber-50 px-3.5 py-2.5 text-xs text-amber-900 font-medium">
                    <span className="flex items-center gap-2">
                      <Users className="h-4 w-4 text-amber-600" />
                      <span>O'rinbosar ushlanmasi (guruhlaringizda o'rinbosar dars o'tgani uchun):</span>
                    </span>
                    <span className="font-mono text-sm font-bold text-amber-700">
                      −{formatMoney(selMonthData.substituteDeduction ?? 0)}
                    </span>
                  </div>
                )}
              </div>
            )}

            {/* Jurnal ushlanmasi tafsiloti (bo'lsa) */}
            {selMonthData && (selMonthData.deduction ?? 0) > 0 && (
              <div className="mt-3">
                <button
                  type="button"
                  onClick={() => setDedOpen((v) => !v)}
                  className="flex w-full items-center justify-between rounded-lg bg-amber-50 px-3 py-2.5 text-left text-xs text-amber-800"
                >
                  <span className="flex items-center gap-1.5">
                    <ChevronRight
                      className={cn('h-3.5 w-3.5 transition-transform', dedOpen && 'rotate-90')}
                    />
                    Jurnal ushlanmasi: −{formatMoney(selMonthData.deduction ?? 0)} (
                    {selMonthData.missedLessons ?? 0} dars)
                  </span>
                  <span className="text-amber-600">sababi</span>
                </button>
                {dedOpen && (
                  <div className="mt-2 space-y-2">
                    {(selMonthData.lessons ?? [])
                      .filter((l) => l.missed > 0)
                      .map((l) => (
                        <div
                          key={l.groupId}
                          className="rounded-lg border border-slate-200 bg-white px-3 py-2"
                        >
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="text-sm font-semibold text-slate-700">
                              {l.groupName}
                            </span>
                            <span className="text-xs text-slate-500">
                              {l.conducted}/{l.planned} dars belgilangan ·{' '}
                              <span className="font-mono font-semibold text-red-600">
                                −{formatMoney(l.deduction)}
                              </span>
                            </span>
                          </div>
                          <div className="mt-1.5 flex flex-wrap gap-1">
                            {l.missedDates.map((d) => (
                              <span
                                key={d}
                                className="rounded-md bg-red-50 px-1.5 py-0.5 font-mono text-[11px] text-red-700"
                              >
                                {formatDate(d)}
                              </span>
                            ))}
                          </div>
                        </div>
                      ))}
                  </div>
                )}
              </div>
            )}

            {/* To'lov qilish formasi (inline) */}
            {payOpen && (
              <div className="mt-4 rounded-xl border border-slate-200 bg-slate-50 p-4">
                <p className="mb-3 text-sm font-semibold text-slate-700">
                  {payMonth ? `${formatMonth(payMonth)} uchun maosh berish` : 'Maosh berish'}
                </p>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-4">
                  <label className="block">
                    <span className="text-xs text-slate-500">Qaysi oy uchun</span>
                    <input
                      type="month"
                      value={payMonth}
                      onChange={(e) => changePayMonth(e.target.value)}
                      className="mt-1 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm focus:border-brand-400 focus:outline-none"
                    />
                    <span className="mt-1 block text-[11px] text-slate-400">
                      {payInfoLoading
                        ? 'Yuklanmoqda...'
                        : payInfo
                          ? `Qoldiq: ${
                              payInfo.remaining < 0
                                ? `+${formatMoney(-payInfo.remaining)}`
                                : formatMoney(payInfo.remaining)
                            }`
                          : "Bu oy uchun ma'lumot yo'q"}
                    </span>
                  </label>
                  <label className="block">
                    <span className="text-xs text-slate-500">Maosh turi</span>
                    <select
                      value={payType}
                      onChange={(e) => handlePayTypeChange(e.target.value as 'all' | 'main' | 'substitute')}
                      className="mt-1 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm focus:border-brand-400 focus:outline-none bg-white"
                    >
                      <option value="all">Umumiy maosh</option>
                      <option value="main">Asosiy maosh</option>
                      {(payInfo?.substituteFee ?? 0) > 0 && (
                        <option value="substitute">
                          O'rinbosarlik haqi (+{formatMoney(payInfo?.substituteFee ?? 0)})
                        </option>
                      )}
                    </select>
                  </label>
                  <label className="block">
                    <span className="text-xs text-slate-500">Summa (so'm)</span>
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={payAmount}
                      onChange={(e) => setPayAmount(Number(e.target.value))}
                      className="mt-1 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 font-mono text-sm focus:border-brand-400 focus:outline-none"
                    />
                  </label>
                  <label className="block">
                    <span className="text-xs text-slate-500">Sana (berilgan kun)</span>
                    <input
                      type="date"
                      value={payDate}
                      onChange={(e) => setPayDate(e.target.value)}
                      className="mt-1 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm focus:border-brand-400 focus:outline-none"
                    />
                    <span className="mt-1 block text-[11px] text-slate-400">
                      Pul haqiqatda berilgan kun
                    </span>
                  </label>
                </div>
                <label className="mt-3 block">
                  <span className="text-xs text-slate-500">Izoh</span>
                  <input
                    type="text"
                    value={payNote}
                    onChange={(e) => setPayNote(e.target.value)}
                    className="mt-1 w-full rounded-lg border border-slate-200 px-2.5 py-1.5 text-sm focus:border-brand-400 focus:outline-none"
                  />
                </label>
                {payError && (
                  <div className="mt-3 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
                    {payError}
                  </div>
                )}
                <div className="mt-3 flex items-center justify-end gap-2">
                  <button
                    type="button"
                    onClick={() => setPayOpen(false)}
                    disabled={payBusy}
                    className="rounded-lg border border-slate-200 px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
                  >
                    Bekor qilish
                  </button>
                  <button
                    type="button"
                    onClick={submitPay}
                    disabled={payBusy || payAmount <= 0 || !payMonth}
                    className={cn(
                      'rounded-lg px-4 py-2 text-sm font-semibold text-white transition-colors',
                      payBusy || payAmount <= 0 || !payMonth
                        ? 'cursor-not-allowed bg-slate-300'
                        : 'bg-brand-600 hover:bg-brand-700',
                    )}
                  >
                    {payBusy ? 'Saqlanmoqda...' : 'To\'lash'}
                  </button>
                </div>
              </div>
            )}
          </Card>

          {/* BERILGAN MAOSHLAR TARIXI */}
          <Card title="Berilgan maoshlar">
            {!salaryLedger || salaryLedger.payments.length === 0 ? (
              <p className="text-sm text-slate-400">Bu davrda maosh berilmagan.</p>
            ) : (
              <ul className="space-y-1.5">
                {[...salaryLedger.payments]
                  .sort((a, b) => (a.date < b.date ? 1 : -1))
                  .map((p, i) => (
                    <li
                      key={i}
                      className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-slate-100 px-3 py-2 text-sm"
                    >
                      <div className="flex items-center gap-3">
                        <span className="font-mono text-slate-500">{formatDate(p.date)}</span>
                        {p.month && (
                          <Badge tone="violet">{formatMonth(p.month)}</Badge>
                        )}
                        {(p.comment || p.note) && (
                          <span className="text-slate-500">{p.comment || p.note}</span>
                        )}
                      </div>
                      <span className="font-mono font-semibold text-slate-700">
                        {formatMoney(p.amount)}
                      </span>
                    </li>
                  ))}
              </ul>
            )}
          </Card>

          {/* PER-GURUH maosh sozlamasi.
              ARXIVLANGAN (yopilgan) guruhlar ham ro'yxatda qoladi: aks holda o'qituvchining yagona
              guruhi yopilgach bu bo'lim butunlay yo'qolib, "Guruh biriktirilmagan" chiqardi va
              o'tgan oylar uchun maoshni na ko'rish, na tuzatish mumkin bo'lardi. */}
          {salaryGroups.length === 0 ? (
            <Card>
              <div className="state">
                <h4>Guruh biriktirilmagan</h4>
                <p>
                  Maosh per-guruh hisoblanadi. Avval bu o'qituvchiga guruh biriktiring (guruh
                  formasida).
                </p>
              </div>
            </Card>
          ) : (
            <GroupSalaryEditor
              key={salaryVersion}
              teacherId={teacher.id}
              groups={salaryGroups}
              subjects={subjects}
              lines={salaryLedger?.groups}
              saving={salaryLoading}
              onSaved={reloadSalary}
            />
          )}

          {salaryLoading && !salaryLedger ? (
            <Card>
              <Loader label="Yuklanmoqda..." />
            </Card>
          ) : !salaryLedger || salaryLedger.months.length === 0 ? (
            <Card>
              <div className="state">
                <h4>Maosh yozuvi yo'q</h4>
                <p>Ushbu o'qituvchi uchun maosh ma'lumotlari topilmadi.</p>
              </div>
            </Card>
          ) : (
            <Card tight title="Oylar bo'yicha">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-sm">
                  <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="px-4 py-3">Oy</th>
                      <th className="px-4 py-3 text-right">Hisoblangan</th>
                      <th className="px-4 py-3 text-right">Berildi</th>
                      <th className="px-4 py-3 text-right">Qoldiq</th>
                      <th className="px-4 py-3">Holat</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {salaryLedger.months.map((row) => (
                      <tr key={row.month} className="hover:bg-slate-50/60">
                        <td className="px-4 py-3 font-medium text-slate-800">
                          <div>{formatMonth(row.month)}</div>
                          {(row.substituteFee ?? 0) > 0 && (
                            <div className="text-[11px] font-semibold text-emerald-600">
                              O'rinbosarlik: +{formatMoney(row.substituteFee ?? 0)}
                            </div>
                          )}
                          {(row.substituteDeduction ?? 0) > 0 && (
                            <div className="text-[11px] font-semibold text-amber-700">
                              O'rinbosarga: −{formatMoney(row.substituteDeduction ?? 0)}
                            </div>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right font-mono text-slate-700">
                          {formatMoney(row.expected)}
                          {/* FOIZLI maoshda pul hali yig'ilmagan bo'lsa "Hisoblangan" 0 chiqadi
                              (masalan guruh yopilib, o'quvchilar to'lamay qolgan oy). Shu sabab
                              yonida "hisoblangan bo'yicha" — hammasi to'lansa qancha bo'lishi. */}
                          {(row.potentialExpected ?? 0) > row.expected && (
                            <div
                              className="text-[11px] font-normal text-amber-600"
                              title="O'quvchilarga hisoblangan (qarz bilan) summadan chiqadigan maosh — pul kelgani sari asosiy raqam shunga yaqinlashadi"
                            >
                              hisob bo'yicha {formatMoney(row.potentialExpected ?? 0)}
                            </div>
                          )}
                        </td>
                        <td className="px-4 py-3 text-right font-mono text-emerald-700">
                          {formatMoney(row.paid)}
                        </td>
                        <td className="px-4 py-3 text-right font-mono text-red-600">
                          {formatMoney(row.remaining)}
                        </td>
                        <td className="px-4 py-3">
                          <Badge
                            tone={
                              row.status === 'paid'
                                ? 'green'
                                : row.status === 'partial'
                                  ? 'amber'
                                  : 'default'
                            }
                            dot
                          >
                            {row.status === 'paid'
                              ? "To'liq"
                              : row.status === 'partial'
                                ? 'Qisman'
                                : "To'lanmadi"}
                          </Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}

          {/* O'zgarishlar tarixi — `audit` ruxsati bilan (admin/superadmin har doim ko'radi) */}
          {canSeeAudit && (
            <Card title="O'zgarishlar tarixi">
              <AuditHistoryList
                filters={{ teacherId: teacher.id }}
                emptyLabel="Maosh bo'yicha o'zgarishlar yo'q"
              />
            </Card>
          )}
        </div>
      )}

      {/* BONUS TAB — o'quvchini ushlab turish bonuslari (maoshga qo'shilmaydi, alohida qayd) */}
      {tab === 'bonus' && <TeacherBonusPanel data={bonuses} loading={bonusLoading} />}

      {/* PERFORMANCE TAB */}
      {tab === 'performance' && (
        <div className="space-y-4">
          {perfLoading || !perf ? (
            <Card>
              <Loader label="Yuklanmoqda..." />
            </Card>
          ) : (
            <>
              {/* KPI cards */}
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                <StatCard label="Jami o'quvchilar" value={perf.totalStudents} icon={Users} />
                <StatCard
                  label="Faol"
                  value={perf.activeStudents}
                  icon={Users}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                />
                <StatCard
                  label="Muzlatilgan"
                  value={perf.frozenStudents}
                  icon={Users}
                  iconBg="bg-amber-50"
                  iconColor="text-amber-600"
                />
                <StatCard
                  label="Ketgan"
                  value={perf.leftStudents}
                  icon={Users}
                  iconBg="bg-red-50"
                  iconColor="text-red-500"
                />
              </div>

              {/* Retention & Loss bars */}
              <Card title="Saqlab qolish statistikasi">
                <div className="space-y-5 p-1">
                  <div className="flex items-center gap-4">
                    <div className="w-36 text-sm text-slate-500">Retention (Faol %)</div>
                    <div className="flex flex-1 items-center gap-3">
                      <div className="relative h-4 flex-1 overflow-hidden rounded-full bg-slate-100">
                        <div
                          className={cn(
                            'h-full rounded-full transition-all',
                            perf.retentionPercent >= 80
                              ? 'bg-emerald-500'
                              : perf.retentionPercent >= 50
                                ? 'bg-amber-400'
                                : 'bg-red-500',
                          )}
                          style={{ width: `${perf.retentionPercent}%` }}
                        />
                      </div>
                      <span className="w-14 text-right font-mono text-sm font-semibold text-slate-700">
                        {perf.retentionPercent}%
                      </span>
                    </div>
                  </div>

                  <div className="flex items-center gap-4">
                    <div className="w-36 text-sm text-slate-500">Loss (Chiqib ketgan %)</div>
                    <div className="flex flex-1 items-center gap-3">
                      <div className="relative h-4 flex-1 overflow-hidden rounded-full bg-slate-100">
                        <div
                          className="h-full rounded-full bg-red-400 transition-all"
                          style={{ width: `${perf.lossPercent}%` }}
                        />
                      </div>
                      <span className="w-14 text-right font-mono text-sm font-semibold text-slate-700">
                        {perf.lossPercent}%
                      </span>
                    </div>
                  </div>
                </div>

                {/* Score badge */}
                <div className="mt-5 flex items-center justify-between border-t border-slate-100 pt-4">
                  <span className="text-sm text-slate-500">Samaradorlik bali</span>
                  <span
                    className={cn(
                      'inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-semibold',
                      scoreColor(perf.effectivenessScore),
                    )}
                  >
                    <span className={cn('h-2 w-2 rounded-full', scoreDot(perf.effectivenessScore))} />
                    {perf.effectivenessScore} / 100
                  </span>
                </div>
              </Card>

              {/* Detail table */}
              <Card title="Batafsil ko'rsatkichlar">
                <div className="divide-y divide-slate-100">
                  <MetricRow label="Guruhlar soni" value={String(perf.groupCount)} />
                  <MetricRow label="Jami o'quvchilar (slot)" value={String(perf.totalStudents)} />
                  <MetricRow
                    label="Faol o'quvchilar"
                    value={String(perf.activeStudents)}
                    color="text-emerald-700"
                  />
                  <MetricRow
                    label="Muzlatilgan"
                    value={String(perf.frozenStudents)}
                    color="text-amber-600"
                  />
                  <MetricRow
                    label="Ketgan"
                    value={String(perf.leftStudents)}
                    color="text-red-500"
                  />
                  <MetricRow
                    label="Retention %"
                    value={`${perf.retentionPercent}%`}
                    extra={<RetentionBar value={perf.retentionPercent} />}
                  />
                  <MetricRow label="Loss %" value={`${perf.lossPercent}%`} />
                </div>
              </Card>

              {perf.totalStudents === 0 && (
                <Card>
                  <div className="state">
                    <h4>Ma'lumot yo'q</h4>
                    <p>
                      Bu o'qituvchining guruhlarida hali o'quvchilar yo'q yoki guruh biriktirilmagan.
                    </p>
                  </div>
                </Card>
              )}
            </>
          )}
        </div>
      )}

      {/* FIKRLAR TAB — o'quvchilardan yig'ilgan fikrlar (faqat ma'muriyat ko'radi) */}
      {tab === 'fikrlar' && canSeeReviews && id && <TeacherReviewsFeed teacherId={id} />}

      {/* AI TAHLIL TAB — o'quvchi oqimi, ketish sabablari, jurnal intizomi, rivojlanish + AI xulosasi */}
      {tab === 'ai' && id && <TeacherAiPanel teacherId={id} teacherName={teacher.fullName} />}

      {/* O'qituvchi rasmi — dumaloq avatarni bosganda ochiladi (kamera yoki fayl).
          O'quvchi sahifasidagi bilan BITTA komponent (PhotoDialog). */}
      <PhotoDialog
        open={photoOpen}
        currentUrl={teacher.photoUrl ?? null}
        // Rasm hali yo'q bo'lsa darhol kamera yoqiladi — shunisi tezroq.
        startWithCamera={!teacher.photoUrl}
        title="O'qituvchi rasmi"
        hint="Doira ichidagi qism o'qituvchi profilida dumaloq avatar bo'lib chiqadi."
        onClose={() => setPhotoOpen(false)}
        onSaved={async (url) => {
          if (!id) return
          await updateTeacherPhoto(id, url)
          setTeacher((t) => (t ? { ...t, photoUrl: url } : t))
        }}
      />
    </div>
  )
}

/**
 * Tanlangan oy kartochkasidagi bitta raqam (tushum yoki maosh). `hint` — raqam ostidagi kichik
 * izoh: "bo'lishi kerak" va "yig'ilmagan" kabi qiymatlar izohsiz bir-biri bilan adashtiriladi.
 */
/**
 * Jamlanma katagi — "beriladigan / berildi / qoldi". `MonthTile` dan farqi: raqam KATTA va chapga
 * tekislangan (bu sahifaning eng birinchi o'qiladigan uch raqami), izoh esa doim ko'rinadi.
 */
function TotalTile({
  label,
  value,
  valueClass = 'text-slate-800',
  hint,
  hintClass = 'text-slate-400',
}: {
  label: string
  value: string
  valueClass?: string
  hint?: string
  hintClass?: string
}) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50/60 px-4 py-3">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-1 font-mono text-[22px] font-semibold leading-none', valueClass)}>
        {value}
      </p>
      {hint && <p className={cn('mt-1.5 text-[11px] leading-tight', hintClass)}>{hint}</p>}
    </div>
  )
}

function MonthTile({
  label,
  value,
  valueClass = 'text-slate-700',
  hint,
  hintClass = 'text-slate-400',
}: {
  label: string
  value: string
  valueClass?: string
  hint?: string
  hintClass?: string
}) {
  return (
    <div className="rounded-xl border border-slate-100 px-3 py-2.5 text-center">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-mono font-semibold', valueClass)}>{value}</p>
      {hint && <p className={cn('mt-0.5 text-[11px] leading-tight', hintClass)}>{hint}</p>}
    </div>
  )
}

type SalaryRow = {
  groupId: string
  name: string
  courseName: string
  monthlyFee: number
  mode: string
  percent: number
  fixed: number
  /** Guruh yopilgan/arxivlangan — ro'yxatda qoladi (o'tgan oylar hisobi shunga bog'liq) */
  archived: boolean
}

/**
 * O'qituvchining HAR guruhi uchun alohida maosh sozlamasi: "Foiz" (shu guruh to'lovidan %) yoki
 * "Qat'iy" (shu guruh uchun qat'iy summa) — boshqa variant yo'q. Saqlanganda o'qituvchi oyligi
 * guruhlar ulushi yig'indisi sifatida hisoblanadi.
 */
function GroupSalaryEditor({
  teacherId,
  groups,
  subjects,
  lines,
  saving,
  onSaved,
}: {
  teacherId: string
  groups: Group[]
  subjects: Subject[]
  lines?: GroupSalaryLine[]
  saving: boolean
  onSaved: () => void
}) {
  const lineByGroup = useMemo(() => {
    const m: Record<string, GroupSalaryLine> = {}
    ;(lines ?? []).forEach((l) => (m[l.groupId] = l))
    return m
  }, [lines])

  // Har qator FOIZ yoki QAT'IY — "umumiy" yo'q. Sozlanmagan guruh amaldagi (ledger) qiymatdan,
  // u ham bo'lmasa "foiz 0" dan boshlanadi (admin tanlaydi).
  const buildRows = (): SalaryRow[] =>
    groups.map((g) => {
      const line = (lines ?? []).find((l) => l.groupId === g.id)
      const raw =
        g.teacherSalaryMode === 'fixed' ? 'fixed' : g.teacherSalaryMode === 'percent' ? 'percent' : ''
      const mode: string = raw || (line?.mode === 'fixed' ? 'fixed' : 'percent')
      return {
        groupId: g.id,
        name: g.name,
        courseName: subjects.find((s) => s.id === g.courseId)?.name ?? '',
        monthlyFee: g.monthlyFee,
        mode,
        percent: raw === 'percent' ? g.teacherSalaryPercent ?? 0 : line?.percent ?? g.teacherSalaryPercent ?? 0,
        fixed: raw === 'fixed' ? g.teacherSalaryFixed ?? 0 : line?.fixed ?? g.teacherSalaryFixed ?? 0,
        archived: !!g.isArchived,
      }
    })

  const [rows, setRows] = useState<SalaryRow[]>(buildRows)
  const [initial] = useState<SalaryRow[]>(buildRows)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  const setRow = (gid: string, patch: Partial<SalaryRow>) =>
    setRows((rs) => rs.map((r) => (r.groupId === gid ? { ...r, ...patch } : r)))

  const isRowDirty = (r: SalaryRow) => {
    const o = initial.find((x) => x.groupId === r.groupId)
    return (
      !o ||
      r.mode !== o.mode ||
      (r.mode === 'percent' && r.percent !== o.percent) ||
      (r.mode === 'fixed' && r.fixed !== o.fixed)
    )
  }

  const dirty = useMemo(() => rows.some(isRowDirty), [rows, initial])

  const handleSave = async () => {
    setBusy(true)
    setSaved(false)
    try {
      // Faqat admin o'zgartirgan qatorlarni yuboramiz — aks holda sozlanmagan (mode="")
      // guruhlar ham hozirgi UI qiymati bilan "muzlab" qoladi va endi umumiy sozlamani
      // kuzatmay qo'yadi.
      const items: GroupSalaryItem[] = rows.filter(isRowDirty).map((r) => ({
        groupId: r.groupId,
        mode: r.mode,
        percent: r.mode === 'percent' ? r.percent : 0,
        fixed: r.mode === 'fixed' ? r.fixed : 0,
      }))
      if (items.length === 0) return
      await saveGroupSalaries(teacherId, items)
      setSaved(true)
      onSaved()
    } catch (e) {
      alert('Saqlashda xato: ' + ((e as Error)?.message ?? ''))
    } finally {
      setBusy(false)
    }
  }

  const periodTotal = (lines ?? []).reduce((a, l) => a + l.periodExpected, 0)

  return (
    <Card
      title="Per-guruh maosh"
      sub="Har guruh uchun alohida foiz yoki qat'iy summa. O'qituvchi oyligi = guruhlar yig'indisi. Yopilgan guruhlar ham qoladi — o'tgan oylar hisobi ularga bog'liq."
    >
      <div className="space-y-2">
        {rows.map((r) => {
          const line = lineByGroup[r.groupId]
          return (
            <div
              key={r.groupId}
              className={cn(
                'rounded-xl border p-3',
                r.archived ? 'border-slate-200 bg-slate-50/60' : 'border-slate-200',
              )}
            >
              <div className="flex flex-wrap items-center justify-between gap-2">
                <div className="min-w-0">
                  <div className="flex items-center gap-2 font-semibold text-slate-800">
                    {r.name}
                    {r.archived && <Badge tone="default">Yopilgan</Badge>}
                  </div>
                  <div className="text-xs text-slate-400">
                    {r.courseName || '—'} · oylik {formatMoney(r.monthlyFee)}
                  </div>
                </div>
                {line && (
                  <div className="text-right">
                    <div className="text-[11px] text-slate-400">Davr bo'yicha</div>
                    <div className="font-mono text-sm font-semibold text-emerald-700">
                      {formatMoney(line.periodExpected)}
                    </div>
                    {line.substituteDeduction && line.substituteDeduction > 0 ? (
                      <div className="text-[11px] font-medium text-amber-700 mt-0.5">
                        O'rinbosarga: −{formatMoney(line.substituteDeduction)} ({line.substitutedLessons || 1} dars)
                      </div>
                    ) : null}
                  </div>
                )}
              </div>

              <div className="mt-3 flex flex-wrap items-center gap-2">
                <div className="inline-flex rounded-lg border border-slate-200 p-0.5">
                  {[
                    { v: 'percent', label: 'Foiz' },
                    { v: 'fixed', label: "Qat'iy summa" },
                  ].map((opt) => (
                    <button
                      key={opt.v}
                      type="button"
                      onClick={() => setRow(r.groupId, { mode: opt.v })}
                      className={cn(
                        'rounded-md px-3 py-1 text-xs font-medium transition-colors',
                        r.mode === opt.v
                          ? 'bg-brand-600 text-white'
                          : 'text-slate-500 hover:text-slate-700',
                      )}
                    >
                      {opt.label}
                    </button>
                  ))}
                </div>

                {r.mode === 'percent' && (
                  <div className="flex items-center gap-1">
                    <input
                      type="number"
                      min={0}
                      max={100}
                      step="any"
                      value={r.percent}
                      onChange={(e) => setRow(r.groupId, { percent: Number(e.target.value) })}
                      className="w-24 rounded-lg border border-slate-200 px-2.5 py-1.5 font-mono text-sm focus:border-brand-400 focus:outline-none"
                    />
                    <span className="text-sm text-slate-500">%</span>
                  </div>
                )}
                {r.mode === 'fixed' && (
                  <div className="flex items-center gap-1">
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={r.fixed}
                      onChange={(e) => setRow(r.groupId, { fixed: Number(e.target.value) })}
                      className="w-40 rounded-lg border border-slate-200 px-2.5 py-1.5 font-mono text-sm focus:border-brand-400 focus:outline-none"
                    />
                    <span className="text-sm text-slate-500">so'm</span>
                  </div>
                )}
              </div>
            </div>
          )
        })}
      </div>

      <div className="mt-4 flex items-center justify-between border-t border-slate-100 pt-3">
        <div className="text-sm text-slate-500">
          Davr jami hisoblangan:{' '}
          <span className="font-mono font-semibold text-slate-800">{formatMoney(periodTotal)}</span>
        </div>
        <div className="flex items-center gap-3">
          {saved && !dirty && (
            <span className="text-xs font-medium text-emerald-600">Saqlandi ✓</span>
          )}
          <button
            type="button"
            disabled={busy || saving || !dirty}
            onClick={handleSave}
            className={cn(
              'rounded-lg px-4 py-2 text-sm font-semibold text-white transition-colors',
              busy || saving || !dirty
                ? 'cursor-not-allowed bg-slate-300'
                : 'bg-brand-600 hover:bg-brand-700',
            )}
          >
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </button>
        </div>
      </div>
    </Card>
  )
}

/** O'rin nishoni — 1/2/3 rangli dumaloq, qolganlari oddiy raqam. */
function RankBadge({ rank }: { rank: number }) {
  if (rank > 3) {
    return <span className="inline-flex h-7 w-7 items-center justify-center text-sm font-semibold text-slate-400">{rank}</span>
  }
  const tone =
    rank === 1
      ? 'bg-amber-100 text-amber-700'
      : rank === 2
        ? 'bg-slate-200 text-slate-700'
        : 'bg-orange-100 text-orange-700'
  return (
    <span className={cn('inline-flex h-7 w-7 items-center justify-center rounded-full text-sm font-bold', tone)}>
      {rank}
    </span>
  )
}

const podiumStyle: Record<number, { wrap: string; icon: typeof Trophy; iconColor: string; label: string }> = {
  1: {
    wrap: 'border-amber-200 bg-amber-50',
    icon: Trophy,
    iconColor: 'text-amber-500',
    label: '1-o\'rin',
  },
  2: {
    wrap: 'border-slate-200 bg-slate-50',
    icon: Medal,
    iconColor: 'text-slate-400',
    label: '2-o\'rin',
  },
  3: {
    wrap: 'border-orange-200 bg-orange-50',
    icon: Award,
    iconColor: 'text-orange-500',
    label: '3-o\'rin',
  },
}

/** TOP-3 reyting kartasi (oltin/kumush/bronza). */
function PodiumCard({ row }: { row: TeacherRatingRow }) {
  const style = podiumStyle[row.rank] ?? podiumStyle[3]
  const Icon = style.icon
  return (
    <div className={cn('rounded-2xl border p-4', style.wrap)}>
      <div className="mb-2 flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">{style.label}</span>
        <Icon className={cn('h-6 w-6', style.iconColor)} />
      </div>
      <div className="truncate text-base font-semibold text-slate-800">{row.fullName}</div>
      <div className="mb-3 flex flex-wrap gap-1">
        {row.groups
          .split(',')
          .map((g) => g.trim())
          .filter(Boolean)
          .map((g, i) => (
            <Badge key={i} tone="violet">
              {g}
            </Badge>
          ))}
      </div>
      <div className="font-mono text-3xl font-bold text-slate-800">{row.ball}</div>
      <div className="text-xs text-slate-400">ball</div>
    </div>
  )
}

function InfoRow({
  label,
  value,
  mono,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="flex justify-between gap-4 border-b border-slate-100 py-2.5 last:border-0">
      <span className="text-sm text-slate-400">{label}</span>
      <span className={cn('text-right text-sm font-medium text-slate-800', mono && 'font-mono')}>
        {value}
      </span>
    </div>
  )
}

function MetricRow({
  label,
  value,
  color,
  extra,
}: {
  label: string
  value: string
  color?: string
  extra?: React.ReactNode
}) {
  return (
    <div className="flex items-center justify-between gap-4 py-3">
      <span className="text-sm text-slate-500">{label}</span>
      <div className="flex items-center gap-3">
        {extra}
        <span className={cn('font-mono text-sm font-semibold text-slate-800', color)}>{value}</span>
      </div>
    </div>
  )
}
