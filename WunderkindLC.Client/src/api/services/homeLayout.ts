import { api } from '../client'
import type { HomeLayout } from '@/lib/homeWidgets'

/* ---------- BOSH SAHIFA VIDJETLARI (CRM Phase 5) — foydalanuvchining O'Z sozlamasi ---------- */

/** `null` — foydalanuvchi hali sozlamagan (standart ko'rinish). */
export async function getHomeLayout(): Promise<HomeLayout | null> {
  const { data } = await api.get<{ layout: HomeLayout | null }>('/admin/home/layout')
  return data.layout ?? null
}

/** Saqlaydi; server normallashtirilgan sozlamani qaytaradi. */
export async function saveHomeLayout(layout: HomeLayout): Promise<HomeLayout> {
  const { data } = await api.put<HomeLayout>('/admin/home/layout', layout)
  return data
}

/** «Standartga qaytarish». */
export async function resetHomeLayout(): Promise<void> {
  await api.delete('/admin/home/layout')
}
