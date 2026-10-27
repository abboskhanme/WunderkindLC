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
  /** Tizim ustuni: bazaviy bosqichi o'zgarmaydi (nomi/rangi tahrirlanadi); o'chirilsa — YASHIRILADI. */
  isSystem: boolean
  /** Shu ustundagi OCHIQ talablar soni (o'chirishda kartalar asosiy ustunga qaytadi). */
  count: number
  /** O'chirilgan (yashirilgan) tizim ustuni — faqat `getContactStages(true)` da keladi. */
  isHidden?: boolean
}

export interface ContactStagePayload {
  title: string
  color: StageColor
  /** Yangi ustun uchun; tizim ustunida O'ZGARTIRIB bo'lmaydi (server 400 qaytaradi). */
  baseStatus?: string
}

/** `all` — yashirilgan (o'chirilgan tizim) ustunlar ham («Sozlamalar → Service» — qaytarish uchun). */
export async function getContactStages(all = false): Promise<ContactStage[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactStage[]>('/admin/contacts/stages', { params: all ? { all: true } : undefined })
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

/** O'chirish: ichidagi kartalar asosiy ustunga qaytadi; tizim ustuni yashiriladi (server). */
export async function deleteContactStage(id: string): Promise<void> {
  await api.delete(`/admin/contacts/stages/${id}`)
}

/** Yashirilgan tizim ustunini taxtaga qaytarish. */
export async function restoreContactStage(id: string): Promise<ContactStage> {
  const { data } = await api.post<ContactStage>(`/admin/contacts/stages/${id}/restore`)
  return data
}

/** «Sozlamalar → Service»: kelmaganlar (necha dars) va qarzdorlar (oyning qaysi sanasidan) avtomatik navbatga. */
export interface ContactAutoSettings {
  absenceEnabled: boolean
  absenceLessons: number
  debtEnabled: boolean
  debtDay: number
}

export async function getContactAutoSettings(): Promise<ContactAutoSettings> {
  const { data } = await api.get<ContactAutoSettings>('/admin/contacts/settings')
  return data
}

export async function saveContactAutoSettings(payload: ContactAutoSettings): Promise<ContactAutoSettings> {
  const { data } = await api.put<ContactAutoSettings>('/admin/contacts/settings', payload)
  return data
}

/** Ustunlar tartibini saqlash (kelgan id'lar tartibida). */
export async function reorderContactStages(ids: string[]): Promise<void> {
  await api.patch('/admin/contacts/stages/reorder', { ids })
}
