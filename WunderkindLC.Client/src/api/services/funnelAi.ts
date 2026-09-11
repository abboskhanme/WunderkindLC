import { api } from '../client'
import type { DayCount, LeadStageCount } from '@/types'

/**
 * VORONKA AI TAHLILI — "O'quv bo'limi → Formalar" bo'limidagi ikkala statistika uchun YAGONA
 * shartnoma: lid formalari (`/admin/forms/statistika`) va daraja testlari
 * (`/admin/level-tests/stats`).
 *
 * <p>Ikkala bo'lim ham bir xil savolga javob beradi — «qaysi kanal/test haqiqiy o'quvchi va PUL
 * keltiryapti» — shuning uchun AI tahlilining SHAKLI ham bitta: raqamlar serverda deterministik
 * hisoblanadi, AI faqat narrativ yozadi va 0..100 baho qo'yadi (`.claude/rules/ai-analysis.md`).
 * Farq faqat `kind` da: u yo'lni va ekrandagi YORLIQLARNI belgilaydi.</p>
 */

/** Qaysi voronka tahlil qilinmoqda. */
export type FunnelAiKind = 'lead-forms' | 'level-tests'

/**
 * `kind` → API yo'li. ⚠️ Xarita ATAYIN bitta joyda: yangi endpoint qo'shilganda yo'l satri
 * fayl bo'ylab sochilib ketmasin.
 */
const basePath: Record<FunnelAiKind, string> = {
  'lead-forms': '/admin/lead-forms',
  'level-tests': '/admin/level-tests',
}

/** Bitta kanal (lid formasi) yoki bitta test bo'yicha voronka. */
export interface FunnelAiChannel {
  /** Forma yoki test nomi. */
  name: string
  /** Manba (lid formalarida — ijtimoiy tarmoq; testlarda bo'sh bo'lishi mumkin). */
  source: string
  submissions: number
  leads: number
  converted: number
  activeStudents: number
  /** Pul to'lagan (takrorsiz) lidlar soni. */
  paid: number
  revenue: number
  convertRate: number
  /** SOTUV konversiyasi — lidlarning necha foizi haqiqatan to'ladi. */
  payRate: number
}

/** Tahlil qilingan paytdagi DETERMINISTIK raqamlar (AI ular ustiga yozadi). */
export interface FunnelAiMetrics {
  kind: FunnelAiKind
  /** Formalar / testlar soni. */
  sources: number
  activeSources: number
  /** Ochilgan (lid formasi) yoki yuborilgan havolalar (daraja testi). */
  views: number
  submissions: number
  /** TAKRORSIZ lidlar — foizlar uchun maxraj. */
  leads: number
  converted: number
  activeStudents: number
  paid: number
  revenue: number
  submitRate: number
  convertRate: number
  payRate: number
  channels: FunnelAiChannel[]
  stages: LeadStageCount[]
  daily: DayCount[]
}

/** Sohaviy baholar (0..100). */
export interface FunnelAiScores {
  hajm: number
  konversiya: number
  sotuv: number
  barqarorlik: number
  umumiy: number
}

/** AI yozgan narrativ — bo'sh maydon ekranda umuman chizilmaydi. */
export interface FunnelAiNarrative {
  umumiy: string
  kanallar: string
  voronka: string
  sifat: string
  pul: string
  ozgarishlar: string
  kuchli: string[]
  zaif: string[]
  xavflar: string[]
  tavsiyalar: string[]
  baholar: FunnelAiScores
  trend: string
}

/** Saqlangan bitta tahlil (raqamlar bilan birga — eski tahlil ochilganda ham to'liq ko'rinadi). */
export interface FunnelAiRecord {
  id: string
  kind: FunnelAiKind
  /** "yyyy-MM-dd" — kuniga bitta tahlil. */
  date: string
  createdAt: string
  model: string
  overallScore: number
  ai: FunnelAiNarrative
  metrics: FunnelAiMetrics
}

/**
 * Tahlil yaratish javobi.
 *
 * ⚠️ `alreadyToday=true` — XATO EMAS: bugungi tahlil allaqachon bor, `record` da o'sha qaytadi
 * (Gemini qayta chaqirilmaydi). Xato faqat `ok=false` da — matn `error` da.
 */
export interface FunnelAiResponse {
  ok: boolean
  alreadyToday: boolean
  record: FunnelAiRecord | null
  error: string | null
}

/** Tahlillar tarixi — eng yangisi birinchi. */
export async function getFunnelAiAnalyses(kind: FunnelAiKind): Promise<FunnelAiRecord[]> {
  const { data } = await api.get<FunnelAiRecord[]>(`${basePath[kind]}/ai-analyses`)
  return data
}

/** Yangi AI tahlil (kuniga bir marta — serverda darvozalangan). */
export async function runFunnelAiAnalysis(kind: FunnelAiKind): Promise<FunnelAiResponse> {
  const { data } = await api.post<FunnelAiResponse>(`${basePath[kind]}/ai-analysis`)
  return data
}
