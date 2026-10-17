import { api } from '../client'

export interface ExecMoney {
  tuition: number
  income: number
  expense: number
  net: number
}

export interface ExecCashbox {
  id: string
  name: string
  kind: 'cash' | 'bank' | string
  balance: number
  isArchived: boolean
}

export interface ExecMonth {
  month: string
  newPaying: number
  leftActive: number
  leftNew: number
  leads: number
  leadsConverted: number
  tuition: number
}

export interface ExecutiveDashboard {
  month: string
  thisMonth: ExecMoney
  prevMonth: ExecMoney
  cashTotal: number
  cashboxes: ExecCashbox[]
  debtTotal: number
  debtors: number
  activeStudents: number
  trend: ExecMonth[]
}

/** Rahbariyat analitikasi — FAQAT superadmin (server hard rol). */
export async function getExecutiveDashboard(month?: string): Promise<ExecutiveDashboard> {
  const { data } = await api.get<ExecutiveDashboard>('/admin/analytics/executive', { params: { month } })
  return data
}
