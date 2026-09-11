import { api } from '../client'

/**
 * O'QUVCHINI USHLAB TURISH BONUSI — "O'quvchilar → Bonus hisoboti" bo'limi API'si.
 *
 * O'quvchi belgilangan muddat (default 6 oy) uzluksiz o'qib to'lasa, uni o'qitgan
 * o'qituvchi(lar)ga bonus ajratiladi — o'qigan oylar nisbatida. Oylik holatlar hech qayerda
 * SAQLANMAYDI: har so'rovda qayta hisoblanadi (kechikkan to'lov kiritilsa katak o'z-o'zidan
 * "to'liq" ga aylanadi). Faqat BERILGAN bonus saqlanadi.
 *
 * DIQQAT: sikl har FAN uchun ALOHIDA yuritiladi — hisobot qatorining kaliti `(studentId, courseId)`.
 * Bir o'quvchi ikki fanga qatnasa, hisobotda ikkita mustaqil qator chiqadi. Bir fan ICHIDA guruh
 * almashtirish siklni uzmaydi.
 *
 * Yana bir qoida: `(o'qituvchi, o'quvchi)` juftligi umr bo'yi BITTA bonus oladi.
 */

/** Oy katagi holati: ✅ to'liq · ⏳ qarzdor · 📄 hisob yozilmagan · ❄️ muzlatilgan · 🚪 a'zolik yo'q */
export type RetentionState = 'paid' | 'debt' | 'nocharge' | 'frozen' | 'gone'
/** Qator holati: boshlanmagan · yo'lda · tayyor · uzilgan · bonus berilgan (bloklangan) */
export type RetentionStatus = 'notstarted' | 'progress' | 'ready' | 'broken' | 'blocked'

export interface RetentionMonthCell {
  month: string
  state: RetentionState
  charged: number
  paid: number
  teacherId: string
  teacherName: string
  /** Sanoqqa kirdimi (+1) */
  counted: boolean
}

export interface RetentionShare {
  teacherId: string
  teacherName: string
  /** Shu o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin — parallel guruhlar) */
  months: number
  amount: number
  /**
   * true — shu o'qituvchi bu o'quvchi orqali ALLAQACHON bonus olgan (bir juftlik = bitta bonus).
   * Bunda ulushi 0 bo'ladi, vazni qolgan o'qituvchilarga qayta taqsimlanadi; `months` esa nega
   * ro'yxatda turgani ko'rinsin uchun haqiqiy qiymat bilan keladi.
   */
  alreadyAwarded: boolean
}

export interface RetentionAward {
  id: string
  studentId: string
  studentName: string
  /** Qaysi fan bo'yicha berilgan (nomi snapshot; juda eski yozuvlarda bo'sh bo'lishi mumkin) */
  courseId: string
  courseName: string
  cycleNo: number
  periodFrom: string
  periodTo: string
  totalAmount: number
  status: 'given' | 'cancelled'
  cancelReason: string
  createdAt: string
  givenBy: string
  note: string
  shares: RetentionShare[]
}

/** Bosiladigan havola: nomi ko'rinadi, bosilganda id bo'yicha profilga o'tiladi. */
export interface RetentionRef {
  id: string
  name: string
}

export interface RetentionRow {
  studentId: string
  fullName: string
  /** Sikl kaliti — (studentId, courseId). Kursi biriktirilmagan eski guruhda — guruh id'si. */
  courseId: string
  courseName: string
  /** Shu fandagi faol guruhlar (bosilsa guruh sahifasiga o'tiladi) */
  groups: RetentionRef[]
  /** Shu siklda o'qitgan o'qituvchi(lar); sikl boshlanmagan bo'lsa — hozirgi o'qituvchi */
  teachers: RetentionRef[]
  days: string
  startMonth: string
  cycleNo: number
  months: RetentionMonthCell[]
  counted: number
  required: number
  status: RetentionStatus
  statusNote: string
  isArchived: boolean
  /** Taxminiy taqsimot (faqat status="ready" bo'lganda to'ladi) */
  shares: RetentionShare[]
  /** Shu o'quvchiga avval berilgan bonuslar */
  awards: RetentionAward[]
}

export interface RetentionSettings {
  monthsRequired: number
  maxGapMonths: number
  defaultAmount: number
}

export interface RetentionReport {
  rows: RetentionRow[]
  settings: RetentionSettings
  readyCount: number
}

export interface TeacherRetentionBonus {
  awardId: string
  studentId: string
  studentName: string
  courseName: string
  periodFrom: string
  periodTo: string
  months: number
  amount: number
  givenAt: string
  givenBy: string
  status: 'given' | 'cancelled'
}

/** O'qituvchida oylari to'planayotgan, hali bonus berilmagan (o'quvchi × fan) sikli. */
export interface TeacherRetentionProgress {
  studentId: string
  studentName: string
  courseId: string
  courseName: string
  groupNames: string
  counted: number
  required: number
  /** Shu SIKLDA aynan shu o'qituvchida o'tgan oylar (kasrli bo'lishi mumkin — parallel guruhlar) */
  myMonths: number
  status: RetentionStatus
  statusNote: string
  /** true — bu o'quvchi orqali allaqachon bonus olgan, bu sikldan unga bonus tegmaydi */
  alreadyAwarded: boolean
}

export interface TeacherRetentionSummary {
  total: number
  count: number
  items: TeacherRetentionBonus[]
  /** DIQQAT: backend `null` yuborishi mumkin — doim `?? []` bilan o'qing. */
  inProgress?: TeacherRetentionProgress[] | null
}

export async function getRetentionReport(): Promise<RetentionReport> {
  const { data } = await api.get<RetentionReport>('/admin/retention-bonus')
  return data
}

/** Bonus berish — HAR FAN uchun alohida, shuning uchun `courseId` MAJBURIY. */
export async function giveRetentionBonus(payload: {
  studentId: string
  courseId: string
  totalAmount: number
  shares: { teacherId: string; amount: number; months: number }[]
  note?: string
}): Promise<{ id: string }> {
  const { data } = await api.post<{ id: string }>('/admin/retention-bonus/awards', payload)
  return data
}

/**
 * Bonusni bekor qilish. DIQQAT: bekor qilish sanoqni QAYTARMAYDI va o'qituvchini bloklangan
 * qoldiradi — ya'ni shu o'quvchi orqali o'sha o'qituvchiga qayta bonus berib bo'lmaydi.
 */
export async function cancelRetentionBonus(awardId: string, reason?: string): Promise<void> {
  await api.post(`/admin/retention-bonus/awards/${awardId}/cancel`, { reason })
}

/** Uzilgan siklni yangi oydan qayta boshlash — FAQAT ko'rsatilgan fan uchun. */
export async function restartRetentionCycle(
  studentId: string,
  courseId: string,
  startMonth: string,
): Promise<void> {
  await api.post(`/admin/retention-bonus/students/${studentId}/restart`, { courseId, startMonth })
}

/**
 * Bitta o'quvchining bonus holati — o'quvchi profilidagi «Bonus» bo'limi uchun.
 * FAQAT admin/superadmin ochadi (server 403 qaytaradi) — bonus o'qituvchi haqiga taalluqli.
 */
export async function getStudentRetention(studentId: string): Promise<RetentionReport> {
  const { data } = await api.get<RetentionReport>(`/admin/retention-bonus/student/${studentId}`)
  return data
}

export async function getTeacherRetentionBonuses(
  teacherId: string,
): Promise<TeacherRetentionSummary> {
  const { data } = await api.get<TeacherRetentionSummary>(
    `/admin/retention-bonus/teacher/${teacherId}`,
  )
  return data
}

export async function getRetentionSettings(): Promise<RetentionSettings> {
  const { data } = await api.get<RetentionSettings>('/admin/retention-bonus/settings')
  return data
}

export async function saveRetentionSettings(
  payload: RetentionSettings,
): Promise<RetentionSettings> {
  const { data } = await api.put<RetentionSettings>('/admin/retention-bonus/settings', payload)
  return data
}

export async function exportRetentionReport(): Promise<void> {
  const res = await api.get('/admin/retention-bonus/export', { responseType: 'blob' })
  const cd = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = match?.[1] ?? `bonus_hisobot_${new Date().toISOString().slice(0, 10)}.xlsx`
  a.click()
  URL.revokeObjectURL(href)
}

/**
 * Jami summani o'qigan OYLAR nisbatida o'qituvchilar orasida bo'ladi — serverdagi
 * taqsimot bilan bir xil qoida.
 *
 * DIQQAT: taqsimotga faqat BLOKLANMAGAN (`alreadyAwarded === false`) ulushlar kiradi —
 * allaqachon bonus olgan o'qituvchining ulushi 0 bo'lib qoladi, uning vazni qolganlarga
 * qayta taqsimlanadi. Yaxlitlash qoldig'i eng katta `months` ga qo'shiladi, shunda yig'indi
 * jami summaga ANIQ teng chiqadi. Admin summani o'zgartirganda modal shu bilan qayta hisoblaydi.
 */
export function splitByMonths(shares: RetentionShare[], total: number): RetentionShare[] {
  const active = shares.filter((s) => !s.alreadyAwarded)
  const totalMonths = active.reduce((s, x) => s + x.months, 0)
  // Bo'ladigan hech kim yo'q (yoki oylar 0) — hammasi 0 bo'lib qolsin.
  if (active.length === 0 || totalMonths <= 0) return shares.map((s) => ({ ...s, amount: 0 }))

  const out = shares.map((s) => ({
    ...s,
    amount: s.alreadyAwarded ? 0 : Math.round((total * s.months) / totalMonths),
  }))
  const diff = total - out.reduce((s, x) => s + x.amount, 0)
  if (diff !== 0) {
    const open = out.filter((s) => !s.alreadyAwarded)
    const biggest = open.reduce((a, b) => (b.months > a.months ? b : a), open[0])
    biggest.amount += diff
  }
  return out
}
