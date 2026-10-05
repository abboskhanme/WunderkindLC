import { api, USE_MOCK } from '../client'

/**
 * KASSALAR (Moliya → «Kassalar», `docs/SPEC-kassalar.md`). Har kirim/chiqim bitta kassaga tushadi,
 * qoldiq tranzaksiyalardan hisoblanadi. Yaratish/tahrirlash/arxivlash — FAQAT superadmin (server 403).
 */

/** Kassa kartasi. `balance` — butun tarix bo'yicha qoldiq; `period*` — tanlangan davr. */
export interface Cashbox {
  id: string
  name: string
  responsibleUserId: string | null
  responsibleName: string | null
  /** «Asosiy kassa» — arxivlanmaydi, mas'uli yo'q to'lovlar shu yerga tushadi. */
  isMain: boolean
  isArchived: boolean
  balance: number
  periodIncome: number
  periodExpense: number
  createdAt: string
}

/** To'lov oynasidagi «Kassa» tanlovi uchun (qoldiqsiz). `mine` — foydalanuvchi o'zi mas'ul. */
export interface CashboxOption {
  id: string
  name: string
  responsibleName: string | null
  mine: boolean
}

export interface CashboxOptions {
  items: CashboxOption[]
  /** Shu foydalanuvchining standart kassasi — tanlovda oldindan belgilanadi. */
  defaultId: string | null
}

export interface CashboxPayload {
  name: string
  responsibleUserId?: string | null
}

/** Faol kassalar + standart kassa (to'lov oynasi). Admin panelidagi har kim o'qiy oladi. */
export async function getCashboxOptions(): Promise<CashboxOptions> {
  if (USE_MOCK) return { items: [], defaultId: null }
  const { data } = await api.get<CashboxOptions>('/admin/cashboxes/options')
  return data
}

/** Kassalar qoldig'i bilan (`finance.main`). */
export async function getCashboxes(from?: string, to?: string): Promise<Cashbox[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Cashbox[]>('/admin/cashboxes', { params: { from, to } })
  return data
}

export async function createCashbox(payload: CashboxPayload): Promise<Cashbox> {
  const { data } = await api.post<Cashbox>('/admin/cashboxes', payload)
  return data
}

export async function updateCashbox(id: string, payload: CashboxPayload): Promise<Cashbox> {
  const { data } = await api.put<Cashbox>(`/admin/cashboxes/${encodeURIComponent(id)}`, payload)
  return data
}

export async function archiveCashbox(id: string): Promise<void> {
  await api.post(`/admin/cashboxes/${encodeURIComponent(id)}/archive`)
}

export async function restoreCashbox(id: string): Promise<void> {
  await api.post(`/admin/cashboxes/${encodeURIComponent(id)}/restore`)
}
