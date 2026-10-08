import { api } from '../client'
import type { ArchivedRecord } from '@/types'

/** O'chirilgan yozuvlar (ixtiyoriy turi bo'yicha). */
export async function getArchive(type?: string): Promise<ArchivedRecord[]> {
  const { data } = await api.get<ArchivedRecord[]>('/admin/archive', {
    params: type ? { type } : undefined,
  })
  return data
}

/** Har bir tur uchun o'chirilgan yozuvlar soni. */
export async function getArchiveCounts(): Promise<Record<string, number>> {
  const { data } = await api.get<Record<string, number>>('/admin/archive/counts')
  return data
}

/**
 * Yozuvni tiklash. `message` — tiklangandan keyin nima qilish kerakligi (masalan o'quvchi guruhsiz
 * qaytdi, xodimga parol va rol qayta beriladi) — foydalanuvchiga ko'rsatiladi.
 */
export async function restoreArchive(id: string): Promise<{ message?: string | null }> {
  const { data } = await api.post<{ ok: boolean; message?: string | null }>(`/admin/archive/${id}/restore`)
  return data ?? {}
}

/** Yozuvni butunlay o'chirish. */
export async function deleteArchive(id: string): Promise<void> {
  await api.delete(`/admin/archive/${id}`)
}
