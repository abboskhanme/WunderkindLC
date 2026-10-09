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
