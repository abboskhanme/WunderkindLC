import type {
  Broadcast,
  MessageClass,
  TelegramParent,
  TelegramStatus,
  TelegramTeacher,
} from '@/types'
import { api, USE_MOCK } from '../client'

/* ============================================================
   "Xabarlar" bo'limi — markazning YAGONA tashqi kanali: Telegram bot.
   SMS kanali, push (Firebase) va guruh chati 2026-10 da olib tashlandi.
   ============================================================ */

/** Xabarlar bo'limi uchun guruhlar ro'yxati (o'quvchi soni, Telegramga ulangan ota-onalar). */
export async function getMessageClasses(): Promise<MessageClass[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessageClass[]>('/admin/messages/classes')
  return data
}

/** Yuborilgan e'lonlar tarixi (ixtiyoriy guruh bo'yicha) */
export async function getBroadcasts(className?: string): Promise<Broadcast[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Broadcast[]>('/admin/messages/broadcasts', {
    params: className ? { className } : undefined,
  })
  return data
}

/** E'lon yuborish so'rovi. Matnda o'rinbosarlar: {fish} {sinf} {qarzdorlik} {balans} {ota-ona} {telefon}. */
export interface SendBroadcastReq {
  /** Qamrov: tanlangan guruh / barcha guruh / tanlangan o'quvchilar/o'qituvchilar */
  scope: 'class' | 'all' | 'selected'
  /** scope === 'class' bo'lganda guruh nomi */
  className?: string
  /** Faqat balansi manfiy (qarzdor) o'quvchilar */
  onlyDebtors: boolean
  /** scope === 'selected' bo'lganda tanlangan o'quvchi id'lari */
  studentIds?: string[]
  /** scope === 'selected' bo'lganda tanlangan o'qituvchi id'lari */
  teacherIds?: string[]
  text: string
}

/** Ota-onalarga Telegram bot orqali e'lon yuborish (har o'quvchiga moslab) */
export async function sendBroadcast(req: SendBroadcastReq): Promise<Broadcast> {
  const { data } = await api.post<Broadcast>('/admin/messages/broadcast', req)
  return data
}

/** Guruh bo'yicha Telegramda ro'yxatdan o'tgan ota-onalar */
export async function getTelegramRegistrations(className?: string): Promise<TelegramParent[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TelegramParent[]>('/admin/messages/telegram/registrations', {
    params: className ? { className } : undefined,
  })
  return data
}

/** Telegramda ro'yxatdan o'tgan o'qituvchilar — "O'qituvchilar" auditoriyasi uchun */
export async function getTelegramTeacherRegistrations(): Promise<TelegramTeacher[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TelegramTeacher[]>('/admin/messages/telegram/registrations/teachers')
  return data
}

/** Telegram bot sozlanganmi (token bormi) — admin UIga ko'rsatish uchun */
export async function getTelegramStatus(): Promise<TelegramStatus> {
  if (USE_MOCK) return { configured: false, botUsername: '' }
  const { data } = await api.get<TelegramStatus>('/admin/messages/telegram/status')
  return data
}

/* ---------- "Tanlab" yuborish uchun oluvchilar ---------- */

/** "Tanlab" yuborish uchun oluvchi (o'quvchi). `telegramChats` — botga ulangan chatlar soni. */
export interface MessageRecipient {
  studentId: string
  fullName: string
  className: string
  /** Shu o'quvchiga ulangan Telegram chatlari soni (0 — xabar bormaydi). */
  telegramChats: number
}
/** "Tanlab" yuborish uchun barcha arxivlanmagan o'quvchilar (ism bo'yicha). */
export async function getMessageRecipients(): Promise<MessageRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessageRecipient[]>('/admin/messages/recipients')
  return data
}

/** "Tanlab" yuborish uchun o'qituvchi oluvchi. */
export interface MessageTeacherRecipient {
  teacherId: string
  fullName: string
  /** O'qituvchiga ulangan Telegram chatlari soni (0 — xabar bormaydi). */
  telegramChats: number
}
/** "Tanlab" yuborish uchun barcha arxivlanmagan o'qituvchilar (ism bo'yicha). */
export async function getMessageTeacherRecipients(): Promise<MessageTeacherRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessageTeacherRecipient[]>('/admin/messages/recipients/teachers')
  return data
}

/* ---------- Birlashgan tayyor matnlar (qo'lda andozalar + avto-xabar qoidalari) ---------- */
/** Birlashgan tayyor matn. source: "manual" (qo'lda andoza) | "auto" (avto-xabar qoidasi matni). */
export interface UnifiedTemplate {
  source: 'manual' | 'auto'
  name: string
  text: string
}
/** Barcha tayyor matnlar — yuborish oynalarida chip sifatida. */
export async function getAllMessageTemplates(): Promise<UnifiedTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<UnifiedTemplate[]>('/admin/messages/templates/all')
  return data
}

/* ---------- Qo'lda xabar andozalari (shablonlar) ---------- */
/** Qo'lda yuboriladigan xabar andozasi. (Avto xabarlar AutoMessageRule'da.) */
export interface MessageTemplate {
  id: string
  name: string
  text: string
  order: number
}
export interface SaveMessageTemplateReq {
  name: string
  text: string
}
export async function getMessageTemplates(): Promise<MessageTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<MessageTemplate[]>('/admin/messaging/templates')
  return data
}
export async function createMessageTemplate(req: SaveMessageTemplateReq): Promise<MessageTemplate> {
  const { data } = await api.post<MessageTemplate>('/admin/messaging/templates', req)
  return data
}
export async function updateMessageTemplate(id: string, req: SaveMessageTemplateReq): Promise<MessageTemplate> {
  const { data } = await api.put<MessageTemplate>(`/admin/messaging/templates/${id}`, req)
  return data
}
export async function deleteMessageTemplate(id: string): Promise<void> {
  await api.delete(`/admin/messaging/templates/${id}`)
}

/**
 * "Tayyor matn" ro'yxatiga TUSHMAYDIGAN hodisalar — matnida QO'LDA to'ldirib bo'lmaydigan
 * token bor.
 *
 * ⚠️ `test_link` andozasida `{link}` bor va u BIR MARTALIK havola: faqat "Daraja testi"
 * bo'limi uni tug'dira oladi. Bu hodisa 2026-10 da katalogdan olib tashlangan (lidga SMS edi),
 * lekin bazada eski qoidasi qolgan bo'lishi mumkin — u ro'yxatga tushmasin.
 *
 * ⚠️ Bu faqat RO'YXAT filtri; matnni qo'lda ham yozish mumkin, shuning uchun haqiqiy
 * darvoza SERVERDA (`MessageTokenCatalog.ForbiddenInManual`).
 */
const NOT_PICKABLE_TRIGGERS = new Set(['test_link'])

/** Lidga oid ESKI avto-xabar hodisalari (SMS bilan birga olib tashlangan) — tanlovga tushmaydi. */
const LEAD_TRIGGERS = new Set(['lead_new', 'trial_reminder', 'test_link', 'test_result'])

/** Tanlash uchun tayyor matn (qo'lda shablon yoki "Xabar yaratish" avto qoidasi matni). */
export interface PickableTemplate {
  name: string
  text: string
}

/**
 * Xabar yuborish oynalarida ko'rsatiladigan TAYYOR matnlar — faqat FOYDALANUVCHI yaratganlari:
 *   • qo'lda andozalar + "Xabar yaratish" avto qoidalari matnlari (o'quvchi/ota-ona hodisalari).
 * Oldindan tayyor (hardcode/seed) matnlar YO'Q — hech narsa yaratilmasa ro'yxat bo'sh bo'ladi.
 */
export async function getPickableTemplates(): Promise<PickableTemplate[]> {
  const { getAutoMessageRules } = await import('./autoMessages')
  const [manual, rules] = await Promise.all([
    getMessageTemplates().catch(() => []),
    getAutoMessageRules().catch(() => []),
  ])
  const manualItems: PickableTemplate[] = manual.map((t) => ({ name: t.name, text: t.text }))
  const autoItems: PickableTemplate[] = rules
    .filter(
      (r) =>
        r.template.trim() !== '' &&
        !NOT_PICKABLE_TRIGGERS.has(r.trigger) &&
        !LEAD_TRIGGERS.has(r.trigger),
    )
    .map((r) => ({ name: r.name, text: r.template }))
  return [...manualItems, ...autoItems]
}
