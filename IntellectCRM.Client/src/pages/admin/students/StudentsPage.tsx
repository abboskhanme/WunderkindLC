import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { usePersistentState } from '@/hooks/usePersistentState'
import { StudentViewModal } from './StudentViewModal'
import { Plus, Search, Pencil, Trash2, Send, Download, X, Wallet, History, Archive, RotateCcw, FileDown, Upload, ChevronLeft, ChevronRight, Lock, LockOpen, Loader2, Phone, PhoneCall, Cake, Medal, Snowflake, CalendarClock, CheckCircle2 } from 'lucide-react'
import type { Gender, Student, Teacher, District } from '@/types'
import { getDistricts } from '@/api/services/districts'
import type { StudentPayload, StudentImportResult } from '@/api/services/students'
import {
  getStudents,
  getArchivedStudents,
  archiveStudent,
  restoreStudent,
  createStudent,
  updateStudent,
  deleteStudent,
  addPayment,
  downloadStudentCredentials,
  downloadStudentImportTemplate,
  importStudents,
  setStudentLoginBlock,
  setStudentLoginBlockBulk,
  downloadSelectedStudents,
  getStudentBalls,
} from '@/api/services/students'
import {
  getClasses,
  bulkFreezeMembers,
  bulkActivateMembers,
  bulkMembershipSummary,
} from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { genderLabels } from '@/config/constants'
import { formatDate, formatMoney, exportToCsv, cn, apiErrorMessage } from '@/lib/utils'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { StudentFormModal } from './StudentFormModal'
import { SmsModal } from './SmsModal'
import { NeedContactModal } from './contacts/NeedContactModal'
import { PaymentModal } from './PaymentModal'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { PaymentHistoryModal } from './PaymentHistoryModal'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { CallPickerModal, type CallOption } from '@/components/CallPickerModal'

type BalanceFilter = 'all' | 'debt' | 'paid'
/** Surat filtri — rasmi bor / rasmi yo'q o'quvchilarni ajratish uchun. */
type PhotoFilter = 'all' | 'with' | 'without'
type Tab = 'active' | 'archived'
type SortOption = 'default' | 'ball-desc' | 'ball-asc'

/**
 * Qarz oralig'i maydonlarida strelka (▲▼) bosilganda summa shu qadam bilan o'zgaradi.
 * Qo'lda ISTALGAN summa yozilaveradi — qadam faqat strelka/klaviatura uchun (`step`
 * validatsiya emas: maydon `<form>` ichida emas, ya'ni "50 000 ga karrali bo'lsin"
 * degan cheklov qo'ymaydi).
 */
const DEBT_STEP = 50_000

/** Filtr select'lari uchun yagona ko'rinish (toolbar ichida). */
const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/**
 * "Holat" ustuni (va CSV eksporti) uchun a'zolik holati. `memberState` — backend hisoblagan
 * yorliq (active > trial > yearFrozen > frozen); eski javoblarda bo'lmasa `active` bayrog'iga
 * tushamiz.
 */
type StateFilter = 'all' | 'active' | 'trial' | 'yearFrozen' | 'frozen' | 'inactive'

/**
 * O'quvchining USTUN ko'rsatadigan holati — filtr ham AYNAN shundan ishlaydi, ya'ni
 * "Muzlatilgan" tanlanganda ro'yxatdagi har qatorda "Muzlatilgan" yozib turadi.
 * (Bir nechta guruhda turlicha bo'lsa backend ustunlik beradi: active > trial > yearFrozen > frozen.)
 *
 * ⚠️ `yearFrozen` ("Aktiv muzlatilgan") oddiy `frozen` dan ALOHIDA qiymat: hisob-kitobi bir xil
 * bo'lsa ham, "yangi o'quv yiliga nechta o'quvchi bilan o'tyapmiz" ni ajratib sanash uchun.
 */
function studentState(s: Student): 'active' | 'trial' | 'yearFrozen' | 'frozen' | 'none' {
  if (s.memberState === 'yearFrozen') return 'yearFrozen'
  if (s.memberState === 'frozen') return 'frozen'
  if (s.memberState === 'trial') return 'trial'
  if (s.active || s.memberState === 'active') return 'active'
  return 'none'
}

function memberStateInfo(s: Student): { label: string; chip: string; dot: string } {
  // Indigo — oddiy muzlatishning sky rangidan va sinovning violet rangidan ajralib tursin.
  if (s.memberState === 'yearFrozen')
    return { label: 'Aktiv muzlatilgan', chip: 'bg-indigo-50 text-indigo-700', dot: 'bg-indigo-500' }
  if (s.memberState === 'frozen')
    return { label: 'Muzlatilgan', chip: 'bg-sky-50 text-sky-700', dot: 'bg-sky-500' }
  if (s.memberState === 'trial')
    return { label: 'Sinovda', chip: 'bg-violet-50 text-violet-700', dot: 'bg-violet-500' }
  if (s.active || s.memberState === 'active')
    return { label: 'Aktiv', chip: 'bg-emerald-50 text-emerald-700', dot: 'bg-emerald-500' }
  return { label: 'Aktiv emas', chip: 'bg-slate-100 text-slate-500', dot: 'bg-slate-400' }
}

/** Ism bo'yicha barqaror avatar rangi (crm/ namunasidagi kabi). */
const AVATAR_COLORS = [
  '#7c3aed', '#2563eb', '#0891b2', '#16a34a', '#ca8a04', '#dc2626', '#db2777', '#9333ea',
]
function avatarColor(name: string): string {
  let h = 0
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) >>> 0
  return AVATAR_COLORS[h % AVATAR_COLORS.length]
}
function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
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

export function StudentsPage() {
  const { user } = useAuth()
  const { can } = usePerm()
  const navigate = useNavigate()
  const [tab, setTab] = usePersistentState<Tab>('students.tab', 'active')
  const [students, setStudents] = useState<Student[]>([])
  const [archived, setArchived] = useState<Student[]>([])
  const [classNames, setClassNames] = useState<string[]>([])
  /** TUGATILGAN (arxivlangan) guruh nomlari — filtrda alohida guruhda ko'rsatiladi. */
  const [archivedClassNames, setArchivedClassNames] = useState<string[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [groupTeachers, setGroupTeachers] = useState<Record<string, string>>({})
  /** O'quvchi ID -> ball (jurnal baholari + bajarilgan baholash mezonlari soni). */
  const [ballMap, setBallMap] = useState<Record<string, number>>({})
  const [loading, setLoading] = useState(true)
  /** Arxivga ko'chirish — ReasonPromptModal uchun. */
  const [archiveTarget, setArchiveTarget] = useState<Student | null>(null)

  // filtrlar — sahifadan chiqib qaytilganda saqlanadi (usePersistentState)
  const [search, setSearch] = usePersistentState('students.search', '')
  /**
   * Qidiruv maydonining JORIY matni. `usePersistentState` har `setState` da
   * `sessionStorage.setItem` (sinxron I/O) qiladi — ya'ni har HARF diskka yozilib, yozish
   * sezilarli sekinlashardi. Endi input LOKAL state'dan chiziladi (darhol), saqlanadigan va
   * FILTRLAYDIGAN qiymat esa 250 ms tinchlikdan keyin yangilanadi.
   * ⚠️ Boshlang'ich qiymat saqlangan `search` dan olinadi — sahifaga qaytilganda qidiruv
   * avvalgidek TIKLANADI.
   */
  const [searchInput, setSearchInput] = useState(search)
  useEffect(() => {
    if (searchInput === search) return
    const t = setTimeout(() => setSearch(searchInput), 250)
    return () => clearTimeout(t)
  }, [searchInput, search, setSearch])
  const [classFilter, setClassFilter] = usePersistentState('students.classFilter', 'all')
  const [teacherFilter, setTeacherFilter] = usePersistentState('students.teacherFilter', 'all')
  const [genderFilter, setGenderFilter] = usePersistentState<'all' | Gender>('students.genderFilter', 'all')
  const [balanceFilter, setBalanceFilter] = usePersistentState<BalanceFilter>('students.balanceFilter', 'all')
  /**
   * QARZ SUMMASI oralig'i ("shu summadan baland / shu summadan past qarzdorlar").
   * MATN sifatida saqlanadi: bo'sh satr = chegara qo'yilmagan, bu `0` dan FARQ qiladi
   * (0 kiritilsa "qarzi 0 dan katta", ya'ni barcha qarzdorlar degani).
   */
  const [debtMin, setDebtMin] = usePersistentState('students.debtMin', '')
  const [debtMax, setDebtMax] = usePersistentState('students.debtMax', '')
  /**
   * A'zolik HOLATI filtri — "Holat" ustunidagi yorliq bilan AYNAN bir xil qiymatlar
   * (`memberStateInfo`). `inactive` ("Aktiv emas") ATAYIN qoldirildi: u sinov + muzlatilgan +
   * guruhsizlarni birga beradi va eski saqlangan tanlov ham buzilmaydi.
   */
  const [activeFilter, setActiveFilter] = usePersistentState<StateFilter>('students.activeFilter', 'all')
  const [districtFilter, setDistrictFilter] = usePersistentState('students.districtFilter', 'all')
  const [schoolFilter, setSchoolFilter] = usePersistentState('students.schoolFilter', 'all')
  const [photoFilter, setPhotoFilter] = usePersistentState<PhotoFilter>('students.photoFilter', 'all')
  /** "Bugun tug'ilgan kun" filtri — yil hisobga olinmasdan oy/kun bo'yicha solishtiriladi. */
  const [birthdayToday, setBirthdayToday] = usePersistentState('students.birthdayToday', false)
  const [sort, setSort] = usePersistentState<SortOption>('students.sort', 'default')
  const [districts, setDistricts] = useState<District[]>([])

  // tanlash
  const [selected, setSelected] = useState<Set<string>>(new Set())
  /** Tanlanganlarni "Bog'lanish kerak" navbatiga qo'shish oynasi (`contacts` ruxsati). */
  const [contactOpen, setContactOpen] = useState(false)

  // modallar
  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<Student | null>(null)
  // Yangi o'quvchi yaratilgach login/parolni ko'rsatish uchun (Eye tugmasi esa shaxsiy daftarga boradi).
  const [viewing, setViewing] = useState<Student | null>(null)
  const openNotebook = (s: Student) => navigate(`/admin/students/${s.id}`)
  const [smsRecipients, setSmsRecipients] = useState<Student[]>([])
  const [paying, setPaying] = useState<Student | null>(null)
  /** To'lov cheki — to'lov kiritilgach shu tranzaksiya cheki ochiladi. */
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)
  const [historyOf, setHistoryOf] = useState<Student | null>(null)
  /** Qo'ng'iroq qilish — CallPickerModal uchun tanlangan o'quvchi */
  const [callStudent, setCallStudent] = useState<Student | null>(null)
  const [deleting, setDeleting] = useState<Student | null>(null)
  /** Tanlanganlarni arxivga ko'chirish — sabab kiritish uchun */
  const [archivingSelected, setArchivingSelected] = useState(false)
  const [archiveReasonModal, setArchiveReasonModal] = useState(false)
  /** Tanlanganlar login'ini ommaviy cheklash/ochish — so'rov jarayonida. */
  const [bulkLoginBlocking, setBulkLoginBlocking] = useState(false)
  /**
   * Tanlanganlarni ommaviy MUZLATISH / «AKTIV MUZLATISH» / AKTIVLASHTIRISH — qaysi amal uchun
   * tasdiq modali ochiq. `null` = modal yopiq. Hammasi bitta state'da, chunki ular bir vaqtda
   * ochilmaydi. `yearFreeze` — o'sha muzlatishning O'ZI, faqat yangi o'quv yili belgisi bilan.
   */
  const [bulkMembership, setBulkMembership] = useState<'freeze' | 'yearFreeze' | 'activate' | null>(null)
  /** Ommaviy a'zolik amali so'rov jarayonida — `bulkLoginBlocking` naqshi bilan bir xil. */
  const [membershipBusy, setMembershipBusy] = useState(false)

  // Excel'dan ommaviy import
  const [importing, setImporting] = useState(false)
  const [importResult, setImportResult] = useState<StudentImportResult | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const handleImportFile = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    e.target.value = '' // bir xil faylni qayta tanlash mumkin bo'lsin
    if (!file) return
    setImporting(true)
    try {
      const result = await importStudents(file)
      setImportResult(result)
      if (result.created > 0) setStudents(await getStudents())
    } catch (err) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
      alert('Yuklashda xatolik: ' + (msg ?? 'fayl noto\'g\'ri yoki server xatosi'))
    } finally {
      setImporting(false)
    }
  }

  // Chegirma o'zgarganda — yangi chegirmani joriy oyga qo'llashni so'rash
  const [discountPrompt, setDiscountPrompt] = useState<{
    id: string
    values: StudentPayload
    oldPct: number
    oldAmount: number
    newPct: number
    newAmount: number
  } | null>(null)

  useEffect(() => {
    setLoading(true)
    Promise.all([getStudents(), getArchivedStudents()])
      .then(([active, arch]) => {
        setStudents(active)
        setArchived(arch)
      })
      .finally(() => setLoading(false))
    // includeArchived=true — TUGATILGAN guruhlar ham filtrda chiqsin (o'quvchilar ularda qolgan:
    // muzlatilgan a'zolik, qarz va h.k.). Faol va tugatilgan alohida ro'yxatda ko'rsatiladi.
    Promise.all([getClasses(true), getTeachers()]).then(([cs, ts]) => {
      const active = cs.filter((c) => !c.isArchived)
      const activeNames = active.map((c) => c.name)
      setClassNames(activeNames)
      // Tugatilgan guruhlar — nomi faol guruhda takrorlanmaganlari (aks holda ikki marta chiqardi).
      setArchivedClassNames(
        Array.from(new Set(cs.filter((c) => c.isArchived).map((c) => c.name)))
          .filter((n) => !activeNames.includes(n)),
      )
      setTeachers(ts)
      // Guruh nomi -> o'qituvchi ID xarita. Avval arxiv, keyin faol — bir xil nomda FAOL guruh ustun.
      const mapping: Record<string, string> = {}
      cs.filter((c) => c.isArchived).forEach((c) => {
        if (c.teacherId) mapping[c.name] = c.teacherId
      })
      active.forEach((c) => {
        if (c.teacherId) mapping[c.name] = c.teacherId
      })
      setGroupTeachers(mapping)
    })
    getDistricts().then(setDistricts)
    getStudentBalls()
      .then((balls) => {
        const map: Record<string, number> = {}
        balls.forEach((b) => {
          map[b.studentId] = b.ball
        })
        setBallMap(map)
      })
      .catch(() => setBallMap({}))
  }, [])

  // Joriy tab manbai.
  const source = tab === 'active' ? students : archived
  /**
   * Holat filtri variantlaridagi sonlar — BOSHQA filtrlarga bog'liq emas (qidiruv/guruh
   * tanlangan bo'lsa ham): savol "markazda nechta muzlatilgan bor", "shu qidiruv ichida
   * nechta" emas. Aks holda son filtr o'zgargan sayin sakrab, ma'nosini yo'qotardi.
   */
  const stateCounts = useMemo(() => {
    const c = { active: 0, trial: 0, yearFrozen: 0, frozen: 0, inactive: 0 }
    for (const s of source) {
      const st = studentState(s)
      if (st === 'active') c.active++
      else {
        c.inactive++
        if (st === 'trial') c.trial++
        else if (st === 'yearFrozen') c.yearFrozen++
        else if (st === 'frozen') c.frozen++
      }
    }
    return c
  }, [source])
  // "Bugun tug'ilgan kun" filtri uchun — yilsiz "MM-DD" solishtirish.
  const todayMonthDay = new Date().toISOString().slice(5, 10)

  // Qarz oralig'i chegarasi: bo'sh/xato qiymat = chegara YO'Q (`null`).
  // Bo'sh joylar olib tashlanadi — "1 000 000" deb yopishtirilgan summa ham ishlasin.
  const parseAmount = (v: string): number | null => {
    const raw = v.replace(/\s/g, '')
    if (raw === '') return null
    const n = Number(raw)
    return Number.isFinite(n) && n >= 0 ? n : null
  }
  const debtMinNum = parseAmount(debtMin)
  const debtMaxNum = parseAmount(debtMax)
  const debtRangeOn = debtMinNum !== null || debtMaxNum !== null
  /** "dan" > "gacha" — hech kim topilmaydi; foydalanuvchiga ochiq aytiladi (jim bo'sh ro'yxat emas). */
  const debtRangeInvalid = debtMinNum !== null && debtMaxNum !== null && debtMinNum > debtMaxNum

  /**
   * Filtrlash + saralash — `useMemo` bilan (yuqoridagi `stateCounts` naqshi). Ilgari bu ish HAR
   * renderda qaytadan bajarilardi: bitta harf yozilganda ham, katakcha belgilanganda ham, modal
   * ochilganda ham butun ro'yxat qaytadan filtrlanib-saralanardi. Natija o'zgarmaydi — faqat
   * haqiqatan bog'liq qiymat o'zgargandagina qayta hisoblanadi.
   */
  const filtered = useMemo(() => source.filter((s) => {
    const q = search.trim().toLowerCase()
    const matchSearch =
      !q ||
      s.fullName.toLowerCase().includes(q) ||
      s.parentFullName.toLowerCase().includes(q)
    /*
     * GURUH / O'QITUVCHI / AKTIVLIK — birga ishlaydi.
     *
     * ⚠️ Ilgari uchtasi ALOHIDA tekshirilardi: "o'qituvchi X" + "aktiv" = «X ning guruhida
     * a'zoligi bor» VA «bu o'quvchi biror joyda aktiv». Natijada X dan ketib (muzlatilib)
     * boshqa o'qituvchida aktiv bo'lgan o'quvchi X ning AKTIV ro'yxatida turaverardi.
     * Endi shart BITTA a'zolik ustida tekshiriladi: «X ning guruhida AKTIV a'zoligi bor».
     */
    const states = s.groupStates ?? []
    /** Shu a'zolik tanlangan holat filtriga mos keladimi. */
    const memberMatches = (st: string) =>
      activeFilter === 'all' ? true
      : activeFilter === 'inactive' ? st !== 'active'
      : st === activeFilter

    const matchClass =
      classFilter === 'all' ||
      (states.length > 0
        ? states.some((g) => g.name === classFilter && memberMatches(g.status))
        // Eski (guruhsiz) yozuvlar uchun zaxira — "asosiy guruh" yorlig'i.
        : s.className === classFilter)
    const matchTeacher =
      teacherFilter === 'all' ||
      states.some((g) => g.teacherId === teacherFilter && memberMatches(g.status))
    const matchGender = genderFilter === 'all' || s.gender === genderFilter
    const matchBalance =
      balanceFilter === 'all' ||
      (balanceFilter === 'debt' ? s.balance < 0 : s.balance >= 0)
    /*
     * QARZ SUMMASI oralig'i — "500 000 dan baland qarzi borlar" kabi savol uchun.
     * Solishtirish MUSBAT qarz miqdorida ketadi (`balance` manfiy bo'lgani uchun uni to'g'ridan
     * solishtirsak "baland/past" TESKARI ishlardi).
     * ⚠️ Chegara qo'yilgan bo'lsa QARZSIZLAR chiqmaydi (qarzi 0 bo'lgan o'quvchi "1 mln gacha"
     * shartiga formal mos kelardi va ro'yxat qarzdorlar o'rniga hammani ko'rsatardi).
     */
    const debt = s.balance < 0 ? -s.balance : 0
    const matchDebtRange =
      !debtRangeOn ||
      (debt > 0 &&
        (debtMinNum === null || debt >= debtMinNum) &&
        (debtMaxNum === null || debt <= debtMaxNum))
    // Aktivlik FILTRI o'quvchi darajasida faqat guruh/o'qituvchi tanlanmaganda qo'llanadi —
    // aks holda yuqoridagi a'zolik darajasidagi tekshiruv bilan ikki marta filtrlanib,
    // "X ning muzlatilgan o'quvchilari" ro'yxati bo'sh chiqib qolardi.
    const groupScoped = classFilter !== 'all' || teacherFilter !== 'all'
    const matchActive =
      activeFilter === 'all' || groupScoped ||
      (activeFilter === 'inactive' ? studentState(s) !== 'active' : studentState(s) === activeFilter)
    const matchDistrict = districtFilter === 'all' || s.districtId === districtFilter
    const matchSchool = schoolFilter === 'all' || s.schoolId === schoolFilter
    const matchBirthday = !birthdayToday || (s.birthDate && s.birthDate.slice(5, 10) === todayMonthDay)
    // Surat: birthCertificateUrl — o'quvchi rasmi (nomi eski, `types/index.ts` izohiga qarang).
    const hasPhoto = !!(s.birthCertificateUrl && s.birthCertificateUrl.trim())
    const matchPhoto =
      photoFilter === 'all' || (photoFilter === 'with' ? hasPhoto : !hasPhoto)
    return matchSearch && matchClass && matchTeacher && matchGender && matchBalance && matchDebtRange && matchActive && matchDistrict && matchSchool && matchBirthday && matchPhoto
  })
    .sort((a, b) => {
      if (sort === 'ball-desc' || sort === 'ball-asc') {
        const ballA = ballMap[a.id] ?? 0
        const ballB = ballMap[b.id] ?? 0
        if (ballA !== ballB) return sort === 'ball-desc' ? ballB - ballA : ballA - ballB
        return a.fullName.localeCompare(b.fullName)
      }
      // "Yangi kiritilgani tepada": tizimga kiritilgan vaqt (bo'lmasa qabul sanasi) bo'yicha kamayish.
      return (b.createdAt || b.enrollmentDate || '').localeCompare(a.createdAt || a.enrollmentDate || '')
    }),
  [source, search, classFilter, teacherFilter, genderFilter, balanceFilter, debtRangeOn, debtMinNum, debtMaxNum, activeFilter, districtFilter, schoolFilter, birthdayToday, todayMonthDay, photoFilter, sort, ballMap])

  /** Filtrlangan ro'yxatdagi JAMI qarz (musbat son) — toolbar o'ng tomonida ko'rsatiladi. */
  const filteredDebtTotal = useMemo(
    () => filtered.reduce((sum, s) => sum + (s.balance < 0 ? -s.balance : 0), 0),
    [filtered],
  )

  // Pagination — standart 30 talik, pastda sahifa hajmini tanlash mumkin.
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(30)
  const totalPages = Math.max(1, Math.ceil(filtered.length / pageSize))
  const pageClamped = Math.min(page, totalPages)
  const paged = useMemo(
    () => filtered.slice((pageClamped - 1) * pageSize, pageClamped * pageSize),
    [filtered, pageClamped, pageSize],
  )
  const rangeFrom = filtered.length === 0 ? 0 : (pageClamped - 1) * pageSize + 1
  const rangeTo = Math.min(filtered.length, pageClamped * pageSize)
  // Filtr/qidiruv/hajm o'zgarsa — birinchi sahifaga qaytamiz. Tanlov SAQLANADI — foydalanuvchi
  // qidirib bir o'quvchini belgilab, keyin boshqasini qidirib belgilay olishi uchun.
  useEffect(() => {
    setPage(1)
  }, [search, classFilter, teacherFilter, genderFilter, balanceFilter, debtMin, debtMax, activeFilter, districtFilter, schoolFilter, photoFilter, birthdayToday, sort, tab, pageSize])
  // Faqat tab (faol/arxiv/hammasi) almashganda tanlovni tozalaymiz — bu boshqa ro'yxat.
  useEffect(() => {
    setSelected(new Set())
  }, [tab])

  const selectedStudents = useMemo(
    () => source.filter((s) => selected.has(s.id)),
    [source, selected],
  )

  // Umumiy ball bo'yicha TOP-3 o'quvchi (filtrdan qat'i nazar) — medal ikonkasi uchun.
  const top3StudentIds = useMemo(
    () =>
      Object.entries(ballMap)
        .filter(([, ball]) => ball > 0)
        .sort((a, b) => b[1] - a[1])
        .slice(0, 3)
        .map(([id]) => id),
    [ballMap],
  )

  // hammasini tanlash holati
  const allSelected = filtered.length > 0 && filtered.every((s) => selected.has(s.id))
  const someSelected = filtered.some((s) => selected.has(s.id)) && !allSelected
  const headerCbRef = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (headerCbRef.current) headerCbRef.current.indeterminate = someSelected
  })

  const toggleOne = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const toggleAll = () =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (allSelected) filtered.forEach((s) => next.delete(s.id))
      else filtered.forEach((s) => next.add(s.id))
      return next
    })

  const clearSelection = () => setSelected(new Set())

  // Filtrlar standart holatdan farq qiladimi — "Tozalash" tugmasi shunda ko'rinadi.
  const filtersActive =
    search !== '' ||
    classFilter !== 'all' ||
    teacherFilter !== 'all' ||
    genderFilter !== 'all' ||
    balanceFilter !== 'all' ||
    debtRangeOn ||
    activeFilter !== 'all' ||
    districtFilter !== 'all' ||
    schoolFilter !== 'all' ||
    photoFilter !== 'all' ||
    birthdayToday ||
    sort !== 'default'
  const clearFilters = () => {
    // Maydonning o'zi ham darhol tozalanadi (debounce kutib turmasin).
    setSearchInput('')
    setSearch('')
    setClassFilter('all')
    setTeacherFilter('all')
    setGenderFilter('all')
    setBalanceFilter('all')
    setDebtMin('')
    setDebtMax('')
    setActiveFilter('all')
    setDistrictFilter('all')
    setSchoolFilter('all')
    setPhotoFilter('all')
    setBirthdayToday(false)
    setSort('default')
  }

  // Tanlanganlarni to'liq Excel'ga yuklab olish (server: profil + guruh holati + balans + login)
  const [exportingSelected, setExportingSelected] = useState(false)
  const handleExportExcel = async () => {
    if (exportingSelected || selectedStudents.length === 0) return
    setExportingSelected(true)
    try {
      await downloadSelectedStudents(selectedStudents.map((s) => s.id))
    } finally {
      setExportingSelected(false)
    }
  }

  const handleExport = () => {
    exportToCsv(
      'oquvchilar.csv',
      ['F.I.SH', 'Guruh', 'Holat', 'Jinsi', "Tug'ilgan kun", 'Manzil', 'Ota-ona', 'Telefon', 'Balans', 'Chegirma'],
      selectedStudents.map((s) => [
        s.fullName,
        s.groups && s.groups.length > 0 ? s.groups.join(', ') : s.className,
        memberStateInfo(s).label,
        genderLabels[s.gender],
        formatDate(s.birthDate),
        s.address,
        s.parentFullName,
        s.parentPhone,
        formatMoney(s.balance),
        s.discountPct > 0 || s.discountAmount > 0
          ? [
              s.discountPct > 0 ? `${s.discountPct}%` : null,
              s.discountAmount > 0 ? formatMoney(s.discountAmount) : null,
            ]
              .filter(Boolean)
              .join(' + ') + (s.discountNote ? ` — ${s.discountNote}` : '')
          : '',
      ]),
    )
  }

  const applyUpdate = (id: string, values: StudentPayload, applyDiscount: boolean) => {
    updateStudent(id, values, applyDiscount)
    // balansni saqlab qolib, qolgan maydonlarni yangilaymiz
    setStudents((prev) => prev.map((s) => (s.id === id ? { ...s, ...values } : s)))
  }

  const resolveDiscountPrompt = (applyDiscount: boolean) => {
    if (!discountPrompt) return
    applyUpdate(discountPrompt.id, discountPrompt.values, applyDiscount)
    setDiscountPrompt(null)
  }

  const handleFormSubmit = (values: StudentPayload) => {
    if (editing) {
      const id = editing.id
      const newPct = values.discountPct ?? 0
      const newAmount = values.discountAmount ?? 0
      const oldPct = editing.discountPct
      const oldAmount = editing.discountAmount
      // Guruh biriktirilishi o'zgarsa ham "joriy oyga qo'llash?" so'raladi — chegirma
      // boshqa guruhga ko'chsa joriy oy hisoblari qayta taqsimlanishi kerak bo'lishi mumkin.
      const newGroup = values.discountGroupId ?? ''
      const oldGroup = editing.discountGroupId ?? ''
      const discountChanged = newPct !== oldPct || newAmount !== oldAmount || newGroup !== oldGroup
      if (discountChanged) {
        // "Ha/Yo'q" tasdiq dialog'i — joriy oyga qo'llash yoki keyingi oydan?
        setDiscountPrompt({ id, values, oldPct, oldAmount, newPct, newAmount })
      } else {
        applyUpdate(id, values, false)
      }
    } else {
      createStudent(values).then((created) => {
        setStudents((prev) => [created, ...prev])
        // Yangi o'quvchining login/parolini darrov ko'rsatamiz.
        setViewing(created)
      })
    }
    setFormOpen(false)
    setEditing(null)
  }

  // DIQQAT: `await` SHART — saqlash muvaffaqiyatli bo'lgandagina modal yopiladi va balans
  // yangilanadi. Xato (masalan kvitansiya raqami band — 409) modalga QAYTA OTILADI: u yerda
  // "allaqachon kiritilgan to'lov" kartochkasi + "Baribir saqlash" ko'rsatiladi.
  const handlePayment = async (
    amount: number,
    month: string,
    groupId?: string,
    comment?: string,
    method?: string,
    date?: string,
    extra?: { receiptNo?: string; paidTime?: string; cardLast4?: string; forceReceipt?: boolean },
  ) => {
    if (!paying) return
    const id = paying.id
    const txId = await addPayment(id, amount, month, groupId, comment, method, date, extra)
    setStudents((prev) =>
      prev.map((s) => (s.id === id ? { ...s, balance: s.balance + amount } : s)),
    )
    setPaying(null)
    // CHEK: to'lov saqlangach kvitansiya ochiladi (Kassa bo'limidagi bilan bir xil).
    if (txId) {
      setReceiptAuto(true)
      setReceiptTx(txId)
    }
  }

  const handleDelete = (s: Student) => setDeleting(s)

  const doDelete = (reasonId?: string) => {
    const s = deleting
    if (!s) return
    deleteStudent(s.id, reasonId)
      .then(() => {
        setStudents((prev) => prev.filter((x) => x.id !== s.id))
        setArchived((prev) => prev.filter((x) => x.id !== s.id))
        setSelected((prev) => {
          const next = new Set(prev)
          next.delete(s.id)
          return next
        })
        setDeleting(null)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "O'chirib bo'lmadi"))
  }

  /** Bitta o'quvchini arxivga ko'chirish — ReasonPromptModal ochadi. */
  const handleArchive = (s: Student) => {
    setArchiveTarget(s)
  }

  /** Arxiv sababi tanlandi — backend'ga yuborish. */
  const doArchive = (reasonId?: string) => {
    if (!archiveTarget) return
    const s = archiveTarget
    archiveStudent(s.id, undefined, reasonId)
      .then(() => {
        // Faol ro'yxatdan olib tashlab, arxivga qo'shamiz.
        const updated: Student = {
          ...s,
          isArchived: true,
          archivedAt: new Date().toISOString().slice(0, 10),
          archiveReason: reasonId ? undefined : null, // Sabab label backend'dan qaytaradi
        }
        setStudents((prev) => prev.filter((x) => x.id !== s.id))
        setArchived((prev) => [updated, ...prev])
        setSelected((prev) => {
          const next = new Set(prev)
          next.delete(s.id)
          return next
        })
        setArchiveTarget(null)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "Arxivlashda xatolik"))
  }

  /** Tanlanganlarni arxivga ko'chirish — ReasonPromptModal ochadi. */
  const handleArchiveSelected = (reasonId?: string) => {
    const toArchive = selectedStudents
    if (toArchive.length === 0) return
    setArchivingSelected(true)
    Promise.all(toArchive.map((s) => archiveStudent(s.id, undefined, reasonId)))
      .then(() => {
        // Barcha tanlanganlarni arxivga ko'chirish
        const now = new Date().toISOString().slice(0, 10)
        const archived = toArchive.map((s) => ({
          ...s,
          isArchived: true,
          archivedAt: now,
          archiveReason: reasonId ? undefined : null,
        }))
        setStudents((prev) => prev.filter((s) => !toArchive.some((x) => x.id === s.id)))
        setArchived((prev) => [...archived, ...prev])
        setSelected(new Set())
        setArchiveReasonModal(false)
      })
      .catch((e) => alert(e?.response?.data?.message ?? "Arxivlashda xatolik"))
      .finally(() => setArchivingSelected(false))
  }
  /** Login'ni cheklash/ochish — tasdiq bilan. */
  const handleToggleLoginBlock = (s: Student) => {
    const nextBlocked = !s.loginBlocked
    const msg = nextBlocked
      ? `"${s.fullName}" tizimga kira olmaydi. Login cheklansinmi?`
      : `"${s.fullName}" uchun login cheklovi olib tashlansinmi?`
    if (!confirm(msg)) return
    setStudentLoginBlock(s.id, nextBlocked)
      .then(() => {
        setStudents((prev) =>
          prev.map((x) => (x.id === s.id ? { ...x, loginBlocked: nextBlocked } : x)),
        )
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Amalni bajarib bo\'lmadi'))
  }

  /** Tanlangan o'quvchilarni ommaviy login cheklash/ochish — tasdiq bilan. */
  const handleBulkLoginBlock = (blocked: boolean) => {
    const ids = selectedStudents.map((s) => s.id)
    if (ids.length === 0) return
    const msg = blocked
      ? `${ids.length} ta o'quvchi login'i cheklansinmi? Ular tizimga kira olmaydi.`
      : `${ids.length} ta o'quvchi uchun login cheklovi olib tashlansinmi?`
    if (!confirm(msg)) return
    setBulkLoginBlocking(true)
    setStudentLoginBlockBulk(ids, blocked)
      .then(() => {
        setStudents((prev) =>
          prev.map((x) => (selected.has(x.id) ? { ...x, loginBlocked: blocked } : x)),
        )
        clearSelection()
      })
      .catch((e) => alert(e?.response?.data?.message ?? 'Amalni bajarib bo\'lmadi'))
      .finally(() => setBulkLoginBlocking(false))
  }

  /**
   * Tanlanganlarni BIR PAYTDA muzlatish yoki aktivlashtirish.
   *
   * ⚠️ Bu sahifada GURUH tanlanmaydi — shuning uchun `groupId = null`, ya'ni amal o'quvchining
   * BARCHA guruhlardagi a'zoligiga tegadi (foydalanuvchi tasdiq modalida shu haqda ochiq
   * ogohlantiriladi). Serverning o'zi holat bo'yicha filtrlaydi: muzlatishda faqat
   * `active`/`trial`, aktivlashtirishda faqat aktiv BO'LMAGAN a'zoliklar olinadi, mos
   * kelmagani jimgina o'tkazib yuboriladi — ya'ni bitta noto'g'ri tanlangan o'quvchi tufayli
   * butun amal to'xtab qolmaydi. Nima bo'lgani `bulkMembershipSummary` da yoziladi.
   */
  const handleBulkMembership = (reasonId?: string, date?: string, retentionBonus?: boolean) => {
    const action = bulkMembership
    if (!action) return
    const ids = selectedStudents.map((s) => s.id)
    if (ids.length === 0) {
      setBulkMembership(null)
      return
    }
    // Sana modaldan keladi (`showDate`); zaxira sifatida bugungi kun — server sanasiz ishlamaydi.
    const day = date || new Date().toISOString().slice(0, 10)
    setMembershipBusy(true)
    // «Aktiv muzlatish» — AYNAN o'sha endpoint, farqi faqat `yearFreeze` bayrog'ida
    // (hisob-kitob, holat va natija xabari oddiy muzlatish bilan bir xil).
    const freezing = action === 'freeze' || action === 'yearFreeze'
    const request = freezing
      ? bulkFreezeMembers(null, ids, day, reasonId, action === 'yearFreeze')
      : bulkActivateMembers(null, ids, day, retentionBonus)
    request
      .then(async (res) => {
        alert(bulkMembershipSummary(res, freezing ? 'freeze' : 'activate'))
        setBulkMembership(null)
        clearSelection()
        // Ro'yxatni QAYTA yuklaymiz: a'zolik holati ham, BALANS ham o'zgargan bo'lishi mumkin
        // (muzlatishda summa qaytariladi, aktivlashtirishda qisman oy yoziladi) — buni lokal
        // yamoq bilan to'g'ri ko'rsatib bo'lmaydi.
        setStudents(await getStudents())
      })
      .catch((e) => {
        // Modalni YOPAMIZ: uning ichki `submitting` bayrog'i qaytmaydi, ya'ni ochiq qoldirsak
        // tasdiq tugmasi abadiy o'chiq bo'lib qolardi. Tanlov saqlanadi — qayta urinish mumkin.
        setBulkMembership(null)
        alert(apiErrorMessage(e, 'Amalni bajarib bo\'lmadi'))
      })
      .finally(() => setMembershipBusy(false))
  }

  /** Arxivdan qaytarish. */
  const handleRestore = (s: Student) => {
    if (!confirm(`"${s.fullName}" o'quvchini arxivdan qaytarish? Login bloklangicha qoladi — keyin parol generatsiya qiling.`)) return
    restoreStudent(s.id).then(() => {
      const updated: Student = { ...s, isArchived: false, archivedAt: null, archiveReason: null }
      setArchived((prev) => prev.filter((x) => x.id !== s.id))
      setStudents((prev) => [updated, ...prev])
    })
  }

  return (
    <div className="space-y-5">
      <PageHeader
        title="O'quvchilar"
        sub={
          tab === 'active'
            ? `Faol: ${students.length} ta · Arxivda: ${archived.length} ta`
            : `Arxivda: ${archived.length} ta o'quvchi`
        }
        actions={
          <>
            {/* Faol/Arxiv tab toggle */}
            <div className="tabs">
              <button
                type="button"
                onClick={() => {
                  setTab('active')
                  clearSelection()
                }}
                className={cn('tab', tab === 'active' && 'active')}
              >
                Faol
              </button>
              <button
                type="button"
                onClick={() => {
                  setTab('archived')
                  clearSelection()
                }}
                className={cn('tab', tab === 'archived' && 'active')}
              >
                <Archive className="mr-1 inline h-4 w-4" />
                Arxiv ({archived.length})
              </button>
            </div>
            {/* Faqat superadmin: barcha o'quvchilarni login/parol bilan Excel'ga yuklab olish.
                Parol faqat foydalanuvchi hali kirmagan bo'lsa ko'rinadi. */}
            {user?.role === 'superadmin' && (
              <Button variant="secondary" onClick={() => downloadStudentCredentials()}>
                <Download className="h-4 w-4" /> Login/parollar
              </Button>
            )}
            {tab === 'active' && (
              <>
                <Button
                  variant="secondary"
                  onClick={() => downloadStudentImportTemplate()}
                  title="Guruh ixtiyoriy (bo'lsa qo'shiladi, bo'lmasa faqat yaratiladi)"
                >
                  <FileDown className="h-4 w-4" /> Shablon
                </Button>
                <Button
                  variant="secondary"
                  disabled={importing}
                  onClick={() => fileInputRef.current?.click()}
                >
                  <Upload className="h-4 w-4" /> {importing ? 'Yuklanmoqda…' : 'Excel yuklash'}
                </Button>
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".xlsx"
                  className="hidden"
                  onChange={handleImportFile}
                />
                {can('students.list', 'create') && (
                  <Button
                    onClick={() => {
                      setEditing(null)
                      setFormOpen(true)
                    }}
                  >
                    <Plus className="h-4 w-4" /> Yangi qo'shish
                  </Button>
                )}
              </>
            )}
          </>
        }
      />

      {/* Filtrlar — toolbar */}
      <div className="toolbar">
        <div className="left">
          <div className="search-inline">
            <Search className="h-4 w-4 shrink-0 text-slate-400" />
            <input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="F.I.SH yoki ota-ona bo'yicha qidirish..."
            />
          </div>
          <select
            value={classFilter}
            onChange={(e) => setClassFilter(e.target.value)}
            className={control}
          >
            <option value="all">Barcha guruhlar</option>
            {classNames.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
            {archivedClassNames.length > 0 && (
              <optgroup label="Tugatilgan guruhlar">
                {archivedClassNames.map((c) => (
                  <option key={`arch-${c}`} value={c}>
                    {c}
                  </option>
                ))}
              </optgroup>
            )}
          </select>
          <select
            value={teacherFilter}
            onChange={(e) => setTeacherFilter(e.target.value)}
            className={control}
          >
            <option value="all">Barcha o'qituvchilar</option>
            {teachers
              .filter((t) => Object.values(groupTeachers).includes(t.id))
              .sort((a, b) => a.fullName.localeCompare(b.fullName))
              .map((t) => (
                <option key={t.id} value={t.id}>
                  {t.fullName}
                </option>
              ))}
          </select>
          <select
            value={genderFilter}
            onChange={(e) => setGenderFilter(e.target.value as 'all' | Gender)}
            className={control}
          >
            <option value="all">Barcha jinslar</option>
            <option value="male">{genderLabels.male}</option>
            <option value="female">{genderLabels.female}</option>
          </select>
          <select
            value={balanceFilter}
            onChange={(e) => {
              const v = e.target.value as BalanceFilter
              setBalanceFilter(v)
              // "Qarzsizlar" + qarz oralig'i = mantiqan bo'sh kesishma; oraliq tozalanadi
              // (Muddat/Holat filtrlari bir-birini tozalagani bilan bir xil qoida).
              if (v === 'paid') {
                setDebtMin('')
                setDebtMax('')
              }
            }}
            className={control}
          >
            <option value="all">Barcha balans</option>
            <option value="debt">Qarzdorlar</option>
            <option value="paid">Qarzsizlar</option>
          </select>
          {/* QARZ SUMMASI oralig'i — "shu summadan baland / past qarzdorlarni ajratish".
              Ikkalasi ham ixtiyoriy: faqat "dan" — undan kattalar, faqat "gacha" — kichiklar. */}
          <div
            className={cn(
              'flex items-center gap-1.5 rounded-lg border bg-white px-2.5 py-1.5 text-sm transition-colors',
              debtRangeInvalid
                ? 'border-red-300 bg-red-50'
                : debtRangeOn
                  ? 'border-brand-400'
                  : 'border-slate-200',
            )}
            title="Qarz summasi bo'yicha: 'dan' — shu summadan baland qarzi borlar, 'gacha' — shu summadan past. Chegara qo'yilsa faqat QARZDORLAR ko'rsatiladi. Strelka (▲▼) bilan 50 000 dan oshadi/kamayadi."
          >
            <Wallet className="h-4 w-4 shrink-0 text-slate-400" />
            <span className="shrink-0 text-xs font-medium text-slate-500">Qarz:</span>
            <input
              type="number"
              min={0}
              step={DEBT_STEP}
              inputMode="numeric"
              value={debtMin}
              onChange={(e) => {
                setDebtMin(e.target.value)
                // Oraliq faqat qarzdorlarga tegishli — "Qarzsizlar" tanlangan bo'lsa ro'yxat
                // jimgina bo'sh qolardi.
                if (e.target.value.trim() !== '' && balanceFilter === 'paid') {
                  setBalanceFilter('debt')
                }
              }}
              placeholder="dan"
              className="w-24 rounded-md border-0 bg-transparent px-1 py-0.5 font-mono text-sm text-slate-700 outline-none placeholder:font-sans placeholder:text-slate-400"
            />
            <span className="text-slate-300">—</span>
            <input
              type="number"
              min={0}
              step={DEBT_STEP}
              inputMode="numeric"
              value={debtMax}
              onChange={(e) => {
                setDebtMax(e.target.value)
                if (e.target.value.trim() !== '' && balanceFilter === 'paid') {
                  setBalanceFilter('debt')
                }
              }}
              placeholder="gacha"
              className="w-24 rounded-md border-0 bg-transparent px-1 py-0.5 font-mono text-sm text-slate-700 outline-none placeholder:font-sans placeholder:text-slate-400"
            />
            {debtRangeOn && (
              <button
                type="button"
                onClick={() => {
                  setDebtMin('')
                  setDebtMax('')
                }}
                className="shrink-0 text-slate-400 transition-colors hover:text-slate-600"
                title="Qarz oralig'ini tozalash"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            )}
          </div>
          {debtRangeInvalid && (
            <span className="self-center text-xs font-medium text-red-600">
              «dan» «gacha»dan katta
            </span>
          )}
          {/* Holat filtri — variantlar "Holat" USTUNIDAGI yorliqlar bilan bir xil.
              Qavsdagi son joriy ro'yxatdagi (faol/arxiv tabi) miqdor: filtrni tanlashdan
              OLDIN nechta odam chiqishini ko'rsatadi. */}
          <select
            value={activeFilter}
            onChange={(e) => setActiveFilter(e.target.value as StateFilter)}
            className={control}
          >
            <option value="all">Barcha holat ({source.length})</option>
            <option value="active">● Aktiv ({stateCounts.active})</option>
            <option value="trial">● Sinovda ({stateCounts.trial})</option>
            {/* «Aktiv muzlatilgan» — oddiy muzlatishdan ALOHIDA variant: aynan shu son
                "yangi o'quv yiliga nechta o'quvchi bilan o'tyapmiz" degan savolga javob beradi.
                Amalni faqat superadmin qiladi, KO'RISH esa hammaga ochiq — belgi qatorda
                baribir turibdi, filtrni yashirish faqat sanashni qiyinlashtirardi. */}
            <option value="yearFrozen">● Aktiv muzlatilgan ({stateCounts.yearFrozen})</option>
            <option value="frozen">● Muzlatilgan ({stateCounts.frozen})</option>
            <option value="inactive">Aktiv emas ({stateCounts.inactive})</option>
          </select>
          <select
            value={districtFilter}
            onChange={(e) => {
              // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi (boshqa tumanga tegishli edi).
              setDistrictFilter(e.target.value)
              setSchoolFilter('all')
            }}
            className={control}
          >
            <option value="all">Barcha tumanlar</option>
            {districts.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}
              </option>
            ))}
          </select>
          <select
            value={schoolFilter}
            onChange={(e) => setSchoolFilter(e.target.value)}
            disabled={districtFilter === 'all'}
            className={cn(control, districtFilter === 'all' && 'opacity-50')}
          >
            <option value="all">
              {districtFilter === 'all' ? 'Barcha maktablar' : '— tanlanmagan —'}
            </option>
            {(districts.find((d) => d.id === districtFilter)?.schools ?? []).map((sc) => (
              <option key={sc.id} value={sc.id}>
                {sc.name}
              </option>
            ))}
          </select>
          <select
            value={photoFilter}
            onChange={(e) => setPhotoFilter(e.target.value as PhotoFilter)}
            className={control}
            title="O'quvchi surati bo'yicha filtr"
          >
            <option value="all">Barcha suratlar</option>
            <option value="with">Rasmi bor</option>
            <option value="without">Rasmi yo'q</option>
          </select>
          <button
            type="button"
            onClick={() => setBirthdayToday((v) => !v)}
            className={cn(
              'inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
              birthdayToday
                ? 'border-brand-400 bg-brand-50 text-brand-700'
                : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50',
            )}
            title="Bugun tug'ilgan kuni bor o'quvchilar"
          >
            <Cake className="h-4 w-4" /> Bugun tug'ilgan kun
          </button>
          <select
            value={sort}
            onChange={(e) => setSort(e.target.value as SortOption)}
            className={control}
            title="Saralash"
          >
            <option value="default">Saralash: standart</option>
            <option value="ball-desc">Ball: yuqoridan</option>
            <option value="ball-asc">Ball: pastdan</option>
          </select>
          {filtersActive && (
            <button
              type="button"
              onClick={clearFilters}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-700"
              title="Barcha filtrlarni tozalash"
            >
              <X className="h-4 w-4" /> Filtrni tozalash
            </button>
          )}
        </div>
        <div className="right">
          {/* Qarz bo'yicha filtrlanganda "nechta odam" yetarli emas — "jami qancha pul" kerak
              (masalan 1 mln dan baland qarzdorlarni ajratib, umumiy summani ko'rish uchun).
              Summa EKRANDAGI (filtrlangan) butun ro'yxat bo'yicha, sahifa bo'yicha emas. */}
          {(debtRangeOn || balanceFilter === 'debt') && filteredDebtTotal > 0 && (
            <span className="mr-3 text-sm text-slate-500">
              Jami qarz:{' '}
              <span className="font-mono font-semibold text-red-600">
                {formatMoney(filteredDebtTotal)}
              </span>
            </span>
          )}
          <span className="text-sm text-slate-400">{filtered.length} ta</span>
        </div>
      </div>

      <Card tight>
        {/* Tanlanganlar uchun amal paneli */}
        {selected.size > 0 && (
          <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 bg-brand-50/60 px-4 py-3">
            <span className="text-sm font-semibold text-brand-700">
              {selected.size} ta tanlandi
            </span>
            <Button variant="secondary" onClick={() => setSmsRecipients(selectedStudents)}>
              <Send className="h-4 w-4" /> SMS yuborish
            </Button>
            {/* Tanlanganlarni bog'lanish navbatiga qo'shish — sabab bir marta tanlanadi. */}
            {can('contacts', 'create') && (
              <Button variant="secondary" onClick={() => setContactOpen(true)}>
                <PhoneCall className="h-4 w-4" /> Bog'lanish kerak ({selected.size})
              </Button>
            )}
            <Button variant="secondary" onClick={handleExportExcel} disabled={exportingSelected}>
              {exportingSelected ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <FileDown className="h-4 w-4" />
              )}
              Yuklab olish (Excel)
            </Button>
            <Button variant="secondary" onClick={handleExport}>
              <Download className="h-4 w-4" /> CSV
            </Button>
            {tab === 'active' && (
              <>
                <Button
                  variant="secondary"
                  onClick={() => handleBulkLoginBlock(true)}
                  disabled={bulkLoginBlocking}
                  className="text-red-600 hover:text-red-700"
                >
                  <Lock className="h-4 w-4" /> Bloklash ({selected.size})
                </Button>
                <Button
                  variant="secondary"
                  onClick={() => handleBulkLoginBlock(false)}
                  disabled={bulkLoginBlocking}
                  className="text-emerald-600 hover:text-emerald-700"
                >
                  <LockOpen className="h-4 w-4" /> Blokdan chiqarish ({selected.size})
                </Button>
                {/*
                  Ommaviy muzlatish/aktivlashtirish — a'zolik holatini o'zgartirish GURUH amali,
                  shuning uchun darvoza `classes.list` (o'quvchilar ruxsati emas).
                */}
                {can('classes.list', 'create') && (
                  <>
                    <Button
                      variant="secondary"
                      onClick={() => setBulkMembership('freeze')}
                      disabled={membershipBusy}
                      className="text-sky-600 hover:text-sky-700"
                    >
                      <Snowflake className="h-4 w-4" /> Muzlatish ({selected.size})
                    </Button>
                    {/* «Aktiv muzlatish» — yangi o'quv yiliga o'tish belgisi bilan muzlatish.
                        FAQAT superadmin: `can()` bu yerda yaramaydi — u oddiy admin uchun ham
                        true qaytaradi (`students.list` naqshi), amal esa admin'ga yopiq. */}
                    {user?.role === 'superadmin' && (
                      <Button
                        variant="secondary"
                        onClick={() => setBulkMembership('yearFreeze')}
                        disabled={membershipBusy}
                        className="text-indigo-600 hover:text-indigo-700"
                      >
                        <CalendarClock className="h-4 w-4" /> Aktiv muzlatish ({selected.size})
                      </Button>
                    )}
                    <Button
                      variant="secondary"
                      onClick={() => setBulkMembership('activate')}
                      disabled={membershipBusy}
                      className="text-emerald-600 hover:text-emerald-700"
                    >
                      <CheckCircle2 className="h-4 w-4" /> Aktivlashtirish ({selected.size})
                    </Button>
                  </>
                )}
              </>
            )}
            {can('students.list', 'delete') && (
              <Button
                variant="secondary"
                onClick={() => setArchiveReasonModal(true)}
                disabled={archivingSelected}
                className="text-red-600 hover:text-red-700"
              >
                <Archive className="h-4 w-4" />
                {archivingSelected ? "Arxivga ko'chirilyapti…" : "Arxivga ko'chirish"}
              </Button>
            )}
            <button
              onClick={clearSelection}
              className="ml-auto inline-flex items-center gap-1 text-sm text-slate-500 hover:text-slate-700"
            >
              <X className="h-4 w-4" /> Bekor qilish
            </button>
          </div>
        )}

        {/* Jadval */}
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="w-10">
                    <input
                      ref={headerCbRef}
                      type="checkbox"
                      checked={allSelected}
                      onChange={toggleAll}
                      className="h-4 w-4 accent-brand-600"
                    />
                  </th>
                  <th className="w-10">#</th>
                  <th>F.I.SH</th>
                  <th>Guruh</th>
                  <th>Holat</th>
                  <th>Jinsi</th>
                  <th>Tug'ilgan kun</th>
                  <th>Ota-ona</th>
                  <th>Telefon</th>
                  <th className="num" title="Jurnal baholari + bajarilgan baholash mezonlari">
                    Ball
                  </th>
                  <th className="num">Balans</th>
                  {tab === 'archived' && <th>Arxiv sanasi</th>}
                  {tab === 'archived' && <th>Sabab</th>}
                  <th className="text-right">Amallar</th>
                </tr>
              </thead>
              <tbody>
                {paged.map((s, i) => (
                  <tr
                    key={s.id}
                    onClick={() => openNotebook(s)}
                    title="Shaxsiy daftarni ochish"
                    className="cursor-pointer"
                  >
                    <td onClick={(e) => e.stopPropagation()}>
                      <input
                        type="checkbox"
                        checked={selected.has(s.id)}
                        onChange={() => toggleOne(s.id)}
                        className="h-4 w-4 accent-brand-600"
                      />
                    </td>
                    <td className="font-mono text-slate-400">{(pageClamped - 1) * pageSize + i + 1}</td>
                    <td>
                      <div className="cell-user">
                        {/* Rasm bo'lsa — o'quvchi surati, bo'lmasa harflardan yasalgan avatar. */}
                        {s.birthCertificateUrl ? (
                          <img
                            src={s.birthCertificateUrl}
                            alt=""
                            loading="lazy"
                            className="avatar avatar-lg object-cover"
                          />
                        ) : (
                          <div className="avatar avatar-lg" style={{ background: avatarColor(s.fullName) }}>
                            {initials(s.fullName)}
                          </div>
                        )}
                        <div className="meta">
                          <strong>
                            <Link
                              to={`/admin/students/${s.id}`}
                              onClick={(e) => e.stopPropagation()}
                              className="text-inherit no-underline hover:underline"
                            >
                              {s.fullName}
                            </Link>
                          </strong>
                          <span>{genderLabels[s.gender]}</span>
                        </div>
                      </div>
                    </td>
                    <td>
                      {/* GURUHLAR ustuni: MUZLATILGAN a'zoliklar ko'rsatilmaydi (server `groups`
                          dan chiqarib beradi) — o'quvchi eski guruhida muzlatilib yangisida
                          aktiv bo'lsa, ro'yxatda faqat YANGISI turadi.
                          ISTISNO: hamma a'zoligi muzlatilgan bo'lsa ular xira ko'rsatiladi —
                          aks holda o'quvchi "guruhsiz" bo'lib chiqib, qayerdaligi bilinmasdi. */}
                      {(() => {
                        const shown = s.groups ?? []
                        const frozen = (s.groupStates ?? []).filter((g) => g.status === 'frozen')
                        if (shown.length === 0 && frozen.length > 0) {
                          return (
                            <div className="flex flex-wrap gap-1">
                              {frozen.map((g) => (
                                <Badge key={g.groupId} tone="default">
                                  {g.name} · {g.yearFreeze ? 'aktiv muzlatilgan' : 'muzlatilgan'}
                                </Badge>
                              ))}
                            </div>
                          )
                        }
                        const list = shown.length > 0 ? shown : s.className ? [s.className] : []
                        return (
                          <div className="flex flex-wrap gap-1">
                            {list.map((g, gi) => (
                              <Badge key={gi} tone="violet">
                                {g}
                              </Badge>
                            ))}
                            {list.length === 0 && <span className="text-xs text-slate-300">—</span>}
                          </div>
                        )
                      })()}
                    </td>
                    <td>
                      {(() => {
                        const st = memberStateInfo(s)
                        return (
                          <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium', st.chip)}>
                            <span className={cn('h-1.5 w-1.5 rounded-full', st.dot)} /> {st.label}
                          </span>
                        )
                      })()}
                    </td>
                    <td className="text-slate-600">{genderLabels[s.gender]}</td>
                    <td className="font-mono text-slate-600">{formatDate(s.birthDate)}</td>
                    <td className="text-slate-600">{s.parentFullName}</td>
                    <td className="font-mono text-slate-600">{s.parentPhone}</td>
                    <td className="num">
                      {(ballMap[s.id] ?? 0) > 0 ? (
                        <span className="inline-flex items-center justify-end gap-1 font-mono font-semibold text-brand-700">
                          {top3StudentIds[0] === s.id && <Medal className="h-4 w-4 text-amber-500" />}
                          {top3StudentIds[1] === s.id && <Medal className="h-4 w-4 text-slate-400" />}
                          {top3StudentIds[2] === s.id && <Medal className="h-4 w-4 text-orange-500" />}
                          {ballMap[s.id]}
                        </span>
                      ) : (
                        <span className="text-slate-300">—</span>
                      )}
                    </td>
                    <td
                      className={cn(
                        'num font-semibold',
                        s.balance < 0
                          ? 'text-red-600'
                          : s.balance > 0
                            ? 'text-emerald-600'
                            : 'text-slate-500',
                      )}
                    >
                      {s.balance > 0 ? `+${formatMoney(s.balance)}` : formatMoney(s.balance)}
                    </td>
                    {tab === 'archived' && (
                      <td className="font-mono text-slate-600">{s.archivedAt ? formatDate(s.archivedAt) : '—'}</td>
                    )}
                    {tab === 'archived' && (
                      <td className="max-w-[18rem] truncate text-slate-600" title={s.archiveReason ?? ''}>
                        {s.archiveReason || '—'}
                      </td>
                    )}
                    <td onClick={(e) => e.stopPropagation()}>
                      <div className="flex items-center justify-end gap-0.5">
                        {tab === 'active' ? (
                          <>
                            <IconBtn icon={Wallet} title="To'lov kiritish" onClick={() => setPaying(s)} />
                            <IconBtn icon={History} title="To'lov tarixi" onClick={() => setHistoryOf(s)} />
                            <IconBtn icon={Phone} title="Qo'ng'iroq qilish" onClick={() => setCallStudent(s)} />
                            {can('students.list', 'edit') && (
                              <IconBtn
                                icon={Pencil}
                                title="Tahrirlash"
                                onClick={() => {
                                  setEditing(s)
                                  setFormOpen(true)
                                }}
                              />
                            )}
                            {can('students.list', 'delete') && (
                              <IconBtn icon={Archive} title="Arxivga ko'chirish" onClick={() => handleArchive(s)} />
                            )}
                            <IconBtn
                              icon={s.loginBlocked ? Lock : LockOpen}
                              title={s.loginBlocked ? "Login cheklangan — ochish" : "Login'ni cheklash"}
                              active={s.loginBlocked}
                              onClick={() => handleToggleLoginBlock(s)}
                            />
                          </>
                        ) : (
                          <>
                            {can('students.list', 'edit') && (
                              <IconBtn icon={RotateCcw} title="Arxivdan qaytarish" onClick={() => handleRestore(s)} />
                            )}
                            {can('students.list', 'delete') && (
                              <IconBtn
                                icon={Trash2}
                                title="Butunlay o'chirish"
                                danger
                                onClick={() => handleDelete(s)}
                              />
                            )}
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
                {filtered.length === 0 && (
                  <tr>
                    <td colSpan={tab === 'archived' ? 14 : 12} className="px-4 py-12 text-center text-slate-400">
                      {tab === 'archived' ? 'Arxivda o\'quvchi yo\'q' : 'Hech narsa topilmadi'}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination — sahifa hajmi + sahifa navigatsiyasi */}
        {!loading && filtered.length > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-4 py-3 text-sm">
            <div className="flex items-center gap-2 text-slate-500">
              <span>Sahifada:</span>
              <select
                value={pageSize}
                onChange={(e) => setPageSize(Number(e.target.value))}
                className={cn(control, '!py-1')}
              >
                {[30, 50, 100, 200].map((n) => (
                  <option key={n} value={n}>
                    {n} ta
                  </option>
                ))}
              </select>
              <span className="text-slate-400">
                {rangeFrom}–{rangeTo} / {filtered.length}
              </span>
            </div>
            <div className="flex items-center gap-1">
              <button
                type="button"
                disabled={pageClamped <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                title="Oldingi sahifa"
              >
                <ChevronLeft className="h-4 w-4" />
              </button>
              <span className="min-w-[72px] text-center font-medium text-slate-600">
                {pageClamped} / {totalPages}
              </span>
              <button
                type="button"
                disabled={pageClamped >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                title="Keyingi sahifa"
              >
                <ChevronRight className="h-4 w-4" />
              </button>
            </div>
          </div>
        )}
      </Card>

      {/* Modallar */}
      <StudentFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleFormSubmit}
        initial={editing}
      />
      <StudentViewModal student={viewing} onClose={() => setViewing(null)} />
      <SmsModal
        open={smsRecipients.length > 0}
        onClose={() => setSmsRecipients([])}
        recipients={smsRecipients}
      />

      {/* Tanlangan o'quvchilarni "Bog'lanish kerak" navbatiga qo'shish. Ochiq talabi
          borlar server tomonda chetlab o'tiladi — oyna nechtasi qo'shilganini ko'rsatadi. */}
      <NeedContactModal
        open={contactOpen}
        students={selectedStudents.map((s) => ({ id: s.id, fullName: s.fullName }))}
        onClose={() => setContactOpen(false)}
        onCreated={(res) => {
          if (res.created > 0) clearSelection()
        }}
      />
      <PaymentModal student={paying} onClose={() => setPaying(null)} onSubmit={handlePayment} />

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

      <PaymentHistoryModal
        studentId={historyOf?.id ?? null}
        onClose={() => setHistoryOf(null)}
        onPaid={() => getStudents().then(setStudents).catch(() => {})}
      />
      <CallPickerModal
        open={!!callStudent}
        onClose={() => setCallStudent(null)}
        title={callStudent?.fullName}
        studentId={callStudent?.id}
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

      <ReasonPromptModal
        open={!!deleting}
        category="student_delete"
        title="O'quvchini o'chirish"
        message={deleting ? `"${deleting.fullName}" o'quvchini BUTUNLAY o'chirasizmi? Bu amalni ortga qaytarib bo'lmaydi.` : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDelete}
        onClose={() => setDeleting(null)}
      />

      {/* Excel'dan import natijasi */}
      <Modal
        open={!!importResult}
        onClose={() => setImportResult(null)}
        title="Excel'dan yuklash natijasi"
        size="md"
        footer={<Button onClick={() => setImportResult(null)}>Yopish</Button>}
      >
        {importResult && (
          <div className="space-y-3 text-sm">
            <div className="flex flex-wrap gap-x-5 gap-y-1">
              <span className="text-emerald-700">
                ✓ Qo'shildi: <b>{importResult.created}</b>
              </span>
              {importResult.failed > 0 && (
                <span className="text-red-600">
                  ✗ Xato: <b>{importResult.failed}</b>
                </span>
              )}
              {importResult.skipped > 0 && (
                <span className="text-slate-500">
                  O'tkazib yuborildi (bo'sh): <b>{importResult.skipped}</b>
                </span>
              )}
            </div>

            {importResult.errors.length > 0 && (
              <div className="max-h-72 overflow-auto rounded-lg border border-slate-100">
                <table className="w-full text-left text-sm">
                  <thead className="sticky top-0 bg-slate-50 text-xs uppercase tracking-wide text-slate-400">
                    <tr>
                      <th className="w-16 px-3 py-2">Qator</th>
                      <th className="px-3 py-2">Xato</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {importResult.errors.map((e, i) => (
                      <tr key={i}>
                        <td className="px-3 py-2 text-slate-500">{e.row}</td>
                        <td className="px-3 py-2 text-red-600">{e.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}

            {importResult.created > 0 && (
              <p className="text-slate-500">
                Yangi o'quvchilarning login/parollarini <b>"Login/parollar"</b> tugmasi orqali yuklab olishingiz mumkin.
              </p>
            )}
          </div>
        )}
      </Modal>

      {/* Bitta o'quvchini arxivga ko'chirish — sabab bilan ReasonPromptModal */}
      <ReasonPromptModal
        open={!!archiveTarget}
        category="archive_student"
        title="O'quvchini arxivga ko'chirish"
        message={
          archiveTarget
            ? `"${archiveTarget.fullName}" o'quvchini arxivga ko'chirasiz. Tarixiy ma'lumotlar (jurnal, davomat, to'lovlar) saqlanadi, lekin faol ro'yxatdan yashirinadi, oylik to'lov hisoblanmaydi va login bloklanadi.`
            : undefined
        }
        confirmLabel="Arxivga ko'chirish"
        tone="red"
        onConfirm={doArchive}
        onClose={() => setArchiveTarget(null)}
      />

      {/* Tanlanganlarni arxivga ko'chirish — sabab bilan ReasonPromptModal */}
      <ReasonPromptModal
        open={archiveReasonModal}
        category="archive_student"
        title="Tanlanganlarni arxivga ko'chirish"
        message={`${selectedStudents.length} ta o'quvchini arxivga ko'chirasiz.`}
        confirmLabel="Arxivga ko'chirish"
        tone="red"
        onConfirm={handleArchiveSelected}
        onClose={() => setArchiveReasonModal(false)}
      />

      <Modal
        open={!!discountPrompt}
        onClose={() => setDiscountPrompt(null)}
        title="Chegirmani joriy oyga qo'llash"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => resolveDiscountPrompt(false)}>
              Yo'q — keyingi oydan
            </Button>
            <Button onClick={() => resolveDiscountPrompt(true)}>Ha — joriy oydan</Button>
          </>
        }
      >
        {discountPrompt && (
          <div className="space-y-3 text-sm text-slate-600">
            <p>
              <span className="font-medium text-slate-800">
                {discountPrompt.values.fullName}
              </span>{' '}
              o'quvchisining chegirmasi{' '}
              <span className="font-medium">
                {discountPrompt.oldPct}% / {formatMoney(discountPrompt.oldAmount)}
              </span>{' '}
              →{' '}
              <span className="font-medium">
                {discountPrompt.newPct}% / {formatMoney(discountPrompt.newAmount)}
              </span>{' '}
              ga o'zgardi. Yangi chegirma qachondan qo'llansin?
            </p>
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-500">
              <p>
                <b className="text-slate-700">Ha</b> — joriy oy hisobi yangi chegirma bilan qayta
                hisoblanadi (balans farqqa moslab to'g'rilanadi).
              </p>
              <p className="mt-1">
                <b className="text-slate-700">Yo'q</b> — joriy oy eski hisobda qoladi, yangi
                chegirma keyingi oydan amal qiladi.
              </p>
            </div>
          </div>
        )}
      </Modal>

      {/* Tanlanganlarni arxivga ko'chirish — sabab bilan modali */}
      <ReasonPromptModal
        open={archiveReasonModal}
        category="student_delete"
        title="Tanlanganlarni arxivga ko'chirish"
        message={`${selected.size} ta o'quvchini arxivga ko'chirasiz. Tarixiy ma'lumotlar (jurnal, davomat, to'lovlar) saqlanadi, lekin faol ro'yxatdan yashirinadi va oylik to'lov hisoblanmaydi.`}
        confirmLabel={archivingSelected ? "Arxivga ko'chirilyapti…" : "Arxivga ko'chirish"}
        tone="red"
        onConfirm={handleArchiveSelected}
        onClose={() => {
          if (!archivingSelected) setArchiveReasonModal(false)
        }}
      />

      {/*
        Ommaviy MUZLATISH — sabab va sana bilan.
        ⚠️ `message` da "BARCHA guruhlardagi" deb ochiq yozilgan: bu sahifada guruh tanlanmaydi,
        ya'ni foydalanuvchi qamrovni boshqa hech qayerdan bila olmaydi.
      */}
      <ReasonPromptModal
        open={bulkMembership === 'freeze'}
        category="freeze"
        title="Ommaviy muzlatish"
        message={`${selected.size} ta o'quvchining BARCHA guruhlardagi a'zoligi shu sanadan muzlatiladi.`}
        confirmLabel={membershipBusy ? 'Muzlatilyapti…' : 'Muzlatish'}
        tone="sky"
        showDate
        onConfirm={handleBulkMembership}
        onClose={() => {
          if (!membershipBusy) setBulkMembership(null)
        }}
      />

      {/*
        Ommaviy «AKTIV MUZLATISH» — oddiy muzlatish bilan AYNAN bir xil amal, farqi faqat yangi
        o'quv yili belgisida. Shuning uchun sabab kategoriyasi ham o'sha (`freeze`) — yangi
        kategoriya "bir xil sabablar ikki ro'yxatda" degani bo'lardi.
      */}
      <ReasonPromptModal
        open={bulkMembership === 'yearFreeze'}
        category="freeze"
        title="Ommaviy aktiv muzlatish"
        message={`${selected.size} ta o'quvchining BARCHA guruhlardagi a'zoligi shu sanadan muzlatiladi va «yangi o'quv yiliga o'tish» deb belgilanadi. Hisob-kitob oddiy muzlatish bilan bir xil — belgi faqat ro'yxatda ajratib sanash uchun.`}
        confirmLabel={membershipBusy ? 'Muzlatilyapti…' : 'Aktiv muzlatish'}
        tone="sky"
        showDate
        onConfirm={handleBulkMembership}
        onClose={() => {
          if (!membershipBusy) setBulkMembership(null)
        }}
      />

      {/* Ommaviy AKTIVLASHTIRISH — sinov/muzlatilgan a'zoliklar faolga o'tadi. */}
      <ReasonPromptModal
        open={bulkMembership === 'activate'}
        category="activate"
        title="Ommaviy aktivlashtirish"
        message={`${selected.size} ta o'quvchining BARCHA guruhlardagi a'zoligi shu sanadan aktivlashtiriladi.`}
        confirmLabel={membershipBusy ? 'Aktivlashtirilyapti…' : 'Aktivlashtirish'}
        tone="brand"
        showDate
        onConfirm={handleBulkMembership}
        onClose={() => {
          if (!membershipBusy) setBulkMembership(null)
        }}
      />
    </div>
  )
}

interface IconBtnProps {
  icon: typeof Pencil
  title: string
  onClick: () => void
  danger?: boolean
  /** Doimiy "yoqilgan" holat (masalan login cheklangan) — hover kutmasdan qizil ko'rinadi. */
  active?: boolean
}

function IconBtn({ icon: Icon, title, onClick, danger, active }: IconBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className={cn(
        'rounded-lg p-1.5 transition-colors',
        active
          ? 'bg-red-50 text-red-600 hover:bg-red-100'
          : danger
            ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
            : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
