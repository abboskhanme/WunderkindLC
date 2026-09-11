import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { Plus, Pencil, Trash2, Download, TrendingUp, TrendingDown, Wallet, AlertCircle, Calculator, History, Inbox, Percent, Search, Receipt, Undo2, Banknote, Users, Award } from 'lucide-react'
import type {
  FinanceDirection,
  FinanceMonthly,
  FinanceSummary,
  FinanceTransaction,
  Refund,
  SalaryReportRow,
} from '@/types'
import {
  getFinanceSummary,
  getFinanceMonthly,
  getTransactions,
  createTransaction,
  updateTransaction,
  deleteTransaction,
  accrueTuition,
  getSalaryReport,
  getCourseReport,
  getRefunds,
  getCashiers,
  getPaymentAuthors,
  type FinanceTransactionPayload,
  type CourseFinanceReport,
  type GroupFinanceRow,
  type PaymentAuthor,
} from '@/api/services/finance'
import { addPayment } from '@/api/services/students'
import type { CashierSummary } from '@/api/services/kassa'
import { financeCategoryLabel, financeDirectionLabels, formatMonth, paymentMethodLabel } from '@/config/constants'
import { formatDate, formatTime, formatDateTime, formatMoney, exportToCsv, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { FinanceMonthlyChart } from '@/components/charts/FinanceMonthlyChart'
import { AuditHistoryModal } from '@/components/audit/AuditHistoryModal'
import type { AuditFilters } from '@/api/services/audit'
import { TransactionFormModal } from './TransactionFormModal'
import { TeacherSalaryDetailModal } from './TeacherSalaryDetailModal'
import { GroupPaymentsModal, type PaymentsTarget } from './GroupPaymentsModal'
import { DailyReportCard } from './DailyReportCard'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { PaymentEditModal } from './PaymentEditModal'
import { RefundModal } from './RefundModal'
import { RetentionBonusTab } from './RetentionBonusTab'
import { useAuth } from '@/context/auth-context'
import { usePerm } from '@/lib/permissions'
import { tabFromUrl } from '@/lib/tabParam'

const todayStr = new Date().toISOString().slice(0, 10)
const yearOf = (d: string) => Number(d.slice(0, 4))

/** "2026-02" → "2026-02-28" (oyning HAQIQIY oxirgi kuni — kabisa yili ham to'g'ri). */
const monthEndDate = (month: string) => {
  const [y, m] = month.split('-').map(Number)
  return `${month}-${String(new Date(y, m, 0).getDate()).padStart(2, '0')}`
}

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-mono text-slate-700 outline-none focus:border-brand-400'

/** To'lovlar bo'limidagi filtr tanlovlari (to'lov turi / kvitansiya / o'qituvchi) — bir xil ko'rinish. */
const filterSelect =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

type DirFilter = 'all' | FinanceDirection
/** To'lov usuli filtri — "Amallar" jadvalida kirimlarni naqt/karta/bank bo'yicha ajratish. */
type MethodFilter = 'all' | 'cash' | 'card' | 'bank'
/** To'lovlar bo'limi: kvitansiya raqami kiritilgan / kiritilmagan to'lovlarni ajratish. */
type ReceiptFilter = 'all' | 'with' | 'without'
/** To'lovlar ro'yxatining saralanishi — sana yoki kvitansiya raqami bo'yicha. */
type PaySort = 'date-desc' | 'date-asc' | 'receipt-asc' | 'receipt-desc'
type Tab = 'overview' | 'groups' | 'teachers' | 'payments' | 'refunds' | 'cashiers' | 'bonuses'
const FINANCE_TABS = ['overview', 'groups', 'teachers', 'payments', 'refunds', 'cashiers', 'bonuses'] as const

const paySortOptions: { value: PaySort; label: string }[] = [
  { value: 'date-desc', label: 'Sana: yangi → eski' },
  { value: 'date-asc', label: 'Sana: eski → yangi' },
  { value: 'receipt-asc', label: 'Kvitansiya: 1 → 9' },
  { value: 'receipt-desc', label: 'Kvitansiya: 9 → 1' },
]

/**
 * "Kvitansiya" ustunida ko'rinadigan raqam: NAQD to'lovda qog'oz kvitansiya raqami ("KV000123"),
 * KARTA to'lovida esa karta raqamining oxirgi 4 raqami ("•••• 1234"). Ikkalasi ham yo'q bo'lsa null.
 */
function receiptCell(p: FinanceTransaction): string | null {
  if (p.receiptNo) return p.receiptNo
  return p.cardLast4 ? `•••• ${p.cardLast4}` : null
}

/** Kvitansiya raqamining SON qismi ("KV000123" → 123). Raqam yo'q bo'lsa null.
 *  Matn sifatida solishtirish noto'g'ri bo'lardi: "KV9" > "KV10" chiqib ketardi. */
function receiptNum(receiptNo: string | null | undefined): number | null {
  const digits = (receiptNo ?? '').replace(/\D/g, '')
  return digits.length > 0 ? Number(digits) : null
}

const tabs: { value: Tab; label: string; icon?: LucideIcon }[] = [
  { value: 'overview', label: 'Umumiy' },
  { value: 'groups', label: 'Guruhlar' },
  { value: 'teachers', label: "O'qituvchilar" },
  { value: 'payments', label: "To'lovlar" },
  { value: 'refunds', label: 'Vozvratlar' },
  // Kassirlar — kim qancha pul qabul qilgan (kassa bo'limidan va moliyadan kiritilgan to'lovlar).
  { value: 'cashiers', label: 'Kassirlar' },
  // Bonus — o'quvchini ushlab turish bonuslarining HISOBOTI (faqat o'qish; berish "O'quvchilar"da).
  { value: 'bonuses', label: 'Bonus', icon: Award },
]

/** Qoldiq/qarz summasini belgisiga qarab ranglash */
function balanceClass(v: number): string {
  return v > 0 ? 'text-red-600' : v < 0 ? 'text-emerald-600' : 'text-slate-400'
}

export function FinancePage() {
  // To'lovni tahrirlash — faqat tizim egasi (superadmin); backend ham shuni talab qiladi.
  const { user } = useAuth()
  const isSuper = user?.role === 'superadmin'
  const { can } = usePerm()
  // O'zgarishlar tarixi — alohida `audit` ruxsati (admin/superadmin uchun har doim true).
  const canSeeAudit = can('audit', 'view')
  const navigate = useNavigate()
  // Boshlang'ich tab manzildan ham kelishi mumkin (`?tab=bonuses`) — "Hisobotlar" bo'limi
  // to'g'ridan-to'g'ri kerakli hisobot tabiga havola beradi.
  const [tab, setTab] = useState<Tab>(() => tabFromUrl(FINANCE_TABS, 'overview'))
  const [from, setFrom] = useState(`${yearOf(todayStr)}-01-01`)
  const [to, setTo] = useState(todayStr)
  const [dirFilter, setDirFilter] = useState<DirFilter>('all')
  const [methodFilter, setMethodFilter] = useState<MethodFilter>('all')

  const [summary, setSummary] = useState<FinanceSummary | null>(null)
  const [monthly, setMonthly] = useState<FinanceMonthly[]>([])
  const [transactions, setTransactions] = useState<FinanceTransaction[]>([])
  const [salaryReport, setSalaryReport] = useState<SalaryReportRow[]>([])
  const [payments, setPayments] = useState<FinanceTransaction[]>([])
  const [paySearch, setPaySearch] = useState('')
  /** "To'lovlar" tabidagi o'qituvchi filtri (bo'sh = hammasi) — to'lov guruhi orqali aniqlanadi. */
  const [payTeacher, setPayTeacher] = useState('')
  /** "To'lovlar" tabidagi to'lov usuli filtri (naqd / karta / bank). */
  const [payMethod, setPayMethod] = useState<MethodFilter>('all')
  /** "To'lovlar" tabidagi kvitansiya filtri (raqami bor / yo'q). */
  const [payReceipt, setPayReceipt] = useState<ReceiptFilter>('all')
  /** "To'lovlar" tabidagi "Kiritgan" filtri — tanlangan xodim (null = hammasi). Kalit emas, obyekt
   *  saqlanadi: davr o'zgarib ro'yxatdan tushib qolsa ham tanlov yo'qolmaydi. */
  const [payAuthor, setPayAuthor] = useState<PaymentAuthor | null>(null)
  /** To'lov kirita oladigan xodim/adminlar (filtr ro'yxati uchun). */
  const [payAuthors, setPayAuthors] = useState<PaymentAuthor[]>([])
  /** "To'lovlar" tabidagi saralash (sana / kvitansiya raqami). */
  const [paySort, setPaySort] = useState<PaySort>('date-desc')
  const [courseReport, setCourseReport] = useState<CourseFinanceReport | null>(null)
  const [loading, setLoading] = useState(true)

  const [formOpen, setFormOpen] = useState(false)
  const [editing, setEditing] = useState<FinanceTransaction | null>(null)
  const [audit, setAudit] = useState<{ filters: AuditFilters; title: string } | null>(null)
  const [detailTeacher, setDetailTeacher] = useState<SalaryReportRow | null>(null)
  /** "Kim to'ladi / kim to'lamadi" modali — bitta guruh yoki o'qituvchining barcha guruhlari. */
  const [paymentsTarget, setPaymentsTarget] = useState<PaymentsTarget | null>(null)
  const [deleting, setDeleting] = useState<FinanceTransaction | null>(null)
  // Chek (kvitansiya): qaysi to'lovning cheki ochiq + avtomatik print (to'lov kiritilgandan keyin).
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [receiptAuto, setReceiptAuto] = useState(false)
  /** "To'lovlar" bo'limida tahrirlanayotgan o'quvchi to'lovi (superadmin). */
  const [editPayment, setEditPayment] = useState<FinanceTransaction | null>(null)
  /** Vozvrat (pul qaytarish) qilinayotgan to'lov (superadmin). */
  const [refundTarget, setRefundTarget] = useState<FinanceTransaction | null>(null)
  const [refunds, setRefunds] = useState<Refund[]>([])
  /** "Kassirlar" tabi — davr bo'yicha kim qancha qabul qilgan. */
  const [cashiers, setCashiers] = useState<CashierSummary[]>([])
  /** Davr ICHIDAGI bitta oy ("" = butun davr) — BARCHA bo'limlarga birdek ta'sir qiladi.
   *  Tanlansa so'rovlar shu oy chegaralari bilan ketadi (`rangeFrom`/`rangeTo`). */
  const [selMonth, setSelMonth] = useState('')

  // AMALDAGI ORALIQ — oy tanlangan bo'lsa faqat o'sha oy, aks holda butun davr.
  // Barcha bo'limlar (umumiy, guruhlar, o'qituvchilar, to'lovlar, vozvrat, kassirlar)
  // SHU oraliqdan foydalanadi — oy tanlovi hamma joyda bir xil ishlasin.
  //
  // DIQQAT 1: tanlangan oy DAVR ichida bo'lishi shart. Sanalar o'zgartirilib, oy davrdan
  // chiqib qolsa — tanlov bekor qilinadi (aks holda select bo'sh ko'rinib, panellar esa eski
  // oy ma'lumotini ko'rsatib turardi).
  // DIQQAT 2: oy chegaralari davr bilan QIRQILADI — "davr ichidagi oy" shartnomasi buzilmasin
  // (masalan davr 15-martdan boshlansa, mart tanlansa 1-14 mart tortilib kelmasin).
  const monthInPeriod = !selMonth || (selMonth >= from.slice(0, 7) && selMonth <= to.slice(0, 7))
  const activeMonth = monthInPeriod ? selMonth : ''
  const rangeFrom = activeMonth
    ? (`${activeMonth}-01` < from ? from : `${activeMonth}-01`)
    : from
  const rangeTo = activeMonth
    ? (monthEndDate(activeMonth) > to ? to : monthEndDate(activeMonth))
    : to

  // Davr o'zgarib oy undan chiqib qolsa — tanlovni tozalaymiz (select "Butun davr"ga qaytsin).
  useEffect(() => {
    if (selMonth && !monthInPeriod) setSelMonth('')
  }, [selMonth, monthInPeriod])

  const load = useCallback(() => {
    setLoading(true)
    Promise.all([
      getFinanceSummary(rangeFrom, rangeTo),
      getFinanceMonthly(yearOf(rangeTo)),
      getTransactions({ from: rangeFrom, to: rangeTo, direction: dirFilter === 'all' ? undefined : dirFilter }),
      getSalaryReport(rangeFrom, rangeTo),
      getTransactions({ from: rangeFrom, to: rangeTo, direction: 'income', category: 'tuition' }),
      getCourseReport(rangeFrom, rangeTo),
      getRefunds(rangeFrom, rangeTo),
      getCashiers(rangeFrom, rangeTo),
      getPaymentAuthors(rangeFrom, rangeTo),
    ])
      .then(([s, m, t, sr, pay, cr, rf, csh, authors]) => {
        setSummary(s)
        setMonthly(m)
        setTransactions(t)
        setSalaryReport(sr)
        setPayments(pay)
        setCourseReport(cr)
        setRefunds(rf)
        setCashiers(csh)
        setPayAuthors(authors)
      })
      .finally(() => setLoading(false))
  }, [rangeFrom, rangeTo, dirFilter])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda ma'lumotni qayta yuklash (maqsadli, useAsync bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const handleSubmit = async (values: FinanceTransactionPayload) => {
    // Yangi o'quvchi to'lovi (kirim → o'quvchi to'lovi) — o'quvchi balansini yangilaydigan to'lov
    // mexanizmi orqali (o'quvchilar bo'limidagi to'lov kabi), oddiy xom yozuv emas.
    let newTxId: string | null = null
    if (!editing && values.direction === 'income' && values.category === 'tuition' && values.studentId) {
      newTxId = await addPayment(values.studentId, values.amount, values.month, undefined, undefined, values.method)
    } else if (editing) {
      await updateTransaction(editing.id, values)
    } else {
      const tx = await createTransaction(values)
      if (values.direction === 'income') newTxId = tx.id
    }
    setFormOpen(false)
    setEditing(null)
    load()
    // Yangi kirim (to'lov) bo'lsa — chekni avtomatik ochib, print dialogini chiqaramiz.
    if (newTxId) {
      setReceiptAuto(true)
      setReceiptTx(newTxId)
    }
  }

  /** Mavjud to'lov uchun chekni qayta ochish (avtomatik print yo'q). */
  const openReceipt = (txId: string) => {
    setReceiptAuto(false)
    setReceiptTx(txId)
  }

  const handleDelete = (t: FinanceTransaction) => setDeleting(t)

  const doDelete = (reasonId?: string) => {
    const t = deleting
    if (!t) return
    deleteTransaction(t.id, reasonId)
      .then(() => {
        setDeleting(null)
        load()
      })
      .catch((e) => alert(e?.response?.data?.message ?? "O'chirib bo'lmadi"))
  }

  const handleAccrue = async () => {
    if (!confirm("Hisoblanmagan oylik to'lovlar barcha o'quvchilarga hisoblanadimi?")) return
    const res = await accrueTuition()
    alert(
      res.count > 0
        ? `${res.months.join(', ')} uchun ${res.count} ta o'quvchiga jami ${formatMoney(res.total)} hisoblandi.`
        : "Yangi hisoblanadigan oy yo'q — hammasi hisoblangan.",
    )
    load()
  }

  const handleExport = () => {
    exportToCsv(
      'moliya.csv',
      ['Sana', "Yo'nalish", 'Toifa', "To'lov usuli", 'Izoh', 'Summa'],
      visibleTx.map((t) => [
        formatDate(t.date),
        financeDirectionLabels[t.direction],
        financeCategoryLabel(t.category),
        t.direction === 'income' && t.method ? paymentMethodLabel(t.method) : '',
        t.note ?? '',
        String(t.amount),
      ]),
    )
  }

  const handleExportTeachers = () => {
    exportToCsv(
      'oqituvchilar-maoshi.csv',
      ["O'qituvchi", 'Oylik', 'Jurnal ushlanmasi', "O'tkazib yuborilgan dars", 'Hisoblangan', 'Berilgan', 'Qoldiq'],
      salaryReport.map((r) => [
        r.teacherName,
        String(r.salary),
        String(r.deduction ?? 0),
        String(r.missedLessons ?? 0),
        String(r.expected),
        String(r.totalPaid),
        String(r.remaining),
      ]),
    )
  }

  // To'lovlar bo'limi: o'qituvchi filtri (to'lov guruhi → o'qituvchi) + o'quvchi nomi bo'yicha qidiruv.
  // Guruhga TEGLANMAGAN to'lovni bir o'qituvchiga bog'lab bo'lmaydi (u o'quvchining barcha guruhlari
  // orasida taqsimlanadi) — shuning uchun o'qituvchi tanlanganda ular ro'yxatga tushmaydi.
  const teacherByGroup = (() => {
    const map = new Map<string, string>()
    courseReport?.groups.forEach((g) => map.set(g.groupId, g.teacherId))
    return map
  })()
  // "Kiritgan" filtri: tanlangan xodimning to'lovlari. Yangi yozuvlar akkaunt id'si bo'yicha,
  // ESKI (createdById'siz) yozuvlar esa ism bo'yicha mos keladi — backend'dagi kassir hisoboti
  // bilan bir xil qoida (CashierReport.PaymentsAsync).
  const matchesAuthor = (p: FinanceTransaction) => {
    if (!payAuthor) return true
    if (payAuthor.id && p.createdById === payAuthor.id) return true
    return !p.createdById && (p.createdBy ?? '') === payAuthor.name
  }
  /** Filtr ro'yxati: ruxsati borlar + (tanlangani ro'yxatda bo'lmasa) tanlangan xodim. */
  const authorOptions = payAuthor && !payAuthors.some((a) => a.key === payAuthor.key)
    ? [payAuthor, ...payAuthors]
    : payAuthors
  const filteredPayments = (() => {
    const q = paySearch.trim().toLowerCase()
    return payments.filter((p) => {
      if (payTeacher && (!p.groupId || teacherByGroup.get(p.groupId) !== payTeacher)) return false
      if (!matchesAuthor(p)) return false
      // To'lov usuli (naqd/karta/bank) — usuli belgilanmagan eski yozuvlar filtrga tushmaydi.
      if (payMethod !== 'all' && p.method !== payMethod) return false
      // "Kvitansiya" ustunida raqam bor / yo'q — naqdda kvitansiya, kartada karta oxiri
      // (ustunda nima ko'rinsa, filtr ham shunga qaraydi).
      const hasNumber = !!(p.receiptNo || p.cardLast4)
      if (payReceipt === 'with' && !hasNumber) return false
      if (payReceipt === 'without' && hasNumber) return false
      if (!q) return true
      // Kvitansiya raqami bo'yicha ham qidiriladi: "kv123" ham, faqat "123" ham topsin.
      const receipt = (p.receiptNo ?? '').toLowerCase()
      return (
        (p.studentName ?? '').toLowerCase().includes(q) ||
        (p.groupName ?? '').toLowerCase().includes(q) ||
        (p.note ?? '').toLowerCase().includes(q) ||
        receipt.includes(q) ||
        receipt.replace(/^kv/, '').includes(q) ||
        // Karta to'lovi — oxirgi 4 raqam bo'yicha ham topilsin ("1234").
        (p.cardLast4 ?? '').includes(q) ||
        // Kim kiritgani (kassir) bo'yicha ham.
        (p.createdBy ?? '').toLowerCase().includes(q)
      )
    })
  })()
    // SARALASH: sana yoki kvitansiya raqami bo'yicha (raqami YO'Q to'lovlar har doim OXIRIDA —
    // yo'nalishdan qat'i nazar, aks holda ular ro'yxat boshini egallab, kerakli raqamlar ko'rinmasdi).
    .sort((a, b) => {
      if (paySort === 'date-desc' || paySort === 'date-asc') {
        const cmp = a.date === b.date
          ? (a.createdAt ?? '').localeCompare(b.createdAt ?? '')
          : a.date.localeCompare(b.date)
        return paySort === 'date-asc' ? cmp : -cmp
      }
      const na = receiptNum(a.receiptNo)
      const nb = receiptNum(b.receiptNo)
      if (na === null && nb === null) return -a.date.localeCompare(b.date)
      if (na === null) return 1
      if (nb === null) return -1
      return paySort === 'receipt-asc' ? na - nb : nb - na
    })

  const handleExportPayments = () => {
    exportToCsv(
      'tolovlar.csv',
      ['Sana', "O'quvchi", 'Guruh', 'Oy', "To'lov usuli", 'Kvitansiya', "To'lov vaqti", 'Kiritgan', 'Summa'],
      filteredPayments.map((p) => [
        formatDate(p.date),
        p.studentName ?? '',
        p.groupName ?? '',
        p.month ?? '',
        p.method ? paymentMethodLabel(p.method) : '',
        // Naqdda kvitansiya raqami, kartada karta oxiri (jadvaldagi ustun bilan bir xil).
        p.receiptNo ?? (p.cardLast4 ? `**** ${p.cardLast4}` : ''),
        p.paidTime ?? '',
        p.createdBy ?? '',
        String(p.amount),
      ]),
    )
  }

  const handleExportGroups = () => {
    if (!courseReport) return
    exportToCsv(
      'guruhlar-faollik.csv',
      ['Guruh', 'Kurs', "O'qituvchi", "O'quvchilar", 'Hisoblangan', "Yig'ilgan", "Yig'ilish %", "To'liq to'lagan", 'Billable'],
      courseReport.groups.map((g) => [
        g.groupName,
        g.courseName,
        g.teacherName,
        String(g.studentCount),
        String(g.billed),
        String(g.collected),
        String(g.collectionPct),
        String(g.fullyPaidStudents),
        String(g.billableStudents),
      ]),
    )
  }

  // Tanlangan davrning kalendar oylari (har o'qituvchining hisoblangan oyi boshlanish oyiga
  // qarab farq qilishi mumkin — bu faqat davr uzunligini ko'rsatadi).
  const periodMonths = (() => {
    const [fy, fm] = rangeFrom.slice(0, 7).split('-').map(Number)
    const [ty, tm] = rangeTo.slice(0, 7).split('-').map(Number)
    return Math.max(1, (ty - fy) * 12 + (tm - fm) + 1)
  })()

  // Tanlov ro'yxati DAVR (from..to) bo'yicha quriladi — oy tanlangach ro'yxat qisqarib
  // qolmasligi uchun `periodMonths` (amaldagi oraliq) EMAS, davr uzunligi ishlatiladi.
  const periodMonthList = (() => {
    const out: string[] = []
    const [fy, fm] = from.slice(0, 7).split('-').map(Number)
    const [ty, tm] = to.slice(0, 7).split('-').map(Number)
    const total = Math.max(1, (ty - fy) * 12 + (tm - fm) + 1)
    let y = fy
    let m = fm
    for (let i = 0; i < total && i < 240; i++) {
      out.push(`${y}-${String(m).padStart(2, '0')}`)
      m += 1
      if (m > 12) { m = 1; y += 1 }
    }
    return out
  })()



  const teacherTotals = {
    expected: salaryReport.reduce((a, r) => a + r.expected, 0),
    paid: salaryReport.reduce((a, r) => a + r.totalPaid, 0),
    remaining: salaryReport.reduce((a, r) => a + Math.max(0, r.remaining), 0),
  }
  // Maosh jurnalga bog'langan bo'lsa (Guruhlar → Jurnal boshqaruvi) — "Ushlanma" ustuni ko'rsatiladi.
  const anyDeduction = salaryReport.some((r) => (r.deduction ?? 0) > 0)
  const paymentsTotal = filteredPayments.reduce((a, p) => a + p.amount, 0)
  const paymentsCashTotal = filteredPayments.reduce((a, p) => a + (p.method === 'cash' ? p.amount : 0), 0)

  // To'lov usuli (naqt/karta/bank) bo'yicha KIRIM summalari — "Amallar" jadvalidagi filtr uchun.
  // Usul faqat kirimga (income) taalluqli; chiqim/eski (method yo'q) yozuvlar hisoblanmaydi.
  const incomeByMethod: Record<'cash' | 'card' | 'bank', number> = { cash: 0, card: 0, bank: 0 }
  for (const t of transactions) {
    if (t.direction === 'income' && (t.method === 'cash' || t.method === 'card' || t.method === 'bank')) {
      incomeByMethod[t.method] += t.amount
    }
  }
  const incomeMethodTotal = incomeByMethod.cash + incomeByMethod.card + incomeByMethod.bank
  // Tanlangan usul bo'yicha ko'rinadigan amallar (usul tanlansa — faqat o'sha usuldagi kirimlar).
  const visibleTx =
    methodFilter === 'all'
      ? transactions
      : transactions.filter((t) => t.direction === 'income' && t.method === methodFilter)

  // To'lovlar bo'limidagi usul kesimidagi summalar (filtr chiplarida ko'rsatiladi). O'qituvchi/
  // qidiruv/kvitansiya filtrlari QO'LLANGAN ro'yxatdan sanaladi — usul chiplari faqat o'zini filtrlaydi.
  const paymentsByMethod: Record<'cash' | 'card' | 'bank', number> = { cash: 0, card: 0, bank: 0 }
  for (const p of payments) {
    if (p.method === 'cash' || p.method === 'card' || p.method === 'bank') paymentsByMethod[p.method] += p.amount
  }

  // Jadval sahifalash — uzun ro'yxatlar brauzerni cho'ktirmasin (20/30/50/100 talik).
  const txPg = usePagination(visibleTx)
  const payPg = usePagination(filteredPayments)
  const refundPg = usePagination(refunds)
  const salaryPg = usePagination(salaryReport)

  return (
    <div>
      <PageHeader
        title="Moliya"
        sub="Markaz kirim-chiqimlari va hisobotlar"
        actions={
          <>
            {can('audit', 'view') && (
              <Button
                variant="secondary"
                onClick={() => setAudit({ filters: {}, title: "Moliya o'zgarishlar tarixi" })}
              >
                <History className="h-4 w-4" /> Tarix
              </Button>
            )}
            <Button variant="secondary" onClick={handleAccrue}>
              <Calculator className="h-4 w-4" /> Oylik to'lovni hisoblash
            </Button>
            {can('finance.main', 'create') && (
              <Button
                onClick={() => {
                  setEditing(null)
                  setFormOpen(true)
                }}
              >
                <Plus className="h-4 w-4" /> Yangi amal
              </Button>
            )}
          </>
        }
      />

      {/* Bo'limlar (sub-tablar) */}
      <div className="subnav">
        {tabs.map((t) => (
          <button
            key={t.value}
            onClick={() => setTab(t.value)}
            className={cn('subnav-tab', tab === t.value && 'active')}
          >
            {t.icon && <t.icon className="mr-1 h-3.5 w-3.5" />}
            {t.label}
          </button>
        ))}
      </div>

      {/* Davr tanlash (barcha bo'limlar uchun). "Bonus" bo'limi ATAYIN chetda — u OY bo'yicha
          ishlaydi va o'z davr tanlovi bor (ikkita davr bir vaqtda ko'rinmasin). */}
      {tab !== 'bonuses' && (
        <div className="toolbar">
          <span className="text-sm font-medium text-slate-600">Davr:</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
          <span className="text-slate-400">—</span>
          <input type="date" value={to} onChange={(e) => setTo(e.target.value)} className={control} />

          {/* Davr ICHIDAGI bitta oy — barcha bo'limlarga birdek qo'llanadi (guruhlar,
              o'qituvchilar, to'lovlar, kassirlar, vozvrat, umumiy). */}
          <span className="ml-2 text-sm font-medium text-slate-600">Oy:</span>
          <select
            value={selMonth}
            onChange={(e) => setSelMonth(e.target.value)}
            title="Davr ichidagi bitta oyni tanlang"
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">Butun davr ({periodMonthList.length} oy)</option>
            {periodMonthList.map((m) => (
              <option key={m} value={m}>
                {formatMonth(m)}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* ============ BONUS (o'quvchini ushlab turish bonuslari hisoboti) ============
          Sahifaning umumiy yuklanishidan MUSTAQIL: ma'lumot faqat shu tab ochilganda so'raladi. */}
      {tab === 'bonuses' && <RetentionBonusTab />}

      {tab === 'bonuses' ? null : loading || !summary ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          {/* ============ UMUMIY ============ */}
          {tab === 'overview' && (
            <div className="space-y-6">
              {/* Kunlik hisobot — oy kalendar qatori, kun bosilsa shu kunlik kirim/chiqim */}
              <DailyReportCard key={rangeTo.slice(0, 7)} initialMonth={rangeTo.slice(0, 7)} />

              <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
                <StatCard
                  label="Umumiy kirim"
                  value={formatMoney(summary.totalIncome)}
                  icon={TrendingUp}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                  hint={`O'quvchi to'lovi: ${formatMoney(summary.tuitionIncome)}`}
                />
                <StatCard
                  label="Umumiy chiqim"
                  value={formatMoney(summary.totalExpense)}
                  icon={TrendingDown}
                  iconBg="bg-red-50"
                  iconColor="text-red-600"
                />
                <StatCard
                  label="Sof balans"
                  value={formatMoney(summary.net)}
                  icon={Wallet}
                  iconBg={summary.net >= 0 ? 'bg-emerald-50' : 'bg-red-50'}
                  iconColor={summary.net >= 0 ? 'text-emerald-600' : 'text-red-600'}
                  hint="Kirim − Chiqim"
                />
                <StatCard
                  label="O'quvchilar qarzi"
                  value={formatMoney(summary.studentDebt)}
                  icon={AlertCircle}
                  iconBg="bg-amber-50"
                  iconColor="text-amber-600"
                  hint={`Avans: ${formatMoney(summary.studentAdvance)}`}
                />
              </div>

              <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
                <Card
                  className="xl:col-span-2"
                  title={`Oylik kirim/chiqim (${yearOf(rangeTo)})`}
                >
                  <FinanceMonthlyChart data={monthly} />
                </Card>

                <Card title="Toifalar bo'yicha">
                  <CategoryList title="Kirim" items={summary.incomeByCategory} positive />
                  <div className="my-3 border-t border-slate-100" />
                  <CategoryList title="Chiqim" items={summary.expenseByCategory} positive={false} />
                </Card>
              </div>

              {/* Amallar jadvali */}
              <Card
                tight
                title="Amallar"
                actions={
                  <>
                    <div className="toolbar !mb-0">
                      {(['all', 'income', 'expense'] as DirFilter[]).map((d) => (
                        <button
                          key={d}
                          onClick={() => setDirFilter(d)}
                          className={cn('filter-chip', dirFilter === d && 'active')}
                        >
                          {d === 'all' ? 'Barchasi' : financeDirectionLabels[d]}
                        </button>
                      ))}
                    </div>
                    {/* To'lov usuli (naqt/karta/bank) — tanlanganda o'sha usuldagi kirimlar + summasi */}
                    <div className="toolbar !mb-0">
                      {(['all', 'cash', 'card', 'bank'] as MethodFilter[]).map((m) => (
                        <button
                          key={m}
                          onClick={() => setMethodFilter(m)}
                          className={cn('filter-chip', methodFilter === m && 'active')}
                        >
                          {m === 'all'
                            ? "Usul: barchasi"
                            : `${paymentMethodLabel(m)} · ${formatMoney(incomeByMethod[m])}`}
                        </button>
                      ))}
                    </div>
                    <Button variant="secondary" onClick={handleExport} disabled={visibleTx.length === 0}>
                      <Download className="h-4 w-4" /> CSV
                    </Button>
                  </>
                }
              >
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Sana</th>
                        <th>Yo'nalish</th>
                        <th>Toifa</th>
                        <th>To'lov usuli</th>
                        <th>Izoh</th>
                        <th className="num">Summa</th>
                        <th className="num">Amallar</th>
                      </tr>
                    </thead>
                    <tbody>
                      {txPg.paged.map((t) => (
                        <tr key={t.id}>
                          <td className="font-mono text-[12.5px] text-slate-500">
                            {formatDate(t.date)}
                            {t.createdAt && formatTime(t.createdAt) && (
                              <span className="ml-1 text-slate-400">{formatTime(t.createdAt)}</span>
                            )}
                          </td>
                          <td>
                            <Badge tone={t.direction === 'income' ? 'green' : 'red'}>
                              {financeDirectionLabels[t.direction]}
                            </Badge>
                          </td>
                          <td className="text-slate-600">{financeCategoryLabel(t.category)}</td>
                          <td>
                            {t.direction === 'income' && t.method ? (
                              <Badge tone="blue">{paymentMethodLabel(t.method)}</Badge>
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                          </td>
                          <td className="text-slate-500">{t.note ?? '—'}</td>
                          <td
                            className={cn(
                              'num font-semibold',
                              t.direction === 'income' ? 'text-emerald-600' : 'text-red-600',
                            )}
                          >
                            {t.direction === 'income' ? '+' : '−'}
                            {formatMoney(t.amount)}
                          </td>
                          <td className="num">
                            <div className="flex items-center justify-end gap-0.5">
                              {t.direction === 'income' && (
                                <button
                                  type="button"
                                  title="Chek (kvitansiya)"
                                  onClick={() => openReceipt(t.id)}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                                >
                                  <Receipt className="h-4 w-4" />
                                </button>
                              )}
                              {canSeeAudit && (
                                <button
                                  type="button"
                                  title="O'zgarishlar tarixi"
                                  onClick={() =>
                                    setAudit({
                                      filters: { entityType: 'FinanceTransaction', entityId: t.id },
                                      title: 'Amal tarixi',
                                    })
                                  }
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                                >
                                  <History className="h-4 w-4" />
                                </button>
                              )}
                              {can('finance.main', 'edit') && (
                                <button
                                  type="button"
                                  title="Tahrirlash"
                                  onClick={() => {
                                    setEditing(t)
                                    setFormOpen(true)
                                  }}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                                >
                                  <Pencil className="h-4 w-4" />
                                </button>
                              )}
                              {can('finance.main', 'delete') && (
                                <button
                                  type="button"
                                  title="O'chirish"
                                  onClick={() => handleDelete(t)}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                                >
                                  <Trash2 className="h-4 w-4" />
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                    {visibleTx.length > 0 && (
                      <tfoot>
                        <tr className="border-t-2 border-slate-200 bg-slate-50/60">
                          <td colSpan={5} className="py-2.5 pl-3 text-[13px] font-semibold text-slate-600">
                            {methodFilter === 'all'
                              ? "Kirim (usul bo'yicha jami)"
                              : `${paymentMethodLabel(methodFilter)} — jami kirim`}
                          </td>
                          <td className="num py-2.5 font-bold text-emerald-600">
                            +{formatMoney(methodFilter === 'all' ? incomeMethodTotal : incomeByMethod[methodFilter])}
                          </td>
                          <td />
                        </tr>
                      </tfoot>
                    )}
                  </table>
                </div>
                <TablePagination {...txPg} />
                {visibleTx.length === 0 && (
                  <div className="state">
                    <div className="state-icon">
                      <Inbox className="h-5 w-5" />
                    </div>
                    <h4>Bu davrda amallar yo'q</h4>
                    <p>Tanlangan davr yoki filtr bo'yicha moliyaviy amal topilmadi.</p>
                  </div>
                )}
              </Card>
            </div>
          )}

          {/* ============ GURUHLAR (faollik + to'lov holati) ============ */}
          {tab === 'groups' && courseReport && (
            <GroupsReport
              report={courseReport}
              month={activeMonth}
              onExport={handleExportGroups}
              onSelect={(g) => setPaymentsTarget({ kind: 'group', id: g.groupId, name: g.groupName })}
              onSelectTeacher={(t) => setPaymentsTarget({ kind: 'teacher', id: t.id, name: t.name })}
            />
          )}

          {/* ============ O'QITUVCHILAR ============ */}
          {tab === 'teachers' && (
            <div className="space-y-6">
              {salaryReport.length > 0 && (
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                  <StatCard
                    label="Jami hisoblangan"
                    value={formatMoney(teacherTotals.expected)}
                    icon={Calculator}
                  />
                  <StatCard
                    label="Jami berilgan"
                    value={formatMoney(teacherTotals.paid)}
                    icon={Wallet}
                    iconBg="bg-emerald-50"
                    iconColor="text-emerald-600"
                  />
                  <StatCard
                    label="Jami qoldiq"
                    value={formatMoney(teacherTotals.remaining)}
                    icon={AlertCircle}
                    iconBg="bg-red-50"
                    iconColor="text-red-600"
                  />
                </div>
              )}
              <Card
                tight
                title="O'qituvchilar maoshi"
                sub={`Davr bo'yicha — ${periodMonths} oy · batafsil uchun o'qituvchini bosing`}
                actions={
                  <Button variant="secondary" onClick={handleExportTeachers} disabled={salaryReport.length === 0}>
                    <Download className="h-4 w-4" /> CSV
                  </Button>
                }
              >
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>O'qituvchi</th>
                        <th className="num">Oylik</th>
                        {anyDeduction && <th className="num">Ushlanma</th>}
                        <th className="num">Hisoblangan</th>
                        <th className="num">Berilgan</th>
                        <th className="num">Qoldiq</th>
                        <th className="num">Tarix</th>
                      </tr>
                    </thead>
                    <tbody>
                      {salaryPg.paged.map((r) => (
                        <tr
                          key={r.teacherId}
                          onClick={() => setDetailTeacher(r)}
                          className="cursor-pointer"
                        >
                          <td className="font-medium text-brand-700">
                            {r.teacherId ? (
                              <Link
                                to={`/admin/teachers/${r.teacherId}`}
                                onClick={(e) => e.stopPropagation()}
                                className="text-inherit hover:underline"
                              >
                                {r.teacherName}
                              </Link>
                            ) : (
                              r.teacherName
                            )}
                          </td>
                          <td className="num text-slate-600">
                            {r.salaryMode === 'percent'
                              ? `${r.salaryPercent ?? 0}% (guruh to'lovidan)`
                              : formatMoney(r.salary)}
                          </td>
                          {anyDeduction && (
                            <td className="num" title="Jurnalda belgilanmagan darslar uchun ushlanma">
                              {(r.deduction ?? 0) > 0 ? (
                                <span className="text-red-600">
                                  −{formatMoney(r.deduction ?? 0)}
                                  <span className="ml-1 text-xs text-slate-400">
                                    ({r.missedLessons ?? 0} dars)
                                  </span>
                                </span>
                              ) : (
                                <span className="text-slate-300">—</span>
                              )}
                            </td>
                          )}
                          <td className="num text-slate-600">{formatMoney(r.expected)}</td>
                          <td className="num font-semibold text-emerald-600">
                            {formatMoney(r.totalPaid)}
                          </td>
                          <td className={cn('num font-semibold', balanceClass(r.remaining))}>
                            {r.remaining < 0 ? `+${formatMoney(-r.remaining)}` : formatMoney(r.remaining)}
                          </td>
                          <td className="num">
                            {canSeeAudit && (
                            <button
                              type="button"
                              title="O'zgarishlar tarixi"
                              onClick={(e) => {
                                e.stopPropagation()
                                setAudit({ filters: { teacherId: r.teacherId }, title: `Tarix — ${r.teacherName}` })
                              }}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                            >
                              <History className="h-4 w-4" />
                            </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                {salaryReport.length === 0 && (
                  <div className="state">
                    <div className="state-icon">
                      <Inbox className="h-5 w-5" />
                    </div>
                    <h4>Ma'lumot yo'q</h4>
                    <p>Tanlangan davr bo'yicha maosh hisoboti topilmadi.</p>
                  </div>
                )}
                <TablePagination {...salaryPg} />
              </Card>
            </div>
          )}

          {/* ============ TO'LOVLAR (kiritilgan to'lovlar ro'yxati) ============ */}
          {tab === 'payments' && (
            <div className="space-y-6">
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <StatCard label="To'lovlar soni" value={String(filteredPayments.length)} icon={Wallet} />
                <StatCard
                  label="Jami summa"
                  value={formatMoney(paymentsTotal)}
                  icon={TrendingUp}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                />
                <StatCard
                  label="Naqd jami"
                  value={formatMoney(paymentsCashTotal)}
                  icon={Banknote}
                  iconBg="bg-teal-50"
                  iconColor="text-teal-600"
                />
              </div>
              <Card
                tight
                title="Kiritilgan to'lovlar"
                sub={
                  payAuthor
                    ? `${payAuthor.name} kiritgan to'lovlar`
                    : payTeacher
                      ? "O'qituvchi guruhlariga teglangan to'lovlar (teglanmagan to'lov bitta o'qituvchiga tegishli emas)"
                      : "O'quvchi to'lovlari (tuition) — xato bo'lsa o'chiring, balans qayta tiklanadi"
                }
                actions={
                  <div className="flex flex-wrap items-center gap-2">
                    {/* To'lov turi — naqd/karta/bank kesimi (yonida shu usul bo'yicha jami summa). */}
                    <select
                      value={payMethod}
                      onChange={(e) => setPayMethod(e.target.value as MethodFilter)}
                      className={filterSelect}
                    >
                      <option value="all">To'lov turi: barchasi</option>
                      {(['cash', 'card', 'bank'] as const).map((m) => (
                        <option key={m} value={m}>
                          {paymentMethodLabel(m)} · {formatMoney(paymentsByMethod[m])}
                        </option>
                      ))}
                    </select>
                    {/* Kvitansiya raqami kiritilgan / kiritilmagan to'lovlar. */}
                    <select
                      value={payReceipt}
                      onChange={(e) => setPayReceipt(e.target.value as ReceiptFilter)}
                      className={filterSelect}
                    >
                      <option value="all">Kvitansiya raqami: barchasi</option>
                      <option value="with">Kvitansiya raqami: bor</option>
                      <option value="without">Kvitansiya raqami: yo'q</option>
                    </select>
                    {/* KIM KIRITGAN — to'lov kirita oladigan barcha xodim/adminlar ro'yxati
                        (hali to'lov kiritmagani ham turadi; jadvaldagi "Kiritgan" ustuni kesimi). */}
                    <select
                      value={payAuthor?.key ?? ''}
                      onChange={(e) =>
                        setPayAuthor(authorOptions.find((a) => a.key === e.target.value) ?? null)
                      }
                      className={filterSelect}
                      title="To'lovni kim kiritgan"
                    >
                      <option value="">Kiritgan: barchasi</option>
                      {authorOptions.map((a) => (
                        <option key={a.key} value={a.key}>
                          {a.name}
                          {a.position ? ` · ${a.position}` : ''}
                        </option>
                      ))}
                    </select>
                    <select
                      value={payTeacher}
                      onChange={(e) => setPayTeacher(e.target.value)}
                      className={filterSelect}
                    >
                      <option value="">Barcha o'qituvchilar</option>
                      {teacherOptions(courseReport).map((t) => (
                        <option key={t.id} value={t.id}>
                          {t.name}
                        </option>
                      ))}
                    </select>
                    <select
                      value={paySort}
                      onChange={(e) => setPaySort(e.target.value as PaySort)}
                      className={filterSelect}
                      title="Saralash"
                    >
                      {paySortOptions.map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                    <div className="relative">
                      <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                      <input
                        type="text"
                        placeholder="O'quvchi / guruh / kvitansiya..."
                        value={paySearch}
                        onChange={(e) => setPaySearch(e.target.value)}
                        className="w-56 rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
                      />
                    </div>
                    <Button variant="secondary" onClick={handleExportPayments} disabled={filteredPayments.length === 0}>
                      <Download className="h-4 w-4" /> CSV
                    </Button>
                  </div>
                }
              >
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Sana</th>
                        <th>O'quvchi</th>
                        <th>Guruh</th>
                        <th>Oy</th>
                        <th>To'lov usuli</th>
                        <th>Kvitansiya</th>
                        <th>Kiritgan</th>
                        <th className="num">Summa</th>
                        <th className="num">Amallar</th>
                      </tr>
                    </thead>
                    <tbody>
                      {payPg.paged.map((p) => (
                        <tr key={p.id}>
                          <td className="font-mono text-[12.5px] text-slate-500">
                            {formatDate(p.date)}
                            {p.createdAt && formatTime(p.createdAt) && (
                              <span className="ml-1 text-slate-400">{formatTime(p.createdAt)}</span>
                            )}
                          </td>
                          <td className="font-medium text-slate-800">
                            {p.studentId ? (
                              <Link to={`/admin/students/${p.studentId}`} className="hover:text-brand-600 hover:underline">
                                {p.studentName ?? '—'}
                              </Link>
                            ) : (
                              p.studentName ?? '—'
                            )}
                          </td>
                          <td>
                            {p.groupName ? (
                              p.groupId ? (
                                <Link to={`/admin/classes/${p.groupId}`}>
                                  <Badge>{p.groupName}</Badge>
                                </Link>
                              ) : (
                                <Badge>{p.groupName}</Badge>
                              )
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                          </td>
                          <td className="text-slate-600">{p.month ? formatMonth(p.month) : '—'}</td>
                          <td>
                            {p.method ? (
                              <Badge tone="blue">{paymentMethodLabel(p.method)}</Badge>
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                            {/* Karta to'lovida — pul o'tkazilgan HAQIQIY vaqt (kassir kiritgan). */}
                            {p.paidTime && (
                              <div className="mt-0.5 font-mono text-[11px] text-slate-400">{p.paidTime}</div>
                            )}
                          </td>
                          <td className="font-mono text-[12.5px] text-slate-600">
                            {receiptCell(p) ?? <span className="text-slate-300">—</span>}
                          </td>
                          {/* KIM KIRITGAN — kassir/admin (FinanceTransaction.CreatedBy). */}
                          <td className="text-[12.5px] text-slate-600">
                            {p.createdBy || <span className="text-slate-300">—</span>}
                          </td>
                          <td className="num font-semibold text-emerald-600">
                            +{formatMoney(p.amount)}
                            {(p.refunded ?? 0) > 0 && (
                              <div className="text-[11px] font-medium text-amber-600" title="Qaytarilgan summa">
                                −{formatMoney(p.refunded ?? 0)} vozvrat
                              </div>
                            )}
                          </td>
                          <td className="num">
                            <div className="flex items-center justify-end gap-0.5">
                              <button
                                type="button"
                                title="Chek (kvitansiya)"
                                onClick={() => openReceipt(p.id)}
                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                              >
                                <Receipt className="h-4 w-4" />
                              </button>
                              {canSeeAudit && (
                                <button
                                  type="button"
                                  title="O'zgarishlar tarixi"
                                  onClick={() =>
                                    setAudit({
                                      filters: { entityType: 'FinanceTransaction', entityId: p.id },
                                      title: "To'lov tarixi",
                                    })
                                  }
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
                                >
                                  <History className="h-4 w-4" />
                                </button>
                              )}
                              {isSuper && can('finance.main', 'edit') && (
                                <button
                                  type="button"
                                  title="Tahrirlash (balans va oylik hisob moslanadi)"
                                  onClick={() => setEditPayment(p)}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                                >
                                  <Pencil className="h-4 w-4" />
                                </button>
                              )}
                              {isSuper && can('finance.main', 'delete') && (p.refunded ?? 0) < p.amount && (
                                <button
                                  type="button"
                                  title="Pul qaytarish (vozvrat)"
                                  onClick={() => setRefundTarget(p)}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-amber-50 hover:text-amber-600"
                                >
                                  <Undo2 className="h-4 w-4" />
                                </button>
                              )}
                              {can('finance.main', 'delete') && (
                                <button
                                  type="button"
                                  title="O'chirish (balans tiklanadi)"
                                  onClick={() => handleDelete(p)}
                                  className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                                >
                                  <Trash2 className="h-4 w-4" />
                                </button>
                              )}
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <TablePagination {...payPg} />
                {filteredPayments.length === 0 && (
                  <div className="state">
                    <div className="state-icon">
                      <Inbox className="h-5 w-5" />
                    </div>
                    <h4>To'lov yo'q</h4>
                    <p>{paySearch ? 'Qidiruv bo\'yicha to\'lov topilmadi.' : 'Tanlangan davrda kiritilgan to\'lov yo\'q.'}</p>
                  </div>
                )}
              </Card>
            </div>
          )}

          {/* KASSIRLAR — kim qancha pul qabul qilgan (kassa bo'limi + moliyadan kiritilgan to'lovlar).
              Qatorni bosish — o'sha kassirning to'lovlari ro'yxati. */}
          {tab === 'cashiers' && (
            <div className="space-y-6">
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <StatCard label="Kassirlar" value={String(cashiers.length)} icon={Users} />
                <StatCard
                  label="Jami qabul qilingan"
                  value={formatMoney(cashiers.reduce((a, c) => a + c.total, 0))}
                  icon={Wallet}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                />
                <StatCard
                  label="To'lovlar soni"
                  value={String(cashiers.reduce((a, c) => a + c.count, 0))}
                  icon={Receipt}
                />
              </div>
              <Card
                tight
                title="Kassirlar kesimi"
                sub="Davr ichida kim qancha pul qabul qilgan — qatorni bosing (uning to'lovlari ro'yxati)"
              >
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Kassir</th>
                        <th className="num">To'lovlar</th>
                        <th className="num">Naqd</th>
                        <th className="num">Karta</th>
                        <th className="num">Bank</th>
                        <th className="num">Jami</th>
                        <th>Oxirgi to'lov</th>
                      </tr>
                    </thead>
                    <tbody>
                      {cashiers.length === 0 ? (
                        <tr>
                          <td colSpan={7} className="py-10 text-center text-slate-400">
                            Bu davrda to'lov kiritilmagan.
                          </td>
                        </tr>
                      ) : (
                        cashiers.map((c) => (
                          <tr
                            key={c.key}
                            // Alohida SAHIFA — ichida qidiruv, sahifalash va CSV bor (modal sig'masdi).
                            onClick={() =>
                              navigate(
                                `/admin/finance/cashiers/${encodeURIComponent(c.key)}` +
                                  `?name=${encodeURIComponent(c.cashierName)}&from=${rangeFrom}&to=${rangeTo}`,
                              )
                            }
                            className="cursor-pointer"
                          >
                            <td className="font-medium text-slate-700">{c.cashierName}</td>
                            <td className="num">{c.count}</td>
                            <td className="num font-mono text-[12.5px] text-slate-600">{formatMoney(c.cash)}</td>
                            <td className="num font-mono text-[12.5px] text-slate-600">{formatMoney(c.card)}</td>
                            <td className="num font-mono text-[12.5px] text-slate-600">{formatMoney(c.bank)}</td>
                            <td className="num font-mono font-semibold text-emerald-600">{formatMoney(c.total)}</td>
                            <td className="text-[12.5px] text-slate-500">
                              {c.lastAt ? formatDateTime(c.lastAt) : '—'}
                            </td>
                          </tr>
                        ))
                      )}
                    </tbody>
                  </table>
                </div>
              </Card>
            </div>
          )}

          {tab === 'refunds' && (
            <div className="space-y-6">
              <div className="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <StatCard label="Vozvratlar soni" value={String(refunds.length)} icon={Undo2} />
                <StatCard
                  label="Jami qaytarilgan"
                  value={formatMoney(refunds.reduce((a, r) => a + r.amount, 0))}
                  icon={TrendingDown}
                  iconBg="bg-amber-50"
                  iconColor="text-amber-600"
                />
              </div>
              <Card
                tight
                title="Vozvratlar tarixi"
                sub="Qaytarilgan pullar — o'quvchi balansi kamaygan, o'qituvchi foizi net summadan hisoblangan"
              >
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Sana</th>
                        <th>O'quvchi</th>
                        <th>Guruh</th>
                        <th>Oy</th>
                        <th>Sabab</th>
                        <th>Mas'ul</th>
                        <th className="num">Asl to'lov</th>
                        <th className="num">Qaytarilgan</th>
                      </tr>
                    </thead>
                    <tbody>
                      {refundPg.paged.map((r) => (
                        <tr key={r.id}>
                          <td className="font-mono text-[12.5px] text-slate-500">
                            {formatDate(r.date)}
                            {r.createdAt && formatTime(r.createdAt) && (
                              <span className="ml-1 text-slate-400">{formatTime(r.createdAt)}</span>
                            )}
                          </td>
                          <td className="font-medium text-slate-800">
                            {r.studentId ? (
                              <Link to={`/admin/students/${r.studentId}`} className="hover:text-brand-600 hover:underline">
                                {r.studentName ?? '—'}
                              </Link>
                            ) : (
                              r.studentName ?? '—'
                            )}
                          </td>
                          <td>
                            {r.groupName ? (
                              r.groupId ? (
                                <Link to={`/admin/classes/${r.groupId}`}>
                                  <Badge>{r.groupName}</Badge>
                                </Link>
                              ) : (
                                <Badge>{r.groupName}</Badge>
                              )
                            ) : (
                              <span className="text-slate-300">—</span>
                            )}
                          </td>
                          <td className="text-slate-600">{r.month ? formatMonth(r.month) : '—'}</td>
                          <td className="text-slate-600">{r.reason || <span className="text-slate-300">—</span>}</td>
                          <td className="text-slate-500">{r.createdBy ?? '—'}</td>
                          <td className="num text-slate-500">
                            {r.paymentAmount != null ? formatMoney(r.paymentAmount) : '—'}
                          </td>
                          <td className="num font-semibold text-amber-600">−{formatMoney(r.amount)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <TablePagination {...refundPg} />
                {refunds.length === 0 && (
                  <div className="state">
                    <div className="state-icon">
                      <Undo2 className="h-5 w-5" />
                    </div>
                    <h4>Vozvrat yo'q</h4>
                    <p>Tanlangan davrda pul qaytarilmagan. To'lov yonidagi ↩ tugmasi orqali qaytarish mumkin.</p>
                  </div>
                )}
              </Card>
            </div>
          )}
        </>
      )}

      <TransactionFormModal
        open={formOpen}
        onClose={() => {
          setFormOpen(false)
          setEditing(null)
        }}
        onSubmit={handleSubmit}
        initial={editing}
      />

      <AuditHistoryModal
        open={!!audit}
        onClose={() => setAudit(null)}
        title={audit?.title}
        filters={audit?.filters ?? {}}
      />

      <TeacherSalaryDetailModal
        teacher={detailTeacher}
        from={rangeFrom}
        to={rangeTo}
        onClose={() => setDetailTeacher(null)}
      />

      <GroupPaymentsModal
        target={paymentsTarget}
        from={rangeFrom}
        to={rangeTo}
        onClose={() => setPaymentsTarget(null)}
      />

      <ReasonPromptModal
        open={!!deleting}
        category="finance_delete"
        title="Tranzaksiyani o'chirish"
        message={deleting ? "Ushbu moliyaviy amalni o'chirasizmi?" : undefined}
        confirmLabel="O'chirish"
        tone="red"
        onConfirm={doDelete}
        onClose={() => setDeleting(null)}
      />

      <PaymentEditModal payment={editPayment} onClose={() => setEditPayment(null)} onSaved={load} />

      <RefundModal payment={refundTarget} onClose={() => setRefundTarget(null)} onSaved={load} />

      <ReceiptModal
        txId={receiptTx}
        autoPrint={receiptAuto}
        onClose={() => {
          setReceiptTx(null)
          setReceiptAuto(false)
        }}
      />
    </div>
  )
}

/** O'qituvchi filtri uchun noyob ro'yxat (hisobot guruhlaridan) — Guruhlar va To'lovlar tablari uchun bir xil. */
function teacherOptions(report: CourseFinanceReport | null): { id: string; name: string }[] {
  if (!report) return []
  const map = new Map<string, string>()
  report.groups.forEach((g) => {
    if (g.teacherId) map.set(g.teacherId, g.teacherName)
  })
  return [...map.entries()].map(([id, name]) => ({ id, name })).sort((a, b) => a.name.localeCompare(b.name))
}

/** Yig'ilish foizini ranglash (90%+ yashil, 60%+ sariq, past qizil). */
function pctClass(p: number): string {
  return p >= 90 ? 'text-emerald-600' : p >= 60 ? 'text-amber-600' : 'text-red-600'
}
function pctBar(p: number): string {
  return p >= 90 ? 'bg-emerald-500' : p >= 60 ? 'bg-amber-500' : 'bg-red-500'
}

/** Guruhlar kesimida moliyaviy hisobot: faollik + bosilganda guruh ichidagi to'lov holati.
 *  O'qituvchi tanlansa — tepadagi kartalar HAM shu o'qituvchi guruhlari bo'yicha qayta hisoblanadi
 *  va "Barchasini ko'rish" bilan uning barcha o'quvchilari bitta ro'yxatda ochiladi. */
function GroupsReport({
  report,
  month,
  onExport,
  onSelect,
  onSelectTeacher,
}: {
  report: CourseFinanceReport
  /** Sahifada tanlangan oy ("" = butun davr) — faqat sarlavha/izoh matni uchun. */
  month: string
  onExport: () => void
  onSelect: (g: GroupFinanceRow) => void
  onSelectTeacher: (t: { id: string; name: string }) => void
}) {
  const [teacherId, setTeacherId] = useState<string>('')

  const teachers = teacherOptions(report)
  const groups = teacherId ? report.groups.filter((g) => g.teacherId === teacherId) : report.groups
  const teacherName = teachers.find((t) => t.id === teacherId)?.name ?? ''

  // Kartalar ko'rinib turgan guruhlardan hisoblanadi — o'qituvchi tanlansa faqat uning guruhlari.
  const collected = groups.reduce((a, g) => a + g.collected, 0)
  const cash = groups.reduce((a, g) => a + g.collectedCash, 0)
  const billed = groups.reduce((a, g) => a + g.billed, 0)
  const pct = billed > 0 ? Math.round((collected / billed) * 1000) / 10 : 0
  const scope = teacherId ? teacherName : 'Markaz bo\'yicha'
  // Kartochka sarlavha/izohlarida "davr" o'rniga tanlangan oy nomi ko'rsatiladi.
  const rangeLabel = month ? formatMonth(month) : 'davr'

  return (
    <div className="space-y-6">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label={`Jami yig'ilgan (${rangeLabel})`}
          value={formatMoney(collected)}
          icon={Wallet}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint={`${scope} — ${month ? 'shu oy' : 'davr oylari'} uchun to'langan (avans o'z oyida hisoblanadi)`}
        />
        <StatCard
          label="Naqd jami"
          value={formatMoney(cash)}
          icon={Banknote}
          iconBg="bg-teal-50"
          iconColor="text-teal-600"
          hint={`${scope} — yig'ilganning naqd qismi`}
        />
        <StatCard
          label="Jami hisoblangan"
          value={formatMoney(billed)}
          icon={Calculator}
          hint={`${scope} — ${rangeLabel} uchun hisoblangan oyliklar`}
        />
        <StatCard
          label="Yig'ilish foizi"
          value={`${pct}%`}
          icon={Percent}
          iconBg={pct >= 90 ? 'bg-emerald-50' : 'bg-amber-50'}
          iconColor={pct >= 90 ? 'text-emerald-600' : 'text-amber-600'}
          hint="Yig'ilgan ÷ hisoblangan"
        />
      </div>

      {/* Guruhlar — faollik (qaysi o'qituvchi guruhi ko'proq yig'di). Guruhni bosing — to'lov holati. */}
      <Card
        tight
        title="Guruhlar bo'yicha faollik"
        sub={
          teacherId
            ? "Guruhni bosing — kim to'ladi, kim to'lamadi · «Barchasini ko'rish» — hamma guruh o'quvchilari birga"
            : "Guruhni bosing — kim to'ladi, kim to'lamadi"
        }
        actions={
          <div className="flex items-center gap-2">
            <select
              value={teacherId}
              onChange={(e) => setTeacherId(e.target.value)}
              className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">Barcha o'qituvchilar</option>
              {teachers.map((t) => (
                <option key={t.id} value={t.id}>
                  {t.name}
                </option>
              ))}
            </select>
            {teacherId && (
              <Button variant="secondary" onClick={() => onSelectTeacher({ id: teacherId, name: teacherName })}>
                <Users className="h-4 w-4" /> Barchasini ko'rish
              </Button>
            )}
            <Button variant="secondary" onClick={onExport} disabled={report.groups.length === 0}>
              <Download className="h-4 w-4" /> CSV
            </Button>
          </div>
        }
      >
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Guruh</th>
                <th>Kurs</th>
                <th>O'qituvchi</th>
                <th className="num">O'quvchi</th>
                <th className="num">Hisoblangan</th>
                <th className="num">Yig'ilgan</th>
                <th className="num">Yig'ilish</th>
                <th className="num">To'liq to'lagan</th>
              </tr>
            </thead>
            <tbody>
              {groups.map((g) => (
                <tr key={g.groupId} onClick={() => onSelect(g)} className="cursor-pointer">
                  <td className="font-medium text-brand-700">
                    <Link
                      to={`/admin/classes/${g.groupId}`}
                      onClick={(e) => e.stopPropagation()}
                      className="text-inherit hover:underline"
                    >
                      {g.groupName}
                    </Link>
                  </td>
                  <td>
                    <Badge>{g.courseName}</Badge>
                  </td>
                  <td className="text-slate-600">
                    {g.teacherId ? (
                      <Link
                        to={`/admin/teachers/${g.teacherId}`}
                        onClick={(e) => e.stopPropagation()}
                        className="text-inherit hover:text-brand-600 hover:underline"
                      >
                        {g.teacherName}
                      </Link>
                    ) : (
                      g.teacherName
                    )}
                  </td>
                  <td className="num text-slate-600">{g.studentCount}</td>
                  <td className="num text-slate-600">{formatMoney(g.billed)}</td>
                  <td className="num font-semibold text-emerald-600">{formatMoney(g.collected)}</td>
                  <td className="num">
                    <div className="flex items-center justify-end gap-2">
                      <div className="h-1.5 w-12 overflow-hidden rounded-full bg-slate-100">
                        <div
                          className={cn('h-full rounded-full', pctBar(g.collectionPct))}
                          style={{ width: `${Math.min(100, g.collectionPct)}%` }}
                        />
                      </div>
                      <span className={cn('font-mono font-semibold', pctClass(g.collectionPct))}>
                        {g.collectionPct}%
                      </span>
                    </div>
                  </td>
                  <td className="num font-mono text-slate-700">
                    {g.fullyPaidStudents}/{g.billableStudents}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {groups.length === 0 && (
          <div className="state">
            <div className="state-icon">
              <Inbox className="h-5 w-5" />
            </div>
            <h4>Ma'lumot yo'q</h4>
            <p>Tanlangan davr/o'qituvchi bo'yicha guruh faolligi topilmadi.</p>
          </div>
        )}
      </Card>
    </div>
  )
}

function CategoryList({
  title,
  items,
  positive,
}: {
  title: string
  items: { category: string; amount: number }[]
  positive: boolean
}) {
  const total = items.reduce((a, c) => a + c.amount, 0)
  return (
    <div>
      <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</p>
      {items.length === 0 ? (
        <p className="text-sm text-slate-400">Ma'lumot yo'q</p>
      ) : (
        <ul className="space-y-2">
          {items.map((c) => (
            <li key={c.category} className="flex items-center justify-between gap-2 text-sm">
              <span className="text-slate-600">{financeCategoryLabel(c.category)}</span>
              <span className={cn('font-mono font-semibold', positive ? 'text-emerald-600' : 'text-red-600')}>
                {formatMoney(c.amount)}
              </span>
            </li>
          ))}
          <li className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2 text-sm font-semibold">
            <span className="text-slate-700">Jami</span>
            <span className={cn('font-mono', positive ? 'text-emerald-700' : 'text-red-700')}>
              {formatMoney(total)}
            </span>
          </li>
        </ul>
      )}
    </div>
  )
}
