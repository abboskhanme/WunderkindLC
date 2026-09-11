import type { Lead, LeadEvent, LeadEventType, TrialLesson, TrialResult, CrmStats } from '@/types'
import type { ReceiptData } from '@/lib/receipt'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { leadsMock } from '../mock/leads'

export type LeadPayload = Omit<Lead, 'id' | 'stage'>

export async function getLeads(): Promise<Lead[]> {
  if (USE_MOCK) {
    await delay()
    return leadsMock
  }
  const { data } = await api.get<Lead[]>('/admin/leads')
  return data.map((l: any) => ({
    ...l,
    firstLessonAttendance: l.firstLessonAttendance || 'no-lesson',
  }))
}

/**
 * `answers` — «Lid kiritish formasi» QO'SHIMCHA savollariga javoblar: `{ savolId: [javob...] }`.
 *
 * ⚠️ YARATISHDA doim uzatiladi (bo'sh bo'lsa ham) — server majburiy savolni AYNAN shu bilan
 * tekshiradi. TAHRIRLASHDA esa uzatilmasa lidning eski javoblari TEGILMAYDI (o'chirilmaydi).
 */
export type LeadAnswersPayload = Record<string, string[]>

export async function createLead(
  payload: LeadPayload, stage: string, answers: LeadAnswersPayload = {},
): Promise<Lead> {
  if (USE_MOCK) {
    await delay(300)
    return { ...payload, id: uid(), stage }
  }
  const { data } = await api.post<Lead>('/admin/leads', { ...payload, stage, answers })
  return data
}

export async function updateLead(
  id: string, payload: LeadPayload, answers?: LeadAnswersPayload,
): Promise<void> {
  if (USE_MOCK) {
    await delay(300)
    return
  }
  await api.put(`/admin/leads/${id}`, answers ? { ...payload, answers } : payload)
}

export async function updateLeadStage(id: string, stage: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.patch(`/admin/leads/${id}`, { stage })
}

export async function deleteLead(id: string, reasonId?: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/leads/${id}`, { params: reasonId ? { reasonId } : undefined })
}

/* ---------- CRM: tarix (timeline) ---------- */

export async function getLeadEvents(id: string): Promise<LeadEvent[]> {
  if (USE_MOCK) {
    await delay(200)
    return []
  }
  const { data } = await api.get<LeadEvent[]>(`/admin/leads/${id}/events`)
  return data
}

export async function addLeadEvent(id: string, type: LeadEventType, text: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.post(`/admin/leads/${id}/events`, { type, text })
}

/* ---------- CRM: sinov darslari ---------- */

export async function getLeadTrials(id: string): Promise<TrialLesson[]> {
  if (USE_MOCK) {
    await delay(200)
    return []
  }
  const { data } = await api.get<TrialLesson[]>(`/admin/leads/${id}/trials`)
  return data
}

export async function scheduleTrial(
  id: string,
  groupId: string,
  scheduledAt: string,
): Promise<string | null> {
  if (USE_MOCK) {
    await delay(200)
    return null
  }
  const { data } = await api.post<{ trialId?: string }>(`/admin/leads/${id}/trials`, {
    groupId,
    scheduledAt,
  })
  return data?.trialId ?? null
}

/** Lid sinov darsi cheki (to'lovsiz ro'yxat varaqasi) — termal chek chizish/print uchun. */
export async function getTrialReceipt(
  trialId: string,
): Promise<ReceiptData & { settingsJson: string }> {
  const { data } = await api.get<ReceiptData & { settingsJson: string }>(
    `/admin/leads/trials/${trialId}/receipt`,
  )
  return data
}

export async function setTrialResult(trialId: string, result: TrialResult): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.patch(`/admin/leads/trials/${trialId}`, { result })
}

/* ---------- CRM: o'quvchiga aylantirish ---------- */

export async function convertLead(
  id: string,
  body: { enrollmentDate?: string; groupId?: string },
): Promise<{ studentId: string }> {
  if (USE_MOCK) {
    await delay(300)
    return { studentId: uid() }
  }
  const { data } = await api.post<{ studentId: string }>(`/admin/leads/${id}/convert`, body)
  return data
}

/* ---------- CRM: statistika ---------- */

export async function getCrmStats(): Promise<CrmStats> {
  if (USE_MOCK) {
    await delay(300)
    return {
      totalLeads: 0, converted: 0, conversionRate: 0,
      byStage: [], bySource: [], monthly: [], byInterest: [],
    }
  }
  const { data } = await api.get<CrmStats>('/admin/leads/stats')
  return data
}

/* ---------- CRM: analitika (voronka / manbalar / menejerlar) ---------- */

/** Voronkaning bitta bosqichi. */
export interface LeadFunnelStage {
  stageId: string
  title: string
  color: string
  order: number
  /** Shu bosqichga YETIB kelgan lidlar soni. */
  reached: number
  /** Birinchi bosqichga nisbatan foiz (0-100). */
  pct: number
  /**
   * Bosqichda o'rtacha turish vaqti (soat). `null` — o'lchash uchun ma'lumot YETARLI EMAS
   * (tarix yaqinda yozila boshlangan). Buni "0 soat" deb ko'rsatish MUMKIN EMAS.
   */
  avgHours: number | null
  /** Nechta to'liq oraliq o'lchangani — raqamga qanchalik ishonish mumkinligini ko'rsatadi. */
  samples: number
}

/** Manba bo'yicha ulush (donut kesmasi). */
export interface LeadSourceSlice {
  source: string
  label: string
  count: number
  /** Umumiy lidlarga nisbatan foiz (0-100). */
  pct: number
}

/** «Kim qaysi bosqichgacha olib bordi» matritsasining bitta katagi. */
export interface LeadManagerStage {
  stageId: string
  /** Shu menejer AYNAN shu bosqichga olib kelgan takrorsiz lidlar soni. */
  reached: number
}

/** Menejer (sotuvchi) kesimidagi ko'rsatkichlar — sotuv bo'limi KPI jadvali. */
export interface LeadManagerRow {
  userId: string
  name: string
  /** Bosqichlar bo'ylab qilingan harakatlar soni. */
  moves: number
  /** Nechta HAR XIL lid bilan ishlagani (kiritgan yoki ko'chirgan). */
  leads: number
  /** O'quvchiga aylantirgan lidlar. */
  won: number
  /** Shundan nechtasini O'ZI kiritgan. */
  created: number
  /** Aylantirganlaridan nechtasi haqiqatan PUL to'lagan. */
  paid: number
  /** Shu lidlar keltirgan SOF tushum. ⚠️ Faqat AYLANTIRGANga yoziladi — ikki marta sanalmaydi. */
  revenue: number
  /** Bosqichlar kesimi — ustunlar tartibi HAR menejerda bir xil (bo'shlari 0 bilan turadi). */
  stages: LeadManagerStage[]
}

/** Lid KANALI (qayerdan keldi) kesmasi. */
export interface LeadOriginRow {
  /** `form` | `test` | `instagram` | `manual` | `other` */
  key: string
  label: string
  leads: number
  converted: number
  paid: number
  revenue: number
  /** O'quvchiga aylanganlar ulushi (0-100). */
  conversionRate: number
  /** PUL to'laganlar ulushi (0-100) — sotuvning haqiqiy o'lchovi. */
  payRate: number
}

/**
 * «BUTUN CRM MANZARASI» — markazdagi BARCHA lidlar (qo'lda kiritilgani ham).
 *
 * "Formalar" bo'limidagi ikkala statistika ham (lid formalari va daraja testi) faqat O'Z
 * kanalini sanaydi; bu blok ularni butun manzara ichiga qo'yadi. Serverdagi juftligi —
 * `LeadCrmOverview` (ikkala sahifa uchun YAGONA hisob).
 */
export interface CrmOverview {
  leads: number
  converted: number
  paid: number
  revenue: number
  origins: LeadOriginRow[]
  /** Barcha lidlar HOZIR qaysi bosqichda (kanban ustuni). */
  byStage: { stage: string; color: string; leads: number }[]
}

export interface LeadAnalytics {
  from: string
  to: string
  total: number
  converted: number
  /** Konversiya foizi (0-100). */
  conversionRate: number
  /** Davrdagi lidlardan haqiqatan PUL to'laganlari. */
  paid: number
  /** Shu lidlarning sof tushumi (to'lov − vozvrat). */
  revenue: number
  /** SOTUV konversiyasi (0-100): to'lagan / jami lid. */
  payRate: number
  funnel: LeadFunnelStage[]
  sources: LeadSourceSlice[]
  /** Bo'sh bo'lishi MUMKIN — bu xato emas, shunchaki davrda harakat qilgan menejer yo'q. */
  managers: LeadManagerRow[]
  /** Lidlar qaysi kanaldan kelgani (bo'sh kanallar ro'yxatda yo'q). */
  origins: LeadOriginRow[]
}

/**
 * Lidlar analitikasi. `from`/`to` — `YYYY-MM-DD`, ikkalasi ham ixtiyoriy
 * (berilmasa server butun davrni oladi).
 */
export async function getLeadAnalytics(from?: string, to?: string): Promise<LeadAnalytics> {
  const params: Record<string, string> = {}
  if (from) params.from = from
  if (to) params.to = to
  const { data } = await api.get<LeadAnalytics>('/admin/leads/analytics', { params })
  return data
}

/**
 * Lid formasi "Qiziqqan fani" ro'yxati — markazdagi KURSLAR nomlari. Kurslar bo'limi "schedule"
 * ruxsatida bo'lgani uchun CRM xodimiga shu (leads ruxsatidagi) endpoint orqali beriladi.
 */
export async function getLeadCourses(): Promise<string[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<string[]>('/admin/leads/courses')
  return data
}
