import { api, USE_MOCK } from '../client'
import type { Relation } from '@/lib/relations'

/* ============================================================
   TELEGRAM XABARLAR (CRM upgrade Phase 2) — `api/admin/messaging/*`.
   Server: `MessagingController` (ruxsat `messages.broadcast`, o'qish ham).
   Hamma ota-ona/o'quvchi xabari OUTBOX orqali ketadi: oldindan sanoq, oluvchi bo'yicha
   jurnal, «✅ Qabul qildim» tugmasi, bloklagan chatlar, bitta biriktirma.
   ============================================================ */

export type AudienceKind = 'active' | 'archive' | 'selected' | 'teachers'
export type MemberStatus = 'active' | 'trial' | 'frozen'

/** Kimga — server `AudienceSpec` bilan AYNAN bir xil. */
export interface AudienceSpec {
  kind: AudienceKind
  groupIds?: string[]
  courseIds?: string[]
  teacherId?: string
  memberStatuses?: MemberStatus[]
  onlyDebtors?: boolean
  studentIds?: string[]
  relations?: Relation[]
  archivedFrom?: string
  archivedTo?: string
  archiveReason?: string
  teacherIds?: string[]
}

export type CampaignKind = 'broadcast' | 'archive' | 'debt'

export interface PreviewRequest {
  template: string
  audience?: AudienceSpec
  studentId?: string
  attachmentId?: string
  /** Avto xabar qoidasi preview'i (hodisa sarlavhasi va namuna qiymatlari). */
  trigger?: string
  /** Kampaniya turi (sarlavha: e'lon / arxiv / qarz). */
  kind?: CampaignKind
}

export interface PreviewResult {
  html: string
  plain: string
  visibleLength: number
  mode: 'text' | 'caption' | 'separate'
  errors: string[]
  warnings: string[]
  sample: { studentId: string; fullName: string; relation: string } | null
}

export interface AudienceCount {
  students: number
  chats: number
  messages: number
  byRelation: { relation: string; chats: number }[]
  notConnected: number
  blocked: number
  perChat: boolean
  sampleNames: string[]
}

export interface MessageAttachment {
  id: string
  kind: 'photo' | 'video' | 'document'
  fileName: string
  sizeBytes: number
  contentType: string
}

export interface CreateCampaignRequest {
  kind: CampaignKind
  audience: AudienceSpec
  template: string
  attachmentId?: string | null
  ackRequired: boolean
}

export interface CampaignCreated {
  id: string
  messages: number
  chats: number
  students: number
}

export interface CampaignRow {
  id: string
  kind: string
  title: string
  textPlain: string
  senderName: string
  createdAt: string
  status: string
  total: number
  sent: number
  failed: number
  blocked: number
  pending: number
  acked: number
  ackRequired: boolean
  hasAttachment: boolean
}

export interface CampaignDetail extends CampaignRow {
  html: string
  attachment: { id: string; kind: string; fileName: string } | null
  audience: AudienceSpec | null
}

export interface Paged<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

export interface CampaignDeliveryRow {
  deliveryId: string
  relation: string
  recipientName: string
  status: string
  sentAt: string | null
  ackAt: string | null
  error: string
}

export interface CampaignStudentDeliveries {
  studentId: string
  studentName: string
  groupNames: string[]
  rows: CampaignDeliveryRow[]
}

export interface AutoStatsRow {
  date: string
  trigger: string
  label: string
  total: number
  sent: number
  failed: number
  blocked: number
  acked: number
}

export interface Debtor {
  studentId: string
  fullName: string
  totalDebt: number
  items: { groupId: string; groupName: string; courseName: string; debt: number }[]
  telegram: { chats: number; relations: string[] }
  lastReminder: { at: string; status: string; ackAt: string | null } | null
}

const BASE = '/admin/messaging'

const EMPTY_COUNT: AudienceCount = {
  students: 0, chats: 0, messages: 0, byRelation: [], notConnected: 0, blocked: 0, perChat: true, sampleNames: [],
}

/** Server preview — xato shablon 400 EMAS, `errors` ichida keladi. */
export async function previewMessage(req: PreviewRequest, signal?: AbortSignal): Promise<PreviewResult> {
  if (USE_MOCK)
    return { html: req.template, plain: req.template, visibleLength: req.template.length, mode: 'text', errors: [], warnings: [], sample: null }
  const { data } = await api.post<PreviewResult>(`${BASE}/preview`, req, { signal })
  return data
}

/** Yuborishdan OLDINGI sanoq — `campaigns` bilan AYNAN bir xil rejalashtiruvchi (son = qatorlar). */
export async function countAudience(
  audience: AudienceSpec, template: string, kind?: CampaignKind, signal?: AbortSignal,
): Promise<AudienceCount> {
  if (USE_MOCK) return EMPTY_COUNT
  const { data } = await api.post<AudienceCount>(`${BASE}/audience/count`, { audience, template, kind }, { signal })
  return data
}

/** Bitta fayl (rasm/video/hujjat) — server tur, hajm va mazmunni (magic bytes) tekshiradi. */
export async function uploadMessageAttachment(file: File, onProgress?: (pct: number) => void): Promise<MessageAttachment> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<MessageAttachment>(`${BASE}/attachments`, form, {
    headers: { 'Content-Type': 'multipart/form-data' },
    // Katta video (50 MB) — umumiy 120 s timeout yetmasligi mumkin.
    timeout: 600_000,
    onUploadProgress: (e) => {
      if (onProgress && e.total) onProgress(Math.round((e.loaded / e.total) * 100))
    },
  })
  return data
}

/** Biriktirma faylini blob URL qilib oladi (rasm eskizi uchun). Chaqiruvchi `URL.revokeObjectURL` qiladi. */
export async function fetchAttachmentObjectUrl(id: string): Promise<string> {
  const res = await api.get(`${BASE}/attachments/${id}/content`, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}

export async function createCampaign(req: CreateCampaignRequest): Promise<CampaignCreated> {
  const { data } = await api.post<CampaignCreated>(`${BASE}/campaigns`, req)
  return data
}

export async function getCampaigns(params: {
  kind?: string; from?: string; to?: string; page?: number; pageSize?: number
}): Promise<Paged<CampaignRow>> {
  if (USE_MOCK) return { items: [], total: 0, page: 1, pageSize: 50 }
  const { data } = await api.get<Paged<CampaignRow>>(`${BASE}/campaigns`, { params, cache: false })
  return data
}

export async function getCampaign(id: string): Promise<CampaignDetail> {
  const { data } = await api.get<CampaignDetail>(`${BASE}/campaigns/${id}`, { cache: false })
  return data
}

export async function getCampaignDeliveries(id: string, params: {
  status?: string; q?: string; page?: number; pageSize?: number
}): Promise<Paged<CampaignStudentDeliveries>> {
  const { data } = await api.get<Paged<CampaignStudentDeliveries>>(`${BASE}/campaigns/${id}/deliveries`, {
    params, cache: false,
  })
  return data
}

export async function cancelCampaign(id: string): Promise<{ cancelled: number }> {
  const { data } = await api.post<{ cancelled: number }>(`${BASE}/campaigns/${id}/cancel`)
  return data
}

export async function getAutoStats(from: string, to: string): Promise<AutoStatsRow[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AutoStatsRow[]>(`${BASE}/auto-stats`, { params: { from, to }, cache: false })
  return data
}

export async function getDebtors(params: {
  groupId?: string; courseId?: string; teacherId?: string; minDebt?: number; memberStatus?: 'active' | 'frozen' | ''
}): Promise<Debtor[]> {
  if (USE_MOCK) return []
  const clean = Object.fromEntries(Object.entries(params).filter(([, v]) => v !== undefined && v !== '' && v !== null))
  const { data } = await api.get<Debtor[]>(`${BASE}/debtors`, { params: clean, cache: false })
  return data
}

/* ---------- Qo'lda shablonlar (CRUD — `messaging/templates`) ---------- */

export interface MessagingTemplate {
  id: string
  name: string
  text: string
  order: number
}

export async function getMessagingTemplates(): Promise<MessagingTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessagingTemplate[]>(`${BASE}/templates`)
  return data
}

export async function createMessagingTemplate(req: { name: string; text: string; order?: number }): Promise<MessagingTemplate> {
  const { data } = await api.post<MessagingTemplate>(`${BASE}/templates`, req)
  return data
}

export async function updateMessagingTemplate(
  id: string, req: { name: string; text: string; order?: number },
): Promise<MessagingTemplate> {
  const { data } = await api.put<MessagingTemplate>(`${BASE}/templates/${id}`, req)
  return data
}

export async function deleteMessagingTemplate(id: string): Promise<void> {
  await api.delete(`${BASE}/templates/${id}`)
}
