import { api } from '../client'
import type { AiCheck, AiCheckListItem, AiCheckOverviewRow } from '@/types'

/** Admin: foydalanuvchilar bo'yicha umumiy ko'rinish (kim necha marta ishlatgan). */
export async function getAiCheckOverview(): Promise<AiCheckOverviewRow[]> {
  const { data } = await api.get<AiCheckOverviewRow[]>('/admin/ai-check/overview')
  return data
}

/** AI tekshiruv global sozlamalari. */
export interface AiCheckSettings {
  /** Standart kunlik limit (premium/shaxsiy limiti yo'q o'quvchilar uchun). */
  defaultDailyLimit: number
  /** Bo'lim o'quvchi ilovasida ochiqmi. Default: false (yopiq). */
  enabled: boolean
}

/** Global sozlamalar: standart kunlik limit + ilovadagi ochiq/yopiq bayrog'i. */
export async function getAiCheckSettings(): Promise<AiCheckSettings> {
  const { data } = await api.get<AiCheckSettings>('/admin/ai-check/settings')
  return data
}

export async function saveAiCheckSettings(dailyLimit: number): Promise<void> {
  await api.put('/admin/ai-check/settings', { dailyLimit })
}

/**
 * Bo'limni o'quvchi ilovasida ochish/yopish. ALOHIDA endpoint —
 * kunlik limit saqlash (`saveAiCheckSettings`) bilan aralashmaydi.
 */
export async function setAiCheckEnabled(enabled: boolean): Promise<void> {
  await api.put('/admin/ai-check/enabled', { enabled })
}

/** O'quvchiga limit/premium/blok belgilash. */
export async function saveAiAccess(
  studentId: string,
  dailyLimit: number,
  isPremium: boolean,
  isBlocked: boolean,
): Promise<void> {
  await api.put(`/admin/ai-check/access/${studentId}`, { dailyLimit, isPremium, isBlocked })
}

/** O'quvchining AI tekshiruv tarixi (admin). */
export async function getAiCheckStudentHistory(studentId: string): Promise<AiCheckListItem[]> {
  const { data } = await api.get<AiCheckListItem[]>(`/admin/ai-check/history/${studentId}`)
  return data
}

/** Bitta yozuv (to'liq). */
export async function getAiCheckAdminItem(id: string): Promise<AiCheck> {
  const { data } = await api.get<AiCheck>(`/admin/ai-check/item/${id}`)
  return data
}
