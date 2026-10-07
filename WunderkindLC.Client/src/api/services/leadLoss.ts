import { api } from '../client'

/**
 * «YO'QOTILGAN LIDLAR» — belgilash, tiklash va hisobot (`.claude/rules/crm-leads.md`
 * «YO'QOTILGAN LIDLAR»).
 *
 * ⚠️ Shakl SERVER SHARTNOMASI bilan aynan bir xil: `WunderkindLC.Application/Dtos/LeadLossDtos.cs`.
 * Maydon qo'shish/olib tashlash ikkala tomonda BIRGA qilinadi.
 */

/** Sabablar katalogidagi kategoriya kaliti (serverda `LeadLoss.ReasonCategory`). */
export const LEAD_LOST_REASON_CATEGORY = 'lead_lost'

/** Kanbandagi DOIMIY "Yo'qotilgan" ustunining droppable id'si (bazada bunday bosqich YO'Q). */
export const LOST_COLUMN_ID = '__lost__'

/** Belgilash/tiklashdan keyingi lid holati — ro'yxatdagi qator shu bilan yangilanadi. */
export interface LeadLossState {
  id: string
  stage: string
  lostAt: string | null
  lostReasonId: string | null
  lostReason: string | null
  lostComment: string | null
  lostByName: string | null
  assigneeUserId: string | null
  assigneeName: string | null
}

/** Lidni YO'QOTILGAN deb belgilash. Sabab MAJBURIY — server bo'shini 400 bilan qaytaradi. */
export async function markLeadLost(id: string, reasonId: string, comment: string): Promise<LeadLossState> {
  const { data } = await api.patch<LeadLossState>(`/admin/leads/${id}/lost`, { reasonId, comment })
  return data
}

/**
 * Yo'qotilgan lidni tiklash. `stage` berilsa — o'sha bosqichga (kanbanda boshqa ustunga
 * sudralganda), berilmasa — yo'qotilishdan oldingi o'z bosqichiga.
 */
export async function restoreLead(id: string, stage?: string): Promise<LeadLossState> {
  const { data } = await api.patch<LeadLossState>(`/admin/leads/${id}/restore`, { stage: stage ?? null })
  return data
}

/* ---------- Hisobot ---------- */

export interface LeadLossReason {
  label: string
  count: number
  /** Yo'qotilganlar ichidagi ulush, % */
  share: number
}

/** Manba / mas'ul kesimi: shu guruhdagi JAMI lid va ulardan yo'qotilganlar. */
export interface LeadLossGroup {
  /** Manba nomi yoki xodim id'si ('' = biriktirilmagan) */
  key: string
  label: string
  total: number
  lost: number
  rate: number
}

export interface LeadLossMonth {
  /** "yyyy-MM" */
  month: string
  total: number
  lost: number
  rate: number
}

export interface LeadLossItem {
  id: string
  fullName: string
  createdAt: string
  lostAt: string
  reason: string
  comment: string
  source: string
  manager: string
  lostBy: string
}

/**
 * DAVR — lid KELGAN sana bo'yicha (CRM analitikasidagi bilan bir xil): «shu davrda kelgan
 * lidlarning qanchasini yo'qotdik». Barcha foizlar shu to'plamdan.
 */
export interface LeadLossReport {
  from: string
  to: string
  total: number
  lost: number
  lostRate: number
  converted: number
  byReason: LeadLossReason[]
  bySource: LeadLossGroup[]
  byManager: LeadLossGroup[]
  monthly: LeadLossMonth[]
  recent: LeadLossItem[]
  /** Ro'yxatga sig'magan yo'qotilgan lidlar soni */
  recentTruncated: number
}

export async function getLeadLossReport(from?: string, to?: string): Promise<LeadLossReport> {
  const { data } = await api.get<LeadLossReport>('/admin/leads/lost-report', {
    params: { from: from || undefined, to: to || undefined },
  })
  return data
}
