import { api } from '../client'

/**
 * CHIQIMLAR TASDIG'I va MAOSH REESTRI (SPEC-backlog-2026-10-17 F3). Shakl serverdagi `ExpenseRequestDto`,
 * `ExpenseApprovalSettingsDto`, `PayrollDto` bilan bir xil.
 */

export type ExpenseRequestStatus = 'pending' | 'approved' | 'rejected' | 'cancelled'

export interface ExpenseRequest {
  id: string
  status: ExpenseRequestStatus
  date: string
  category: string
  typeName: string | null
  amount: number
  note: string | null
  comment: string | null
  teacherName: string | null
  staffName: string | null
  month: string | null
  method: string | null
  cashboxId: string | null
  cashboxName: string | null
  salaryPayKind: 'advance' | 'final' | null
  createdAt: string
  createdBy: string | null
  decidedAt: string | null
  decidedBy: string | null
  decisionNote: string | null
  transactionId: string | null
  /** Joriy foydalanuvchi tasdiqlay/rad eta oladimi (tasdiqlovchi va kirituvchi emas). */
  canDecide: boolean
  isMine: boolean
}

export interface ExpenseApprovalSettings {
  /** 0 — tasdiq o'chiq. */
  threshold: number
  canApprove: boolean
  canEditThreshold: boolean
}

export async function getExpenseRequests(status?: ExpenseRequestStatus): Promise<ExpenseRequest[]> {
  const { data } = await api.get<ExpenseRequest[]>('/admin/finance/expense-requests', { params: { status } })
  return data
}

export async function getExpenseApprovalSettings(): Promise<ExpenseApprovalSettings> {
  const { data } = await api.get<ExpenseApprovalSettings>('/admin/finance/expense-requests/settings')
  return data
}

export async function saveExpenseApprovalThreshold(threshold: number): Promise<ExpenseApprovalSettings> {
  const { data } = await api.put<ExpenseApprovalSettings>('/admin/finance/expense-requests/settings', { threshold })
  return data
}

export async function approveExpense(id: string, note?: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/approve`, { note })
  return data
}

export async function rejectExpense(id: string, note: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/reject`, { note })
  return data
}

export async function cancelExpense(id: string): Promise<ExpenseRequest> {
  const { data } = await api.post<ExpenseRequest>(`/admin/finance/expense-requests/${encodeURIComponent(id)}/cancel`)
  return data
}

/* ---------- Maosh reestri ---------- */

export type PayrollSource = 'ledger' | 'kpi' | 'kpi-pending' | 'fixed' | 'none'

export interface PayrollRow {
  kind: 'teacher' | 'staff'
  id: string
  name: string
  role: string
  source: PayrollSource
  accrued: number
  advance: number
  final: number
  /** Turi belgilanmagan (eski) to'lovlar. */
  other: number
  paid: number
  remaining: number
  status: 'paid' | 'partial' | 'unpaid' | 'none'
  fixedAmount: number | null
  fixedFrom: string | null
}

export interface Payroll {
  month: string
  accrued: number
  paid: number
  remaining: number
  advance: number
  final: number
  teachers: PayrollRow[]
  staff: PayrollRow[]
}

export async function getPayroll(month: string): Promise<Payroll> {
  const { data } = await api.get<Payroll>('/admin/finance/payroll', { params: { month } })
  return data
}

export async function setStaffSalary(userId: string, amount: number, fromMonth: string, note?: string): Promise<void> {
  await api.put(`/admin/finance/payroll/staff/${encodeURIComponent(userId)}/salary`, { amount, fromMonth, note })
}
