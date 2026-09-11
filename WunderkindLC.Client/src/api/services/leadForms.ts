import { api } from '../client'
import type { DayCount, LeadStageCount } from '@/types'
import type { CrmOverview } from './leads'

/**
 * LID FORMALARI — "O'quv bo'limi → Formalar" bo'limi.
 *
 * Har bir ijtimoiy tarmoq (Instagram, Facebook, Telegram, ...) uchun alohida ommaviy forma:
 * o'z havolasi (`/forma/{slug}`) va o'z MANBASI bilan. To'ldirilgan ariza CRM'da lid bo'lib
 * tushadi, statistika esa "qaysi kanal nechta o'quvchi keltirdi" ni ko'rsatadi.
 * Ruxsat: `leads`.
 */

/** Qo'shimcha savol turlari — serverdagi `LeadFormService.Kinds` bilan bir xil. */
export type LeadFormFieldKind = 'text' | 'textarea' | 'number' | 'select' | 'radio' | 'checkbox'

/** Tur → o'zbekcha yorliq (komponentlarda xom satr yozilmasin). */
export const fieldKindLabels: Record<LeadFormFieldKind, string> = {
  text: 'Qisqa matn',
  textarea: 'Uzun matn',
  number: 'Raqam',
  select: "Ro'yxatdan tanlash",
  radio: 'Bitta variant',
  checkbox: 'Bir nechta variant',
}

/** Variantlar SHART bo'lgan turlar (serverdagi `NeedsOptions` bilan bir xil). */
export function needsOptions(kind: string): boolean {
  return kind === 'select' || kind === 'radio' || kind === 'checkbox'
}

/**
 * Ijtimoiy tarmoq havolalari — ariza YUBORILGANDAN KEYIN "rahmat" ekranida ikonka bo'lib
 * chiqadi (mijoz menejer qo'ng'iroq qilgunicha kanalga obuna bo'lib qolsin). Bo'sh — chizilmaydi.
 */
export interface LeadFormSocials {
  instagram: string
  telegram: string
  facebook: string
  youtube: string
  website: string
}

export const emptySocials: LeadFormSocials = {
  instagram: '', telegram: '', facebook: '', youtube: '', website: '',
}

export interface LeadFormListItem {
  id: string
  title: string
  slug: string
  source: string
  /** Formaning kursi — ERKIN MATN (markazdagi kurslar katalogiga bog'lanmagan). */
  courseName: string
  isActive: boolean
  views: number
  fieldCount: number
  submissionCount: number
  createdAt: string
  createdBy: string
}

export interface LeadFormField {
  id: string
  label: string
  kind: LeadFormFieldKind
  options: string[]
  placeholder: string
  required: boolean
  order: number
}

export interface LeadFormDetail {
  id: string
  title: string
  slug: string
  source: string
  courseName: string
  /** Mijozga ko'rsatiladigan kurs variantlari — formaning O'ZIDA yoziladi (`askCourse` uchun). */
  courseOptions: string[]
  intro: string
  successText: string
  buttonText: string
  askAge: boolean
  askCourse: boolean
  askParentPhone: boolean
  isActive: boolean
  views: number
  createdAt: string
  createdBy: string
  fields: LeadFormField[]
  socials: LeadFormSocials
}

export interface LeadFormFieldInput {
  id?: string
  label: string
  kind: LeadFormFieldKind
  options: string[]
  placeholder: string
  required: boolean
}

export interface LeadFormPayload {
  title: string
  source: string
  courseName: string
  courseOptions: string[]
  intro: string
  successText: string
  buttonText: string
  askAge: boolean
  askCourse: boolean
  askParentPhone: boolean
  isActive: boolean
  fields: LeadFormFieldInput[]
  socials: LeadFormSocials
}

/** Formaga tushgan bitta ariza (+ lidning HOZIRGI holati). */
export interface LeadFormSubmission {
  id: string
  formId: string
  formTitle: string
  fullName: string
  phone: string
  parentPhone: string
  age: number
  courseName: string
  ref: string
  createdAt: string
  leadId: string
  isNewLead: boolean
  studentId: string | null
  active: boolean
  leadDeleted: boolean
  /** Lidning HOZIRGI kanban bosqichi (bo'sh — bosqichsiz yoki lid o'chirilgan). */
  stageTitle: string
  /** Bosqich rangi — `config/stageColors.ts` kalitlari (slate | blue | ...). */
  stageColor: string
  /** SOTUV natijasi: odam pul to'ladimi (to'lov − vozvrat > 0). */
  paid: boolean
  paidTotal: number
  /** Birinchi to'lov sanasi ("yyyy-MM-dd"); to'lov bo'lmasa — bo'sh. */
  firstPaidAt: string
  answers: { question: string; answers: string[] }[]
}

export interface LeadFormStatRow {
  formId: string
  title: string
  source: string
  isActive: boolean
  views: number
  submissions: number
  newLeads: number
  converted: number
  activeStudents: number
  /** Pul to'lagan (takrorsiz) lidlar soni. */
  paid: number
  /** Shu formadan kelgan lidlar to'lagan SOF summa. */
  revenue: number
  submitRate: number
  convertRate: number
  /** SOTUV konversiyasi — lidlarning necha foizi haqiqatan to'ladi. */
  payRate: number
}
export interface LeadFormSourceRow {
  source: string
  forms: number
  submissions: number
  converted: number
  activeStudents: number
  paid: number
  revenue: number
}
export interface LeadFormRefRow {
  ref: string
  submissions: number
  converted: number
  paid: number
}
export interface LeadFormStats {
  forms: number
  activeForms: number
  views: number
  submissions: number
  newLeads: number
  converted: number
  activeStudents: number
  paid: number
  revenue: number
  byForm: LeadFormStatRow[]
  bySource: LeadFormSourceRow[]
  byRef: LeadFormRefRow[]
  /** Bosqich va kunlik oqim — daraja testi statistikasi bilan YAGONA shakl (`@/types`). */
  byStage: LeadStageCount[]
  daily: DayCount[]

  /* ---- BUTUN CRM manzarasi (formadan TASHQARI lidlar ham) ----
     ⚠️ Yuqoridagi raqamlar faqat FORMALARDAN kelganlarni sanaydi. Markazda esa qo'lda
     kiritilgan, daraja testidan va Instagramdan kelgan lidlar ham bor — ularsiz bu sahifadagi
     sonlar "markazning hamma lidi" deb o'qilib, noto'g'ri xulosaga olib kelardi. */

  /** CRM'dagi BARCHA lidlar — kanallar va bosqichlar bilan (daraja testi sahifasida ham shu blok). */
  overview: CrmOverview
}

export async function getLeadForms(): Promise<LeadFormListItem[]> {
  const { data } = await api.get<LeadFormListItem[]>('/admin/lead-forms')
  return data
}

export async function getLeadForm(id: string): Promise<LeadFormDetail> {
  const { data } = await api.get<LeadFormDetail>(`/admin/lead-forms/${id}`)
  return data
}

export async function createLeadForm(payload: LeadFormPayload): Promise<LeadFormDetail> {
  const { data } = await api.post<LeadFormDetail>('/admin/lead-forms', payload)
  return data
}

export async function updateLeadForm(id: string, payload: LeadFormPayload): Promise<LeadFormDetail> {
  const { data } = await api.put<LeadFormDetail>(`/admin/lead-forms/${id}`, payload)
  return data
}

/** Nusxa olish — yangi havola bilan, MANBASIZ va O'CHIQ holda yaratiladi. */
export async function duplicateLeadForm(id: string): Promise<LeadFormDetail> {
  const { data } = await api.post<LeadFormDetail>(`/admin/lead-forms/${id}/duplicate`)
  return data
}

export async function deleteLeadForm(id: string): Promise<void> {
  await api.delete(`/admin/lead-forms/${id}`)
}

/** Arizalar — barcha formalar bo'yicha yoki bitta forma bo'yicha. */
export async function getLeadFormSubmissions(formId?: string): Promise<LeadFormSubmission[]> {
  const { data } = await api.get<LeadFormSubmission[]>('/admin/lead-forms/submissions', {
    params: formId ? { formId } : undefined,
  })
  return data
}

export async function getLeadFormStats(): Promise<LeadFormStats> {
  const { data } = await api.get<LeadFormStats>('/admin/lead-forms/stats')
  return data
}

/** Lid manbalari ma'lumotnomasi (Sabablar sahifasida boshqariladi). */
export async function getLeadFormSources(): Promise<string[]> {
  const { data } = await api.get<string[]>('/admin/lead-forms/sources')
  return data
}

// ⚠️ Kurslar ma'lumotnomasi endpointi ATAYIN YO'Q: forma kursni markazdagi kurslar katalogidan
// olmaydi — variantlar formaning O'ZIDA yoziladi (`courseOptions`).
