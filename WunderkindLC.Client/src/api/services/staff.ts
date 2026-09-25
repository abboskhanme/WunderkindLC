import type { Staff, Credentials, StaffRoleTemplate } from '@/types'
import { api, USE_MOCK } from '../client'

export interface StaffPayload {
  fullName: string
  position: string
  newPassword?: string
  /** Telefon — xodim botda yangi lid xabarnomasini olishi uchun (leads ruxsati bo'lsa) */
  phone?: string
}

export interface CreateStaffWithTemplatePayload {
  fullName: string
  position: string
  phone?: string
  newPassword?: string
  /** Rolle shabloni kodi (call_operator, cashier, administrator) */
  templateCode?: string
  /** Qo'shimcha ruxsatlari */
  extraPermissions?: string[]
  /**
   * Xodimning ROLI (id). Yaratishda — ruxsatlar roldan olinadi. Tahrirda: berilmasa rol
   * o'zgarmaydi, `''` — individual ruxsatlarga o'tkaziladi.
   */
  roleTemplateId?: string
}

/** Rolni yaratish/tahrirlash (Boshqaruv → Rollar). */
export interface StaffRolePayload {
  name: string
  description?: string
  permissions: string[]
}

export async function createStaffRole(payload: StaffRolePayload): Promise<StaffRoleTemplate> {
  const { data } = await api.post<StaffRoleTemplate>('/admin/staff/role-templates', payload)
  return data
}

/** Rolni tahrirlash — ruxsatlar shu roldagi BARCHA xodimga darhol tarqaladi. */
export async function updateStaffRole(id: string, payload: StaffRolePayload): Promise<StaffRoleTemplate> {
  const { data } = await api.put<StaffRoleTemplate>(`/admin/staff/role-templates/${id}`, payload)
  return data
}

/** Rolni o'chirish — rolda xodim bo'lsa server rad etadi (sababi bilan). */
export async function deleteStaffRole(id: string): Promise<void> {
  await api.delete(`/admin/staff/role-templates/${id}`)
}

export async function getStaffRoleTemplates(): Promise<StaffRoleTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StaffRoleTemplate[]>('/admin/staff/role-templates')
  return data
}

export async function getStaff(): Promise<Staff[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Staff[]>('/admin/staff')
  return data
}

export async function createStaff(payload: CreateStaffWithTemplatePayload): Promise<Staff> {
  const { data } = await api.post<Staff>('/admin/staff', payload)
  return data
}

export async function updateStaff(id: string, payload: CreateStaffWithTemplatePayload): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}`, payload)
  return data
}

export async function deleteStaff(id: string, reasonId?: string): Promise<void> {
  await api.delete(`/admin/staff/${id}`, { params: reasonId ? { reasonId } : undefined })
}

export async function getStaffCredentials(id: string): Promise<Credentials> {
  const { data } = await api.get<Credentials>(`/admin/staff/${id}/credentials`)
  return data
}

/** Xodimga yangi tasodifiy parol generatsiya qiladi — parol bir marta qaytadi. */
export async function resetStaffPassword(id: string): Promise<Credentials> {
  const { data } = await api.post<Credentials>(`/admin/staff/${id}/reset-password`)
  return data
}

/** Xodim bo'lim ruxsatlarini saqlash (faqat superadmin) */
export async function setStaffPermissions(id: string, permissions: string[]): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}/permissions`, { permissions })
  return data
}

/**
 * Akkaunt ROLINI o'zgartirish — "ikkinchi superadmin" tayinlash yoki qaytarish (faqat superadmin).
 * Superadminda bo'lim ruxsatlari umuman tekshirilmaydi: u markazning to'liq huquqli egasi bo'ladi.
 * Qayta login SHART EMAS — rol har so'rovda bazadan o'qiladi.
 */
export async function setStaffRole(id: string, role: 'superadmin' | 'admin' | 'staff'): Promise<Staff> {
  const { data } = await api.put<Staff>(`/admin/staff/${id}/role`, { role })
  return data
}
