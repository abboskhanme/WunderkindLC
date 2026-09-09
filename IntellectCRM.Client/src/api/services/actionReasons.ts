import { api } from '../client'
import type { ActionReason } from '@/types'

/** Barcha amal sabablari (kategoriya bo'yicha tartiblangan). */
export async function getActionReasons(): Promise<ActionReason[]> {
  const { data } = await api.get<ActionReason[]>('/admin/action-reasons')
  return data
}

/**
 * Serverda RUXSAT ETILGAN kategoriyalar kalitlari (tartibi bilan).
 *
 * "Sabablar" sahifasi kartochkalarni shundan quradi — backendga qo'shilgan yangi kategoriya
 * UI'da o'z-o'zidan paydo bo'ladi (yorlig'i bo'lmasa kalit nomi bilan). Ilgari ro'yxat faqat
 * frontendda edi va `contact`/`archive_student` sahifada UMUMAN ko'rinmasdi.
 */
export async function getActionReasonCategories(): Promise<string[]> {
  const { data } = await api.get<string[]>('/admin/action-reasons/categories')
  return data
}

/**
 * Qaysi kategoriyalarda «nazoratdan tashqari» belgisi ma'noli (server aytadi — ro'yxat ikki
 * joyda ayri ketmasin). Endpoint bo'lmasa bo'sh ro'yxat: checkbox umuman ko'rsatilmaydi.
 */
export async function getOutOfControlCategories(): Promise<string[]> {
  const { data } = await api.get<string[]>('/admin/action-reasons/out-of-control-categories')
  return data
}

export async function createActionReason(
  category: string,
  label: string,
  outOfControl = false,
): Promise<ActionReason> {
  const { data } = await api.post<ActionReason>('/admin/action-reasons', {
    category,
    label,
    outOfControl,
  })
  return data
}

/**
 * ⚠️ `outOfControl` berilmasa server bayroqni TEGMAYDI (null = "o'zgarmadi") — nomni
 * tahrirlash belgini jimgina o'chirib yubormasin.
 */
export async function updateActionReason(
  id: string,
  label: string,
  outOfControl?: boolean,
): Promise<void> {
  await api.put(`/admin/action-reasons/${id}`, { label, outOfControl })
}

export async function deleteActionReason(id: string): Promise<void> {
  await api.delete(`/admin/action-reasons/${id}`)
}
