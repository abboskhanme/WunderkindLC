import { api } from '../client'

/** Local Call (CTI) — Android agent-telefonlar orqali qo'ng'iroqlar: agentlar, tarix, audio, dial. */

export interface CtiAgent {
  id: string
  login: string
  displayName: string
  isActive: boolean
  isOnline: boolean
  lastSeenAt: string | null
  hasFcmToken: boolean
  /** Biriktirilgan xodim id'si — bo'lsa faqat shu xodim (+ SuperAdmin) ko'radi. */
  staffUserId: string | null
  staffUserName: string
}

export type CtiCallDirection = 'incoming' | 'outgoing' | 'missed'

export interface CtiCall {
  id: string
  agentId: string
  agentName: string
  direction: CtiCallDirection
  remoteNumber: string
  contactName: string
  studentId: string | null
  studentName: string | null
  startedAt: string
  answeredAt: string | null
  endedAt: string | null
  durationSec: number
  hasAudio: boolean
  note: string
}

export interface CtiCallEvent {
  type: 'ringing' | 'answered' | 'ended'
  at: string
}

export interface CtiCallDetail extends CtiCall {
  events: CtiCallEvent[]
  /** Azure diarizatsiya transkripti ("1-suhbatdosh: ..."). Bo'sh — hali qilinmagan. */
  transcript: string
  /** Gemini AI tahlili. Bo'sh — hali qilinmagan. */
  aiAnalysis: string
}

export interface CtiCallFilters {
  agentId?: string
  direction?: '' | CtiCallDirection
  dateFrom?: string
  dateTo?: string
  search?: string
}

export interface CtiCallList {
  total: number
  items: CtiCall[]
}

/** Raqam bo'yicha GURUHLANGAN qator — bitta raqam: nechta qo'ng'iroq + oxirgisi haqida ma'lumot. */
export interface CtiNumberGroup {
  remoteNumber: string
  contactName: string
  studentId: string | null
  studentName: string
  callCount: number
  missedCount: number
  hasAudio: boolean
  lastCallAt: string
  lastDirection: CtiCallDirection
  lastDurationSec: number
  lastAgentName: string
}

export interface CtiNumberGroupList {
  /** Jami NECHTA RAQAM (qo'ng'iroq emas) */
  total: number
  items: CtiNumberGroup[]
}

/** Agentlar ro'yxati (onlayn holat, FCM bor-yo'qligi bilan). */
export async function getCtiAgents(): Promise<CtiAgent[]> {
  const { data } = await api.get<CtiAgent[]>('/cti/agents')
  return data
}

/** Yangi agent-telefon qo'shish (login/parol Android ilovaga kiritiladi). staffUserId — SuperAdmin
 * xohlagan xodimga biriktirishi mumkin (boshqalar yaratsa har doim o'ziga biriktiriladi, server tomonda). */
export async function createCtiAgent(req: {
  login: string
  password: string
  displayName: string
  staffUserId?: string | null
}): Promise<{ id: string }> {
  const { data } = await api.post('/cti/agents', req)
  return data
}

/** Agentni tahrirlash — parol bo'sh qoldirilsa o'zgarmaydi. staffUserId faqat SuperAdmin uchun ishlaydi. */
export async function updateCtiAgent(
  id: string,
  req: { displayName: string; isActive: boolean; password: string; staffUserId?: string | null },
): Promise<void> {
  await api.put(`/cti/agents/${id}`, req)
}

/** Click-to-call — agentga push orqali buyruq (agent oflayn bo'lsa delivered=false). */
export async function dialCtiAgent(
  agentId: string,
  number: string,
): Promise<{ commandId: string; delivered: boolean }> {
  const { data } = await api.post(`/cti/agents/${agentId}/dial`, { number })
  return data
}

/** Agent telefonining SIM-kartasidan ixtiyoriy matnli SMS yuborish (agent oflayn bo'lsa delivered=false). */
export async function sendCtiSms(
  agentId: string,
  number: string,
  text: string,
): Promise<{ commandId: string; delivered: boolean }> {
  const { data } = await api.post(`/cti/agents/${agentId}/sms`, { number, text })
  return data
}

/** Qo'ng'iroqlar tarixi — sahifalab, filtrlar bilan. `number` — aniq raqamning qo'ng'iroqlari. */
export async function getCtiCalls(
  f: CtiCallFilters = {},
  page = 1,
  pageSize = 50,
  number?: string,
): Promise<CtiCallList> {
  const { data } = await api.get<CtiCallList>('/cti/calls', {
    params: {
      agentId: f.agentId || undefined,
      direction: f.direction || undefined,
      dateFrom: f.dateFrom || undefined,
      dateTo: f.dateTo || undefined,
      search: f.search || undefined,
      number: number || undefined,
      page,
      pageSize,
    },
  })
  return data
}

/** Qo'ng'iroqlar RAQAM bo'yicha guruhlangan tarixi — har raqam bitta qator (soni + oxirgisi). */
export async function getCtiCallsGrouped(
  f: CtiCallFilters = {},
  page = 1,
  pageSize = 50,
): Promise<CtiNumberGroupList> {
  const { data } = await api.get<CtiNumberGroupList>('/cti/calls/grouped', {
    params: {
      agentId: f.agentId || undefined,
      direction: f.direction || undefined,
      dateFrom: f.dateFrom || undefined,
      dateTo: f.dateTo || undefined,
      search: f.search || undefined,
      page,
      pageSize,
    },
  })
  return data
}

/** Bitta qo'ng'iroqning to'liq detali (hodisalar vaqt chizig'i bilan). */
export async function getCtiCallDetail(id: string): Promise<CtiCallDetail> {
  const { data } = await api.get<CtiCallDetail>(`/cti/calls/${id}`)
  return data
}

/** Audio yozuvni autentifikatsiya bilan yuklab, pleyer uchun blob URL qaytaradi (bo'shatish chaqiruvchida). */
export async function fetchCtiCallAudioUrl(id: string): Promise<string> {
  const res = await api.get(`/cti/calls/${id}/audio`, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}

/** Qo'ng'iroqqa izoh saqlash. */
export async function updateCtiCallNote(id: string, note: string): Promise<void> {
  await api.put(`/cti/calls/${id}/note`, { note })
}

/** Audio yozuvni Azure Speech (diarizatsiya) orqali so'zlovchilar ajratilgan transkriptga o'giradi. */
export async function transcribeCtiCall(id: string): Promise<{ transcript: string }> {
  const { data } = await api.post(`/cti/calls/${id}/transcribe`, {})
  return data
}

/** Transkriptni Gemini AI bilan tahlil qiladi (avval transkript bo'lishi shart). */
export async function analyzeCtiCall(id: string): Promise<{ analysis: string }> {
  const { data } = await api.post(`/cti/calls/${id}/analyze`, {})
  return data
}

/** Berilgan raqamga yuborilgan SMS (Eskiz+Local) — raqam tarixida qo'ng'iroqlar bilan birga ko'rsatish uchun. */
export interface CtiSmsHistoryItem {
  id: string
  message: string
  status: string
  /** Yuborish manbai: "eskiz" | "local". */
  provider: string
  createdAt: string
}
export async function getCtiSmsForNumber(number: string): Promise<CtiSmsHistoryItem[]> {
  const { data } = await api.get<CtiSmsHistoryItem[]>('/cti/sms', { params: { number } })
  return data
}
