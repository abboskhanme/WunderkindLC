import type {
  CategoryAmount,
  FinanceDirection,
  FinanceMonthly,
  FinanceSummary,
  FinanceTransaction,
  Refund,
  SalaryReportRow,
  StudentFinanceRow,
} from '@/types'
import type { ReceiptData } from '@/lib/receipt'
// Kassir kesimi turlari kassa servisida (yagona manba) — moliya jadvali ham shulardan foydalanadi.
import type { CashierPayments, CashierSummary } from './kassa'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { financeMock } from '../mock/finance'
import { studentsMock } from '../mock/students'
import { classesMock } from '../mock/classes'
import { teachersMock } from '../mock/teachers'

/** "YYYY-MM-DD" oralig'idagi (inklyuziv) kalendar oylar soni */
function monthsInPeriod(from?: string, to?: string): number {
  if (!from || !to) return 1
  const [fy, fm] = from.slice(0, 7).split('-').map(Number)
  const [ty, tm] = to.slice(0, 7).split('-').map(Number)
  const m = (ty - fy) * 12 + (tm - fm) + 1
  return m < 1 ? 1 : m
}

export interface FinanceTransactionPayload {
  date: string
  direction: FinanceDirection
  category: string
  amount: number
  note?: string
  studentId?: string
  /** Chiqim "salary" bo'lsa — qaysi o'qituvchiga (oylik maosh) */
  teacherId?: string
  /**
   * QAYSI OY UCHUN ("YYYY-MM") — `date` (pul harakat qilgan kun) bilan ARALASHTIRMANG.
   * Kirim "tuition" (o'quvchi to'lovi) va chiqim "salary" (o'qituvchi maoshi) uchun to'ldiriladi;
   * maosh aynan shu maydon bo'yicha oyga bog'lanadi (`SalaryLedger.BuildAsync`).
   */
  month?: string
  /** To'lov usuli (kirim uchun): cash | card | bank */
  method?: string
  /** Qog'oz kvitansiya raqami (naqd to'lov) — "KV" seriyasi bilan */
  receiptNo?: string
  /** To'lov vaqti "HH:mm" (karta orqali to'lov) */
  paidTime?: string
  /** Karta raqamining oxirgi 4 raqami (karta orqali to'lov) */
  cardLast4?: string
}

/**
 * O'quvchi to'lovini tahrirlash (FAQAT superadmin) — sana/summa/oy/guruh/usul/izoh.
 * Server balansni summa farqiga moslaydi va yangi (guruh, oy) hisobini kerak bo'lsa ochadi.
 */
export interface PaymentEditPayload {
  date: string
  amount: number
  /** Qaysi oy uchun ("YYYY-MM") */
  month: string
  /** Qaysi guruh uchun (bo'sh = guruhsiz) */
  groupId?: string
  /** cash | card | bank */
  method?: string
  /** Kassir izohi */
  comment?: string
  /** Qog'oz kvitansiya raqami (naqd to'lov) — "KV" seriyasi bilan */
  receiptNo?: string
  /** To'lov vaqti "HH:mm" (karta orqali to'lov) */
  paidTime?: string
  /** Karta raqamining oxirgi 4 raqami (karta orqali to'lov) */
  cardLast4?: string
  /** Kvitansiya raqami boshqa to'lovda band bo'lsa ham saqlash ("Baribir saqlash"). */
  forceReceipt?: boolean
}

export interface TransactionFilters {
  from?: string
  to?: string
  direction?: FinanceDirection
  category?: string
}

/* ---------- Mock yordamchilari ---------- */

function inRange(t: FinanceTransaction, from?: string, to?: string): boolean {
  if (from && t.date < from) return false
  if (to && t.date > to) return false
  return true
}

function sumBy(items: FinanceTransaction[], cat: (t: FinanceTransaction) => boolean): number {
  return items.filter(cat).reduce((acc, t) => acc + t.amount, 0)
}

function byCategory(items: FinanceTransaction[]): CategoryAmount[] {
  const map = new Map<string, number>()
  items.forEach((t) => map.set(t.category, (map.get(t.category) ?? 0) + t.amount))
  return [...map.entries()]
    .map(([category, amount]) => ({ category, amount }))
    .sort((a, b) => b.amount - a.amount)
}

/* ---------- API ---------- */

export async function getTransactions(filters: TransactionFilters = {}): Promise<FinanceTransaction[]> {
  if (USE_MOCK) {
    await delay()
    return financeMock
      .filter(
        (t) =>
          inRange(t, filters.from, filters.to) &&
          (!filters.direction || t.direction === filters.direction) &&
          (!filters.category || t.category === filters.category),
      )
      .sort((a, b) => (a.date < b.date ? 1 : -1))
  }
  const { data } = await api.get<FinanceTransaction[]>('/admin/finance/transactions', {
    params: filters,
  })
  return data
}

export async function createTransaction(
  payload: FinanceTransactionPayload,
): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(250)
    const studentName = payload.studentId
      ? studentsMock.find((s) => s.id === payload.studentId)?.fullName
      : undefined
    const tx: FinanceTransaction = { id: uid(), ...payload, studentName }
    financeMock.unshift(tx)
    return tx
  }
  const { data } = await api.post<FinanceTransaction>('/admin/finance/transactions', payload)
  return data
}

/** Bitta to'lov uchun chek (kvitansiya) ma'lumotlari — termal chek chizish/print uchun. */
export async function getReceipt(txId: string): Promise<ReceiptData & { settingsJson: string }> {
  const { data } = await api.get<ReceiptData & { settingsJson: string }>(
    `/admin/finance/receipt/${txId}`,
  )
  return data
}

export async function updateTransaction(
  id: string,
  payload: FinanceTransactionPayload,
): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(250)
    const i = financeMock.findIndex((t) => t.id === id)
    const studentName = payload.studentId
      ? studentsMock.find((s) => s.id === payload.studentId)?.fullName
      : undefined
    const tx: FinanceTransaction = { id, ...payload, studentName }
    if (i >= 0) financeMock[i] = tx
    return tx
  }
  const { data } = await api.put<FinanceTransaction>(`/admin/finance/transactions/${id}`, payload)
  return data
}

/**
 * O'quvchi to'lovini tahrirlash (superadmin). Server: balans = summa farqiga moslanadi,
 * yangi (guruh, oy) uchun oylik hisob yo'q bo'lsa ochiladi, audit yoziladi.
 */
export async function updatePayment(id: string, payload: PaymentEditPayload): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(250)
    const i = financeMock.findIndex((t) => t.id === id)
    if (i >= 0) financeMock[i] = { ...financeMock[i], ...payload }
    return financeMock[i]
  }
  const { data } = await api.put<FinanceTransaction>(`/admin/finance/payments/${id}`, payload)
  return data
}

/** Vozvrat (pul qaytarish) so'rovi — summa (majburiy), sana (ixtiyoriy, default bugun), sabab (ixtiyoriy). */
export interface RefundPayload {
  amount: number
  date?: string
  reason?: string
}

/**
 * O'quvchi to'lovini qisman/to'liq VOZVRAT qilish (FAQAT superadmin). Server: alohida vozvrat yozuvi yaratadi,
 * o'quvchi balansini shu summaga kamaytiradi (muzlatishdan hosil bo'lgan avans qaytariladi), o'qituvchining
 * foizli maoshi va "yig'ilgan" hisobotlari net (to'langan − vozvrat) dan qayta hisoblanadi.
 */
export async function refundPayment(id: string, payload: RefundPayload): Promise<FinanceTransaction> {
  if (USE_MOCK) {
    await delay(200)
    return { id, date: payload.date ?? '', direction: 'expense', category: 'refund', amount: payload.amount }
  }
  const { data } = await api.post<FinanceTransaction>(`/admin/finance/payments/${id}/refund`, payload)
  return data
}

/** Vozvratlar tarixi (qaytarilgan pullar) — davr bo'yicha, asl to'lov ma'lumoti bilan. */
export async function getRefunds(from?: string, to?: string): Promise<Refund[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<Refund[]>('/admin/finance/refunds', { params: { from, to } })
  return data
}

export async function deleteTransaction(id: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    const i = financeMock.findIndex((t) => t.id === id)
    if (i >= 0) financeMock.splice(i, 1)
    return
  }
  await api.delete(`/admin/finance/transactions/${id}`, { params: reasonId ? { reasonId } : undefined })
}

export async function getFinanceSummary(from?: string, to?: string): Promise<FinanceSummary> {
  if (USE_MOCK) {
    await delay()
    const items = financeMock.filter((t) => inRange(t, from, to))
    const income = items.filter((t) => t.direction === 'income')
    const expense = items.filter((t) => t.direction === 'expense')
    const totalIncome = income.reduce((a, t) => a + t.amount, 0)
    const totalExpense = expense.reduce((a, t) => a + t.amount, 0)
    const tuitionIncome = sumBy(income, (t) => t.category === 'tuition')
    const balances = studentsMock.map((s) => s.balance)
    return {
      totalIncome,
      totalExpense,
      net: totalIncome - totalExpense,
      tuitionIncome,
      otherIncome: totalIncome - tuitionIncome,
      incomeByCategory: byCategory(income),
      expenseByCategory: byCategory(expense),
      studentDebt: balances.filter((b) => b < 0).reduce((a, b) => a - b, 0),
      studentAdvance: balances.filter((b) => b > 0).reduce((a, b) => a + b, 0),
      transactionsCount: items.length,
    }
  }
  const { data } = await api.get<FinanceSummary>('/admin/finance/summary', { params: { from, to } })
  return data
}

/** O'qituvchilar maoshi hisoboti (davr bo'yicha): oylik, kerakli, berilgan, qoldiq */
export async function getSalaryReport(from?: string, to?: string): Promise<SalaryReportRow[]> {
  if (USE_MOCK) {
    await delay()
    const periodFrom = (from ?? `${new Date().getFullYear()}-01-01`).slice(0, 7)
    return teachersMock.map((t) => {
      // Oylik o'qituvchi boshlagan oydan hisoblanadi (avvalgi oylar uchun qarz yozilmaydi).
      const startMonth =
        t.salaryStartMonth && t.salaryStartMonth > periodFrom ? t.salaryStartMonth : periodFrom
      const months = monthsInPeriod(`${startMonth}-01`, to)
      const paid = financeMock.filter(
        (x) =>
          x.teacherId === t.id &&
          x.category === 'salary' &&
          x.date.slice(0, 7) >= startMonth &&
          inRange(x, undefined, to),
      )
      const totalPaid = paid.reduce((a, p) => a + p.amount, 0)
      const expected = t.salary * months
      return {
        teacherId: t.id,
        teacherName: t.fullName,
        salary: t.salary,
        totalPaid,
        paymentsCount: paid.length,
        months,
        expected,
        remaining: expected - totalPaid,
      }
    })
  }
  const { data } = await api.get<SalaryReportRow[]>('/admin/finance/salary-report', {
    params: { from, to },
  })
  return data
}

/** O'quvchilar bo'yicha moliya hisoboti (joriy holat): hisoblangan, to'langan, qoldiq, avans */
export async function getStudentReport(): Promise<StudentFinanceRow[]> {
  if (USE_MOCK) {
    await delay()
    return studentsMock
      .map((s) => {
        const fee = classesMock.find((c) => c.name === s.className)?.monthlyFee ?? 0
        const charged = fee * 5 // mock: 5 oy hisoblangan
        const debt = s.balance < 0 ? -s.balance : 0
        const advance = s.balance > 0 ? s.balance : 0
        const paid = Math.max(0, charged - debt)
        const monthDiscount = Math.max(
          0,
          Math.min(fee, (fee * s.discountPct) / 100 + s.discountAmount),
        )
        const discount = monthDiscount * 5
        return {
          studentId: s.id,
          fullName: s.fullName,
          className: s.className,
          charged,
          discount,
          paid,
          debt,
          advance,
          discountPct: s.discountPct,
          discountAmount: s.discountAmount,
        }
      })
      .sort((a, b) => b.debt - a.debt || a.fullName.localeCompare(b.fullName))
  }
  const { data } = await api.get<StudentFinanceRow[]>('/admin/finance/student-report')
  return data
}

export interface AccrueResult {
  months: string[]
  count: number
  total: number
}

/** Oylik to'lovni hisoblash. month berilmasa — hisoblanmagan barcha oylar. */
export async function accrueTuition(month?: string): Promise<AccrueResult> {
  if (USE_MOCK) {
    await delay(300)
    return { months: month ? [month] : [], count: 0, total: 0 }
  }
  const { data } = await api.post<AccrueResult>('/admin/finance/accrue', null, {
    params: month ? { month } : {},
  })
  return data
}

/* ---------- Kurs/guruh kesimida moliyaviy hisobot ---------- */
export interface CourseFinanceRow {
  courseId: string
  courseName: string
  price: number
  groupCount: number
  studentCount: number
  billed: number
  collected: number
  collectionPct: number
  fullyPaidStudents: number
  billableStudents: number
  paidPct: number
}
export interface GroupFinanceRow {
  groupId: string
  groupName: string
  courseName: string
  teacherId: string
  teacherName: string
  studentCount: number
  billed: number
  collected: number
  /** Yig'ilganning NAQD (usuli "cash") ulushi — "Naqd jami" kartasi uchun */
  collectedCash: number
  collectionPct: number
  fullyPaidStudents: number
  billableStudents: number
}
export interface CourseFinanceReport {
  from: string
  to: string
  totalBilled: number
  totalCollected: number
  /** Markaz bo'yicha naqd yig'ilgan (guruh qatorlaridagi collectedCash yig'indisi) */
  totalCollectedCash: number
  collectionPct: number
  courses: CourseFinanceRow[]
  groups: GroupFinanceRow[]
}

/* ---------- Bitta guruh ichidagi to'lov holati (kim to'ladi / kim to'lamadi) ---------- */
export interface GroupPaymentRow {
  studentId: string
  fullName: string
  status: string
  billed: number
  collected: number
  debt: number
  fullyPaid: boolean
  hasPaid: boolean
}
export interface GroupPaymentsReport {
  groupId: string
  groupName: string
  from: string
  to: string
  billed: number
  collected: number
  paidCount: number
  unpaidCount: number
  studentCount: number
  rows: GroupPaymentRow[]
}

/** Bitta guruh ichidagi to'lov holati — kim to'ladi, kim to'lamadi (davr bo'yicha). */
export async function getGroupPayments(groupId: string, from?: string, to?: string): Promise<GroupPaymentsReport> {
  const { data } = await api.get<GroupPaymentsReport>(`/admin/finance/group-payments/${groupId}`, {
    params: { from, to },
  })
  return data
}

/** O'qituvchining BARCHA guruhlari bo'yicha to'lov holati — javob shakli guruhnikidek
 *  (ko'p guruhli o'quvchi bitta qatorda jamlanadi). */
export async function getTeacherPayments(teacherId: string, from?: string, to?: string): Promise<GroupPaymentsReport> {
  const { data } = await api.get<GroupPaymentsReport>(`/admin/finance/teacher-payments/${teacherId}`, {
    params: { from, to },
  })
  return data
}

/** Kurs/guruh kesimida moliyaviy hisobot (qaysi kurs ko'p daromad, to'lov to'liqligi, faol guruh). */
export async function getCourseReport(from?: string, to?: string): Promise<CourseFinanceReport> {
  if (USE_MOCK) {
    await delay()
    return {
      from: from ?? '',
      to: to ?? '',
      totalBilled: 0,
      totalCollected: 0,
      totalCollectedCash: 0,
      collectionPct: 0,
      courses: [],
      groups: [],
    }
  }
  const { data } = await api.get<CourseFinanceReport>('/admin/finance/course-report', {
    params: { from, to },
  })
  return data
}

export async function getFinanceMonthly(year: number): Promise<FinanceMonthly[]> {
  if (USE_MOCK) {
    await delay()
    const result: FinanceMonthly[] = []
    for (let m = 1; m <= 12; m++) {
      const month = `${year}-${String(m).padStart(2, '0')}`
      const items = financeMock.filter((t) => t.date.startsWith(month))
      result.push({
        month,
        income: sumBy(items, (t) => t.direction === 'income'),
        expense: sumBy(items, (t) => t.direction === 'expense'),
      })
    }
    return result
  }
  const { data } = await api.get<FinanceMonthly[]>('/admin/finance/monthly', { params: { year } })
  return data
}

/* ---------- Kassirlar kesimi (Moliya → "Kassirlar") ---------- */

/** Davr ichida kim qancha pul qabul qilgan (kassir kesimi). */
export async function getCashiers(from?: string, to?: string): Promise<CashierSummary[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<CashierSummary[]>('/admin/finance/cashiers', { params: { from, to } })
  return data
}

/** "Kiritgan" filtri varianti — to'lov kirita oladigan xodim/admin (yoki ilgari kiritgan akkaunt). */
export interface PaymentAuthor {
  /** Kalit: akkaunt id'si yoki eski yozuvlar uchun "name:F.I.Sh" */
  key: string
  id: string | null
  name: string
  /** Lavozim yorlig'i (Kassir/Administrator/...) — bo'sh bo'lishi mumkin */
  position: string
  /** Hozir to'lov kirita oladimi (ruxsati bormi) — false = faqat eski to'lovlari bor */
  canEnter: boolean
}

/** To'lov kirita oladiganlar ro'yxati ("Kiritgan" filtri uchun; davr — eski kiritganlar ham chiqsin). */
export async function getPaymentAuthors(from?: string, to?: string): Promise<PaymentAuthor[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<PaymentAuthor[]>('/admin/finance/payment-authors', { params: { from, to } })
  return data
}

/** Bitta kassir kiritgan to'lovlar (jadvaldagi qatorni bosganda) + o'sha kassirning jami. */
export async function getCashierPayments(
  from: string | undefined,
  to: string | undefined,
  cashierId: string | null,
  cashierName: string,
): Promise<CashierPayments> {
  const { data } = await api.get<CashierPayments>('/admin/finance/cashier-payments', {
    params: { from, to, cashierId: cashierId ?? undefined, cashierName },
  })
  return data
}

/* ---------- Bonuslar hisoboti (Moliya → "Bonus") ----------
 *
 * DIQQAT: bonus PUL CHIQIMI EMAS — u faqat qayd (haqiqiy pul o'qituvchiga maosh to'lovi orqali
 * beriladi). Shuning uchun bu raqamlar Moliyaning kirim/chiqim xulosasiga KIRMAYDI, alohida
 * hisobot bo'lib turadi. Bonus BERISH — "O'quvchilar → Bonus hisoboti" sahifasida.
 */

/** Hisobotdagi bitta qator — bonus × o'qituvchi (bitta bonus bir necha o'qituvchiga bo'linishi mumkin). */
export interface RetentionBonusFinanceRow {
  awardId: string
  /** Bonus BERILGAN oy ("YYYY-MM") */
  givenMonth: string
  teacherId: string
  teacherName: string
  studentId: string
  studentName: string
  courseName: string
  /** Qaysi davr uchun ("YYYY-MM") */
  periodFrom: string
  periodTo: string
  /** Shu o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin) */
  months: number
  /** Shu o'qituvchiga tegadigan summa (so'm) */
  amount: number
  /** given — berilgan | cancelled — bekor qilingan */
  status: string
  /** Berilgan sana-vaqti ("yyyy-MM-ddTHH:mm:ss") */
  givenAt: string
  givenBy: string
}

/** O'qituvchilar kesimi — kim qancha bonus oldi (faqat "given"). */
export interface RetentionBonusByTeacher {
  teacherId: string
  teacherName: string
  count: number
  total: number
}

/** Oylar kesimi — qaysi oyda qancha bonus berildi (faqat "given"). */
export interface RetentionBonusByMonth {
  month: string
  count: number
  total: number
}

export interface RetentionBonusFinanceReport {
  /** Hisobot davri ("YYYY-MM", ikkalasi ham inklyuziv) */
  from: string
  to: string
  /** Jami berilgan summa — faqat "given" */
  total: number
  count: number
  /** Bekor qilinganlar alohida (jamiga qo'shilmaydi) */
  cancelledTotal: number
  cancelledCount: number
  byTeacher: RetentionBonusByTeacher[]
  byMonth: RetentionBonusByMonth[]
  rows: RetentionBonusFinanceRow[]
}

/** Bonuslar hisoboti (davr "YYYY-MM"; bo'sh bo'lsa server joriy yil boshidan joriy oygacha oladi). */
export async function getRetentionBonusReport(
  from?: string,
  to?: string,
): Promise<RetentionBonusFinanceReport> {
  const { data } = await api.get<RetentionBonusFinanceReport>('/admin/finance/retention-bonuses', {
    params: cleanParams({ from, to }),
  })
  return data
}

/** Bonuslar hisobotini Excel'ga yuklab olish. */
export async function exportRetentionBonusReport(from?: string, to?: string): Promise<void> {
  const res = await api.get('/admin/finance/retention-bonuses/export', {
    params: cleanParams({ from, to }),
    responseType: 'blob',
  })
  const cd = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = match?.[1] ?? `bonus_moliya_${new Date().toISOString().slice(0, 10)}.xlsx`
  a.click()
  URL.revokeObjectURL(href)
}

/** Bo'sh (undefined/null/"") parametrlarni so'rovdan chiqarib tashlaydi. */
function cleanParams(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}
