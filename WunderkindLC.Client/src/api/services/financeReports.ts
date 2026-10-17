import { api } from '../client'

/**
 * MOLIYA HISOBOTLARI — «Qarzdorlik tahlili», «Guruhlar va tariflar», «Hisob-fakturalar»
 * (SPEC-backlog-2026-10-17 F1). Butun hisob SERVERDA (`DebtLedger` — kassadagi to'lov oynasi qoidasi),
 * sahifalar faqat chizadi.
 *
 * ⚠️ Shakl `WunderkindLC.Application/Dtos/FinanceReportDtos.cs` bilan MAYDONMA-MAYDON bir xil.
 */

export interface DebtTotals {
  charged: number
  discount: number
  net: number
  paid: number
  debt: number
  collectionPct: number
  debtors: number
}

export type DebtBucketKey = 'notdue' | '0-6' | '7-29' | '30-59' | '60+'

export interface DebtAgeBucket {
  key: DebtBucketKey
  label: string
  rows: number
  students: number
  debt: number
}

export interface DebtRow {
  studentId: string
  studentName: string
  phone: string
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
  memberStatus: string
  isArchived: boolean
  /** Oy ("yyyy-MM") → qolgan qarz (faqat > 0). */
  cells: Record<string, number>
  debt: number
  oldestMonth: string
  ageDays: number
  bucket: DebtBucketKey
}

/** Hisobi YO'Q oyga tushgan to'lovlar (sinovdagi avans, teglanmagan eski to'lov) — qarzni yopmaydi. */
export interface UnmatchedPayments {
  count: number
  amount: number
}

export interface DebtReport {
  from: string
  to: string
  months: string[]
  dueDay: number
  includeArchived: boolean
  total: DebtTotals
  active: DebtTotals
  archived: DebtTotals
  buckets: DebtAgeBucket[]
  rows: DebtRow[]
  unmatched: UnmatchedPayments
}

export interface DebtReportQuery {
  from?: string
  to?: string
  includeArchived?: boolean
  groupId?: string
  teacherId?: string
  minAge?: number
}

export async function getDebtReport(q: DebtReportQuery): Promise<DebtReport> {
  const { data } = await api.get<DebtReport>('/admin/reports/debts', { params: q })
  return data
}

export interface TariffPrice {
  price: number
  students: number
  partial: number
}

export interface TariffGroup {
  groupId: string
  groupName: string
  courseName: string
  teacherId: string
  teacherName: string
  isArchived: boolean
  students: number
  prices: TariffPrice[]
  planned: number
  discount: number
  net: number
  collected: number
  debt: number
  collectionPct: number
  debtors: number
  weak: boolean
}

export interface TariffTeacher {
  teacherId: string
  teacherName: string
  groups: number
  students: number
  planned: number
  discount: number
  net: number
  collected: number
  debt: number
  collectionPct: number
  weakGroups: number
}

export interface TariffReport {
  month: string
  weakPct: number
  totals: DebtTotals
  groups: TariffGroup[]
  teachers: TariffTeacher[]
}

export async function getTariffReport(month: string, teacherId?: string): Promise<TariffReport> {
  const { data } = await api.get<TariffReport>('/admin/reports/tariffs', {
    params: { month, teacherId: teacherId || undefined },
  })
  return data
}

export type InvoiceStatus = 'paid' | 'partial' | 'unpaid'

export interface InvoiceRow {
  id: string
  date: string
  month: string
  dueDate: string
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  courseName: string
  amount: number
  discount: number
  net: number
  paid: number
  remaining: number
  status: InvoiceStatus
  overdueDays: number
  isArchived: boolean
}

export interface InvoiceList {
  from: string
  to: string
  dueDay: number
  totalCount: number
  page: number
  pageSize: number
  totals: DebtTotals
  paidCount: number
  partialCount: number
  unpaidCount: number
  overdueCount: number
  items: InvoiceRow[]
  unmatched: UnmatchedPayments
}

export interface InvoiceQuery {
  from?: string
  to?: string
  status?: InvoiceStatus | ''
  overdue?: boolean
  q?: string
  groupId?: string
  page?: number
  pageSize?: number
}

export async function getInvoices(q: InvoiceQuery): Promise<InvoiceList> {
  const { data } = await api.get<InvoiceList>('/admin/reports/invoices', {
    params: { ...q, status: q.status || undefined, q: q.q?.trim() || undefined },
  })
  return data
}
