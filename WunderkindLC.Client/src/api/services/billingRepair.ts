import { api } from '../client'

/**
 * HISOBNI TUZATISH vositasi (FAQAT superadmin — server HARD rol bilan tekshiradi).
 * Aktiv qilib qo'shilganda hisoblanmay qolgan «qo'shilgan oy» (joriy va o'tgan oy).
 * Ikki qadam: `getMissingActivationCharges` HECH NARSA yozmaydi; `applyActivationCharges` — faqat tanlanganlar.
 */
export interface MissingActivationCharge {
  /** Tanlov kaliti — `applyActivationCharges` ga shu qaytariladi. */
  key: string
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  /** Aktivlashtirilgan sana "yyyy-MM-dd". */
  activatedAt: string
  /** Hisoblanadigan oy "yyyy-MM". */
  month: string
  /** Yoziladigan qarz (qisman, chegirma ayirilgan). */
  amount: number
  /** Hozirgi umumiy balans. */
  balance: number
}

export interface ApplyActivationChargesResult {
  applied: number
  total: number
  skipped: number
  errors: string[]
}

export async function getMissingActivationCharges(): Promise<MissingActivationCharge[]> {
  const { data } = await api.get<MissingActivationCharge[]>('/admin/billing-repair/activation-charges')
  return data
}

export async function applyActivationCharges(keys: string[]): Promise<ApplyActivationChargesResult> {
  const { data } = await api.post<ApplyActivationChargesResult>('/admin/billing-repair/activation-charges', { keys })
  return data
}

/**
 * NOTO'G'RI OYGA YOZILGAN TO'LOVLAR (FAQAT superadmin) — to'lov oynasi eng eski qarzdor oyni tanlab qo'ygan va
 * butun to'lov o'sha oyga yozilgan; ortig'i keyingi (joriy oygacha) kam to'langan oylarga ko'chiriladi.
 * `getPaymentMonthRepairs` HECH NARSA yozmaydi; `applyPaymentMonthRepairs` — faqat tanlangan (o'quvchi, guruh).
 * Pul, balans va kassa o'zgarmaydi — faqat to'lov "qaysi oy uchun" tegi.
 */
export interface PaymentMonthMove {
  fromMonth: string
  toMonth: string
  amount: number
}

export interface PaymentMonthRepairRow {
  /** Tanlov kaliti — (o'quvchi, guruh); `applyPaymentMonthRepairs` ga shu qaytariladi. */
  key: string
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  teacherName: string
  moves: PaymentMonthMove[]
  /** Ko'chiriladigan jami summa. */
  total: number
  /** Ko'chirib bo'lmagan ortiqcha (vozvrati bor to'lov) haqida izohlar. */
  notes: string[]
}

export async function getPaymentMonthRepairs(): Promise<PaymentMonthRepairRow[]> {
  const { data } = await api.get<PaymentMonthRepairRow[]>('/admin/billing-repair/payment-months')
  return data
}

export async function applyPaymentMonthRepairs(keys: string[]): Promise<ApplyActivationChargesResult> {
  const { data } = await api.post<ApplyActivationChargesResult>('/admin/billing-repair/payment-months', { keys })
  return data
}
