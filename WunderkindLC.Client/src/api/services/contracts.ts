import type {
  ContractTemplate,
  ContractField,
  ContractDoc,
  StudentRecipient,
  StaffRecipient,
  SendResult,
} from '@/types'
import { api, USE_MOCK } from '../client'

type Target = 'parent' | 'staff'

/** Berilgan target (ota-ona/xodim) bo'yicha yuklangan Word andozalar */
export async function getTemplates(target: Target): Promise<ContractTemplate[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ContractTemplate[]>('/admin/contracts/templates', {
    params: { target },
  })
  return data
}

/** Yangi andoza yozuvi (fayl avval uploadAdminFile orqali yuklanadi) */
export async function createTemplate(
  target: Target,
  name: string,
  fileUrl: string,
  fileName: string,
): Promise<ContractTemplate> {
  const { data } = await api.post<ContractTemplate>('/admin/contracts/templates', {
    target,
    name,
    fileUrl,
    fileName,
  })
  return data
}

/** Custom (matnli) andoza yaratish — fayl shart emas, @-o'rinbosarli matn + qo'shimcha o'rinbosarlar */
export async function createCustomTemplate(
  target: Target,
  name: string,
  body: string,
  fields: ContractField[],
): Promise<ContractTemplate> {
  const { data } = await api.post<ContractTemplate>('/admin/contracts/templates', {
    target,
    name,
    body,
    fields,
  })
  return data
}

/** Custom (matnli) andozani tahrirlash */
export async function updateCustomTemplate(
  id: string,
  target: Target,
  name: string,
  body: string,
  fields: ContractField[],
): Promise<ContractTemplate> {
  const { data } = await api.put<ContractTemplate>(`/admin/contracts/templates/${id}`, {
    target,
    name,
    body,
    fields,
  })
  return data
}

export async function deleteTemplate(id: string): Promise<void> {
  await api.delete(`/admin/contracts/templates/${id}`)
}

/** O'quvchi oluvchilari (har o'quvchi alohida qator) */
export async function getStudentRecipients(): Promise<StudentRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StudentRecipient[]>('/admin/contracts/recipients/students')
  return data
}

/** Xodim oluvchilari */
export async function getStaffRecipients(): Promise<StaffRecipient[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<StaffRecipient[]>('/admin/contracts/recipients/staff')
  return data
}

/**
 * Bitta oluvchi uchun shartnomani to'ldirib .docx faylini YUKLAB OLISH (Telegram shart emas).
 * Server shartnoma raqamini beradi va tarixga yozadi; brauzer faylni saqlaydi.
 */
export async function downloadContract(
  target: Target,
  templateId: string,
  recipientKey: string,
): Promise<void> {
  const res = await api.post('/admin/contracts/build', { target, templateId, recipientKey }, {
    responseType: 'blob',
  })
  const cd: string = res.headers['content-disposition'] ?? ''
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(cd)
  const name = m ? decodeURIComponent(m[1]) : 'shartnoma.docx'
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  a.click()
  URL.revokeObjectURL(url)
}

/* ---------- Tuzilgan shartnomalar (saqlangan PDF/DOCX nusxalar) ---------- */

/** Tuzilgan shartnomalar tarixi (raqam bo'yicha kamayish tartibida) */
export async function getContracts(params?: {
  target?: Target
  recipientKey?: string
  q?: string
}): Promise<ContractDoc[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ContractDoc[]>('/admin/contracts', { params })
  return data
}

/**
 * Tayyor PDF nusxani shartnomaga biriktirish (fayl avval uploadAdminFile orqali yuklanadi).
 * PDF'ni superadminning o'zi tayyorlaydi; qayta yuklansa eskisi almashtiriladi.
 */
export async function uploadContractPdf(
  id: string,
  fileUrl: string,
  fileName: string,
): Promise<ContractDoc> {
  const { data } = await api.post<ContractDoc>(`/admin/contracts/${id}/pdf`, {
    fileUrl,
    fileName,
  })
  return data
}

/** Shartnomaning ilovada ko'rinishini yoqish/o'chirish */
export async function setContractVisibility(id: string, visible: boolean): Promise<ContractDoc> {
  const { data } = await api.put<ContractDoc>(`/admin/contracts/${id}/visibility`, { visible })
  return data
}

/** Shartnoma yozuvini va saqlangan fayllarini o'chirish */
export async function deleteContract(id: string): Promise<void> {
  await api.delete(`/admin/contracts/${id}`)
}

/** Tanlangan oluvchilarga shartnoma yuborish (Telegram bot orqali) */
export async function sendContracts(
  target: Target,
  templateId: string,
  recipientKeys: string[],
): Promise<SendResult[]> {
  const { data } = await api.post<SendResult[]>('/admin/contracts/send', {
    target,
    templateId,
    recipientKeys,
  })
  return data
}
