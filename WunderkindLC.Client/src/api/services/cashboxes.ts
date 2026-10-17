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
  /** Davr ichida boshqa kassadan KO'CHIRIB kelingan / ketgan (kirim-chiqim EMAS — alohida). */
  periodTransferIn: number
  periodTransferOut: number
  createdAt: string
  /** Kassa turi: naqd yoki bank hisob raqami (SPEC-backlog F3). */
  kind: 'cash' | 'bank'
  /** Boshlang'ich qoldiq — `openingDate` kunining boshidagi pul (sana bo'lsa undan oldingi harakatlar hisobga olinmaydi). */
  openingBalance: number
  openingDate: string | null
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
  kind?: 'cash' | 'bank'
  openingBalance?: number
  /** "" — sana olib tashlanadi (butun tarix). */
  openingDate?: string
}

export interface CashboxStatementRow {
  id: string
  date: string
  kind: 'income' | 'expense' | 'transfer_in' | 'transfer_out'
  category: string
  typeName: string | null
  amount: number
  note: string | null
  /** O'quvchi / o'qituvchi / xodim / boshqa kassa nomi. */
  party: string | null
  createdBy: string | null
}

export interface CashboxStatement {
  cashboxId: string
  cashboxName: string
  kind: 'cash' | 'bank'
  from: string
  to: string
  opening: number
  income: number
  expense: number
  transferIn: number
  transferOut: number
  closing: number
  rows: CashboxStatementRow[]
}

/** Kassa ko'chirmasi (`finance.main`): davr boshidagi qoldiq, harakatlar va yakuniy qoldiq. */
export async function getCashboxStatement(id: string, from: string, to: string): Promise<CashboxStatement> {
  const { data } = await api.get<CashboxStatement>(`/admin/cashboxes/${encodeURIComponent(id)}/statement`, {
    params: { from, to },
  })
  return data
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

/* ---------- KASSADAN KASSAGA KO'CHIRISH (billing.md) ---------- */

/** Bitta ko'chirish. Bekor qilingani ham qaytadi (ro'yxatda ustiga chizilgan, hisobga kirmaydi). */
export interface CashboxTransfer {
  id: string
  fromCashboxId: string
  fromCashboxName: string
  toCashboxId: string
  toCashboxName: string
  amount: number
  /** "yyyy-MM-dd" */
  date: string
  note: string | null
  createdAt: string
  createdByName: string | null
  isVoided: boolean
  voidedAt: string | null
  voidedByName: string | null
  voidReason: string | null
}

export interface CashboxTransferPayload {
  fromCashboxId: string
  toCashboxId: string
  amount: number
  /** Bo'sh — bugun. Kelajak sana server tomonidan rad etiladi. */
  date?: string
  note?: string
  /** Oyna kaliti — qayta bosilgan "Saqlash" ikkinchi ko'chirish yozmasin (`lib/requestId.ts`). */
  requestId?: string
}

/** Ko'chirishlar tarixi (`finance.main`). */
export async function getCashboxTransfers(from?: string, to?: string): Promise<CashboxTransfer[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<CashboxTransfer[]>('/admin/cashboxes/transfers', { params: { from, to } })
  return data
}

/** Kassadan kassaga ko'chirish — FAQAT superadmin (server 403). Qoldiq yetmasa — 400 sabab bilan. */
export async function createCashboxTransfer(payload: CashboxTransferPayload): Promise<CashboxTransfer> {
  const { data } = await api.post<CashboxTransfer>('/admin/cashboxes/transfers', payload)
  return data
}

/** Ko'chirishni bekor qilish (qator qoladi, qoldiqlar tiklanadi) — FAQAT superadmin. */
export async function voidCashboxTransfer(id: string, reason?: string): Promise<void> {
  await api.delete(`/admin/cashboxes/transfers/${encodeURIComponent(id)}`, {
    params: { reason: reason || undefined },
  })
}
