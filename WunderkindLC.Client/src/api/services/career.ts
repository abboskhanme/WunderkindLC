import { api } from '../client'

/**
 * KARYERA (Wunderkind Career) — "Boshqaruv → Vakansiyalar" bo'limi API'si.
 *
 * Nomzodlar ALOHIDA Telegram bot (`CAREER_BOT_TOKEN`) ichidagi Mini App orqali
 * (`/vakansiya` — statik HTML/Bootstrap sahifa) ariza yuboradi. Bu yerda admin
 * vakansiyalarni boshqaradi, "Biz haqimizda"ni to'ldiradi va arizalarni bosqichma-bosqich
 * yuritadi — bosqich o'zgarganda nomzodga botda avtomatik xabar ketadi.
 */

export type VacancyStatus = 'active' | 'archived'
export type EmploymentType = 'full' | 'part' | 'shift' | 'remote'
/** Ariza bosqichlari — backenddagi `CareerService.Stages` bilan bir xil kalitlar */
export type ApplicationStatus = 'new' | 'review' | 'interview' | 'trial' | 'hired' | 'rejected'

export interface CareerAbout {
  title: string
  tagline: string
  about: string
  /** Imtiyozlar — har qatorda bittadan */
  benefits: string
  logoUrl: string
  address: string
  landmark: string
  mapUrl: string
  workTime: string
  phone: string
  phone2: string
  email: string
  telegram: string
  instagram: string
  facebook: string
  youtube: string
  tiktok: string
  website: string
  updatedAt: string
  updatedBy: string
}

export interface Vacancy {
  id: string
  title: string
  department: string
  employmentType: EmploymentType
  location: string
  salaryFrom: number
  salaryTo: number
  salaryNote: string
  description: string
  requirements: string
  responsibilities: string
  conditions: string
  status: VacancyStatus
  /** "yyyy-MM-dd" yoki bo'sh */
  deadline: string
  order: number
  createdAt: string
  createdBy: string
  archivedAt: string
  archivedBy: string
  /** Shu vakansiyaga tushgan jami ariza */
  applicationCount: number
  /** Ulardan hali ko'rilmagani ("new") */
  newCount: number
}

export interface VacancyPayload {
  title: string
  department: string
  employmentType: EmploymentType
  location: string
  salaryFrom: number
  salaryTo: number
  salaryNote: string
  description: string
  requirements: string
  responsibilities: string
  conditions: string
  deadline: string
  order: number
}

export interface JobApplicationEvent {
  status: ApplicationStatus
  note: string
  createdAt: string
  createdBy: string
}

export interface JobApplication {
  id: string
  number: number
  vacancyId: string
  vacancyTitle: string
  chatId: number
  tgUsername: string
  fullName: string
  phone: string
  experience: string
  motivation: string
  /** `/uploads/...pdf` yoki bo'sh */
  cvUrl: string
  cvName: string
  status: ApplicationStatus
  /** Oxirgi bosqich izohi — NOMZOD ham ko'radi */
  statusNote: string
  statusChangedAt: string
  statusChangedBy: string
  /** Faqat admin ko'radigan ichki izoh */
  adminNote: string
  createdAt: string
  /** Faqat bitta ariza so'ralganda to'ladi */
  history?: JobApplicationEvent[]
}

export interface CareerStage {
  key: ApplicationStatus
  label: string
  /** Nomzod ilovada ko'radigan matn */
  candidateText: string
  icon: string
  order: number
  isFinal: boolean
}

export interface CareerStats {
  total: number
  /** Yakunlanmagan (qabul ham, rad ham qilinmagan) arizalar */
  active: number
  hired: number
  rejected: number
  byStatus: Record<string, number>
}

const BASE = '/admin/career'

/* ---------- Bosqichlar ---------- */

export async function getStages(): Promise<CareerStage[]> {
  const { data } = await api.get<CareerStage[]>(`${BASE}/stages`)
  return data
}

/* ---------- Biz haqimizda ---------- */

export async function getAbout(): Promise<CareerAbout> {
  const { data } = await api.get<CareerAbout>(`${BASE}/about`)
  return data
}

export async function saveAbout(payload: Partial<CareerAbout>): Promise<CareerAbout> {
  const { data } = await api.put<CareerAbout>(`${BASE}/about`, payload)
  return data
}

/* ---------- Vakansiyalar ---------- */

export async function getVacancies(status?: VacancyStatus): Promise<Vacancy[]> {
  const { data } = await api.get<Vacancy[]>(`${BASE}/vacancies`, {
    params: { status: status || undefined },
  })
  return data
}

export async function createVacancy(payload: VacancyPayload): Promise<Vacancy> {
  const { data } = await api.post<Vacancy>(`${BASE}/vacancies`, payload)
  return data
}

export async function updateVacancy(id: string, payload: VacancyPayload): Promise<Vacancy> {
  const { data } = await api.put<Vacancy>(`${BASE}/vacancies/${id}`, payload)
  return data
}

export async function archiveVacancy(id: string): Promise<Vacancy> {
  const { data } = await api.post<Vacancy>(`${BASE}/vacancies/${id}/archive`)
  return data
}

export async function restoreVacancy(id: string): Promise<Vacancy> {
  const { data } = await api.post<Vacancy>(`${BASE}/vacancies/${id}/restore`)
  return data
}

export async function deleteVacancy(id: string): Promise<void> {
  await api.delete(`${BASE}/vacancies/${id}`)
}

/* ---------- Arizalar ---------- */

export async function getApplications(params: {
  status?: ApplicationStatus | ''
  vacancyId?: string
  q?: string
}): Promise<JobApplication[]> {
  const { data } = await api.get<JobApplication[]>(`${BASE}/applications`, {
    params: {
      status: params.status || undefined,
      vacancyId: params.vacancyId || undefined,
      q: params.q || undefined,
    },
  })
  return data
}

export async function getApplication(id: string): Promise<JobApplication> {
  const { data } = await api.get<JobApplication>(`${BASE}/applications/${id}`)
  return data
}

/** Bosqichni o'zgartiradi — nomzodga karyera botida avtomatik xabar ketadi. */
export async function setApplicationStatus(
  id: string,
  status: ApplicationStatus,
  note: string,
): Promise<JobApplication> {
  const { data } = await api.post<JobApplication>(`${BASE}/applications/${id}/status`, { status, note })
  return data
}

export async function setApplicationNote(id: string, adminNote: string): Promise<JobApplication> {
  const { data } = await api.put<JobApplication>(`${BASE}/applications/${id}/note`, { adminNote })
  return data
}

export async function deleteApplication(id: string): Promise<void> {
  await api.delete(`${BASE}/applications/${id}`)
}

export async function getCareerStats(): Promise<CareerStats> {
  const { data } = await api.get<CareerStats>(`${BASE}/stats`)
  return data
}

/** Logotip uchun umumiy admin fayl yuklash endpointi. */
export async function uploadCareerFile(file: File): Promise<string> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<{ url: string }>('/admin/uploads', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data.url
}
