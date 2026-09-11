import { api } from '../client'
import type { LeadSource } from '@/types'

/**
 * Lid manbalari ma'lumotnomasi — "O'quv bo'limi → Sabablar" sahifasida boshqariladi,
 * lid formasi va Lidlar filtri shu ro'yxatdan tanlaydi.
 */
export async function getLeadSources(): Promise<LeadSource[]> {
  const { data } = await api.get<LeadSource[]>('/admin/lead-sources')
  return data
}

export async function createLeadSource(name: string): Promise<LeadSource> {
  const { data } = await api.post<LeadSource>('/admin/lead-sources', { name })
  return data
}

/** Nomni o'zgartirish — shu manbaga ega lidlar ham yangi nomga ko'chiriladi (server). */
export async function updateLeadSource(id: string, name: string): Promise<void> {
  await api.put(`/admin/lead-sources/${id}`, { name })
}

export async function deleteLeadSource(id: string): Promise<void> {
  await api.delete(`/admin/lead-sources/${id}`)
}
