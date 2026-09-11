import { api } from '../client'
import type { LeadFormFieldKind } from './leadForms'

/**
 * «LID KIRITISH FORMASI» — Lidlar bo'limidagi "Yangi lid" oynasi qanday ko'rinishini belgilaydi.
 *
 * ⚠️ Bu OMMAVIY forma EMAS (`leadForms.ts` — u tashqi mijoz uchun, o'z havolasi bilan).
 * Bu yerda markaz O'Z XODIMIGA qaysi maydonni majburiy qilishini sozlaydi.
 * Boshqariladi: "O'quv bo'limi → Formalar → Lid kiritish formasi" (`/admin/forms/lid-kiritish`).
 */

/** Standart maydon holati (serverdagi `LeadEntryRules.States` bilan bir xil). */
export type LeadEntryState = 'hidden' | 'optional' | 'required'

export const leadEntryStateLabels: Record<LeadEntryState, string> = {
  hidden: "So'ralmaydi",
  optional: 'Ixtiyoriy',
  required: 'Majburiy',
}

/** Standart maydon — kodda mavjud (Lead ustuni), sozlamada faqat HOLATI saqlanadi. */
export interface LeadEntryStandard {
  /** `fullName` | `phone` | `birthDate` ... — `Lead` maydonining nomi. */
  key: string
  label: string
  state: LeadEntryState
  /** Sozlab bo'lmaydi (F.I.SH — doim majburiy). */
  locked: boolean
}

/** Markaz o'zi qo'shgan savol. Javobi lidning `answers` iga tushadi. */
export interface LeadEntryField {
  id: string
  label: string
  kind: LeadFormFieldKind
  options: string[]
  placeholder: string
  required: boolean
  order: number
}

export interface LeadEntryForm {
  standard: LeadEntryStandard[]
  fields: LeadEntryField[]
}

/** Saqlash payload'i — bandlar TO'LIQ almashtiriladi. */
export interface LeadEntryFormPayload {
  standard: { key: string; state: LeadEntryState }[]
  fields: {
    id?: string
    label: string
    kind: LeadFormFieldKind
    options: string[]
    placeholder: string
    required: boolean
  }[]
}

/** Qo'shimcha savolga berilgan javob (ommaviy forma javoblari bilan bir xil shakl). */
export interface LeadAnswer {
  question: string
  answers: string[]
}

/** Joriy sozlama. GET har qanday xodimga ochiq — lid oynasi shusiz chizila olmaydi. */
export async function getLeadEntryForm(): Promise<LeadEntryForm> {
  const { data } = await api.get<LeadEntryForm>('/admin/lead-entry-form')
  return data
}

/** Sozlamani saqlash (`leads.forms:edit`). Javobda server tozalagan holat qaytadi. */
export async function saveLeadEntryForm(payload: LeadEntryFormPayload): Promise<LeadEntryForm> {
  const { data } = await api.put<LeadEntryForm>('/admin/lead-entry-form', payload)
  return data
}

/** Bitta lidning qo'shimcha savollarga bergan javoblari. */
export async function getLeadAnswers(leadId: string): Promise<LeadAnswer[]> {
  const { data } = await api.get<LeadAnswer[]>(`/admin/leads/${leadId}/answers`)
  return data
}
