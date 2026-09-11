import type { StageColor } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * "BOG'LANISH KERAK" TAXTASINING USTUNLARI — foydalanuvchi boshqaradi (lidlardagi
 * `stages.ts` bilan bir xil naqsh).
 *
 * ⚠️ MUHIM FARQ: bu yerda ustun HOLATNI ALMASHTIRMAYDI. Har ustunning `baseStatus` i bor
 * (new | callback | done | failed) va butun mantiq — navbat, muddat guruhlari, hisobotlar,
 * AI tahlil — avvalgidek AYNAN SHU holat bo'yicha ishlaydi. Ya'ni ustun qo'shish hisobotni
 * buzmaydi (`.claude/rules/contacts.md`).
 */
export interface ContactStage {
  id: string
  title: string
  color: StageColor
  order: number
  /** Bazaviy holat — hisobot mantiqi shu bo'yicha. */
  baseStatus: string
  /** Bazaviy holat yorlig'i (serverdan — yagona katalog). */
  baseStatusLabel: string
  /** Tizim ustuni: o'chirilmaydi va bazaviy bosqichi o'zgarmaydi (nomi/rangi tahrirlanadi). */
  isSystem: boolean
  /** Shu ustundagi talablar soni — o'chirish mumkinligini oldindan ko'rsatadi. */
  count: number
}

export interface ContactStagePayload {
  title: string
  color: StageColor
  /** Yangi ustun uchun; tizim ustunida O'ZGARTIRIB bo'lmaydi (server 400 qaytaradi). */
  baseStatus?: string
}

export async function getContactStages(): Promise<ContactStage[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactStage[]>('/admin/contacts/stages')
  return data
}

export async function createContactStage(payload: ContactStagePayload): Promise<ContactStage> {
  const { data } = await api.post<ContactStage>('/admin/contacts/stages', payload)
  return data
}

export async function updateContactStage(
  id: string,
  payload: ContactStagePayload,
): Promise<ContactStage> {
  const { data } = await api.put<ContactStage>(`/admin/contacts/stages/${id}`, payload)
  return data
}

export async function deleteContactStage(id: string): Promise<void> {
  await api.delete(`/admin/contacts/stages/${id}`)
}

/** Ustunlar tartibini saqlash (kelgan id'lar tartibida). */
export async function reorderContactStages(ids: string[]): Promise<void> {
  await api.patch('/admin/contacts/stages/reorder', { ids })
}
