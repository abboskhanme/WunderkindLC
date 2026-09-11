import { api, USE_MOCK } from '../client'
import { messageTokens as fallbackTokens } from '@/config/messageTemplates'

/**
 * Avto xabarlar (avtomatik xabar qoidalari) servisi.
 *
 * Backend PARALLEL yozilmoqda — bu yerdagi turlar/endpointlar kelishilgan KONTRAKT:
 *   GET    /admin/auto-messages/triggers  → AutoMessageTrigger[]  (katalog: qanday hodisalar bor)
 *   GET    /admin/auto-messages           → AutoMessageRule[]      (sozlangan qoidalar)
 *   POST   /admin/auto-messages           → AutoMessageRule        (id/createdAt siz body)
 *   PUT    /admin/auto-messages/{id}      → AutoMessageRule
 *   DELETE /admin/auto-messages/{id}      → 204
 */

/** Avto xabar hodisasi (trigger) — katalog elementi. */
export interface AutoMessageTrigger {
  /** Barqaror kalit (masalan "lead_new", "payment", "lesson_reminder") */
  key: string
  /** Ko'rsatiladigan nom */
  label: string
  /** Qisqa izoh (qachon ishga tushadi) */
  description: string
  /** Bo'lim: "Lidlar" | "O'quv jarayoni" | "Moliya" | "Boshqa" (eski backendda kelmasligi mumkin) */
  category?: string
  /** Shu hodisada ishlaydigan o'rinbosarlar (masalan "{fish}", "{summa}") */
  tokens: string[]
  /** Qaysi kanallar shu hodisa uchun mavjud */
  channels: { sms: boolean; push: boolean; telegram: boolean }
  /** Reja bo'yicha yuborishni qo'llab-quvvatlaydimi (har kuni / har oy) */
  supportsSchedule: boolean
  /** Yuborish qamrovi (dars boshida / to'ldirilmagan bo'lsa / hammasiga) mavjudmi */
  supportsSendScope: boolean
  /** Mumkin bo'lgan auditoriya qiymatlari (masalan ["parents","students"]) */
  audiences: string[]
  /** Standart auditoriya (yangi qoida yaratishda) */
  defaultAudience: string
  /** Standart xabar shabloni (yangi qoida yaratishda — bo'sh shablon backend tomonidan rad etiladi) */
  defaultTemplate: string
  /** Matn IXTIYORIY — bo'sh qoldirilsa tizim matnni o'zi tuzadi (qarzdorlik eslatmasi kabi). */
  templateOptional?: boolean
}

/** Bitta avto xabar qoidasi. */
export interface AutoMessageRule {
  id: string
  /** Qaysi hodisaga bog'langan (AutoMessageTrigger.key) */
  trigger: string
  /** Qoida nomi (ixtiyoriy yorliq) */
  name: string
  enabled: boolean
  sendSms: boolean
  sendPush: boolean
  sendTelegram: boolean
  audience: string
  /** Xabar matni (o'rinbosarlar bilan) */
  template: string
  /** Dars/hodisadan oldin/keyin surilish (daqiqa) */
  offsetMinutes: number
  /** Yuborish qamrovi: "lesson_start" | "not_filled" | "all" (yoki bo'sh) */
  sendScope: string
  /** Reja turi: "daily" | "monthly" (yoki bo'sh) */
  scheduleType: string
  /** Reja vaqti "HH:mm" */
  scheduleTime: string
  /** Oylik rejada — oyning nechanchi kuni (1-28) */
  scheduleDayOfMonth: number
  createdAt: string
  /** SMS qaysi orqali yuborilsin: "eskiz" (standart) | "local" (CTI agent telefonidan). */
  smsProvider: string
}

/** Yangi qoida yaratish / tahrirlash uchun (id va createdAt yo'q). */
export type AutoMessageRuleInput = Omit<AutoMessageRule, 'id' | 'createdAt'>

/** Hodisalar katalogi. */
export async function getAutoMessageTriggers(): Promise<AutoMessageTrigger[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AutoMessageTrigger[]>('/admin/auto-messages/triggers')
  return data
}

/** Sozlangan qoidalar. */
export async function getAutoMessageRules(): Promise<AutoMessageRule[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<AutoMessageRule[]>('/admin/auto-messages')
  return data
}

/** Yangi qoida yaratish. */
export async function createAutoMessageRule(input: AutoMessageRuleInput): Promise<AutoMessageRule> {
  const { data } = await api.post<AutoMessageRule>('/admin/auto-messages', input)
  return data
}

/** Qoidani yangilash. */
export async function updateAutoMessageRule(
  id: string,
  input: AutoMessageRuleInput,
): Promise<AutoMessageRule> {
  const { data } = await api.put<AutoMessageRule>(`/admin/auto-messages/${id}`, input)
  return data
}

/** Qoidani o'chirish. */
export async function deleteAutoMessageRule(id: string): Promise<void> {
  await api.delete(`/admin/auto-messages/${id}`)
}

/** Keng tarqalgan hodisalar uchun tayyor SMS habarlar to'plamini yaratadi (mavjud SMS qoidalari o'tkazib yuboriladi). */
export async function seedStandardSms(): Promise<{ created: number }> {
  const { data } = await api.post<{ created: number }>('/admin/auto-messages/seed-sms')
  return data
}

/* ---------- Tokenlar (o'rinbosarlar) katalogi ---------- */

/** Server tomonidan e'lon qilingan o'rinbosar (token). */
export interface MessageToken {
  /** Masalan "{ism}" */
  token: string
  /** Ko'rsatiladigan yorliq (masalan "O'quvchi ismi") */
  label: string
  /** Guruh: "student" | "lead" | "common" | "event" */
  group: string
}

/**
 * Tokenlar katalogi — serverdan. Server javob bermasa (eski backend),
 * `config/messageTemplates.ts` dagi lokal ro'yxat FAQAT fallback sifatida ishlatiladi.
 */
export async function getMessageTokens(): Promise<MessageToken[]> {
  if (!USE_MOCK) {
    try {
      const { data } = await api.get<MessageToken[]>('/admin/auto-messages/tokens')
      if (Array.isArray(data) && data.length > 0) return data
    } catch {
      // fallbackka o'tamiz
    }
  }
  return fallbackTokens.map((t) => ({ token: t.token, label: t.label, group: 'common' }))
}

/* ---------- UI yorliqlari ---------- */

/** Auditoriya kalitiga o'zbekcha yorliq. */
export function audienceLabel(a: string): string {
  const map: Record<string, string> = {
    all: 'Barchasi',
    parents: 'Ota-onalar',
    parent: 'Ota-ona',
    students: "O'quvchilar",
    student: "O'quvchi",
    teachers: "O'qituvchilar",
    teacher: "O'qituvchi",
    lead: 'Lid',
    leads: 'Lidlar',
    debtors: 'Qarzdorlar',
  }
  return map[a] ?? a
}

/** Yuborish qamrovi yorliqlari. */
export const sendScopeOptions: { value: string; label: string }[] = [
  { value: 'lesson_start', label: 'Dars boshida' },
  { value: 'not_filled', label: "To'ldirilmagan bo'lsa" },
  { value: 'all', label: 'Hammasiga' },
]
export const sendScopeLabel = (s: string): string =>
  sendScopeOptions.find((o) => o.value === s)?.label ?? s

/** Reja turi yorliqlari. */
export const scheduleTypeOptions: { value: string; label: string }[] = [
  { value: 'daily', label: 'Har kuni' },
  { value: 'monthly', label: 'Har oy' },
]
export const scheduleTypeLabel = (s: string): string =>
  scheduleTypeOptions.find((o) => o.value === s)?.label ?? s
