import { api } from '../client'

/**
 * MOSLASHUV (onboarding — 30 kunlik adaptatsiya) — "Boshqaruv → Moslashuv" API'si
 * (`api/admin/onboarding`, ruxsat `vacancies.onboarding`).
 *
 * Moslashuv ikki yo'l bilan boshlanadi: ariza "Ishga qabul qilindi" bosqichiga o'tganda
 * AVTOMATIK (server, idempotent) yoki qo'lda ("Yangi xodim moslashuvi"). Boshlanganda katalog
 * qadamlari NUSXA qilinadi — katalogni keyin tahrirlash davom etayotgan moslashuvga tegmaydi.
 */

export type OnboardingStatus = 'active' | 'completed' | 'cancelled'
/** Ro'yxat filtri: holat yoki `overdue` (faol + kechikkan qadami bor) */
export type OnboardingFilter = '' | OnboardingStatus | 'overdue'

export interface OnboardingStep {
  id: string
  title: string
  description: string
  /** Boshlanishdan necha kun ichida (0..30) */
  dayOffset: number
  order: number
  /** O'chirilgan qadam yangi moslashuvlarga nusxa qilinmaydi */
  isActive: boolean
}

export interface OnboardingStepPayload {
  title: string
  description: string
  dayOffset: number
  isActive: boolean
}

export interface OnboardingTask {
  id: string
  stepId: string
  title: string
  description: string
  dayOffset: number
  order: number
  /** "yyyy-MM-dd" = boshlanish + dayOffset */
  dueDate: string
  done: boolean
  doneAt: string
  doneBy: string
  note: string
  /** Muddat o'tgan va bajarilmagan (faqat faol moslashuvda) */
  overdue: boolean
}

export interface EmployeeOnboarding {
  id: string
  fullName: string
  phone: string
  position: string
  /** Avtomatik boshlangan bo'lsa — ariza */
  jobApplicationId: string
  applicationNumber: number
  teacherId: string
  staffUserId: string
  startDate: string
  mentorId: string
  mentorName: string
  status: OnboardingStatus
  /** `hired` — ariza qabul qilinganda avtomatik, `manual` — qo'lda */
  source: 'hired' | 'manual'
  note: string
  completedAt: string
  cancelledAt: string
  createdAt: string
  createdBy: string
  total: number
  doneCount: number
  overdueCount: number
  /** Pastga yaxlitlangan — 100 faqat hammasi bajarilsa */
  progressPct: number
  /** Nechanchi kun (boshlanish kuni = 1) */
  dayNumber: number
  nextTitle: string
  nextDueDate: string
  /** Faqat bitta moslashuv so'ralganda */
  tasks?: OnboardingTask[]
}

export interface EmployeeOnboardingPayload {
  fullName: string
  phone: string
  position: string
  startDate: string
  mentorId: string
  teacherId: string
  staffUserId: string
  note: string
}

export interface OnboardingSummary {
  active: number
  completed: number
  cancelled: number
  /** Kechikkan qadami bor faol moslashuvlar */
  withOverdue: number
  overdueTasks: number
}

export interface OnboardingPerson {
  id: string
  kind: 'teacher' | 'staff'
  fullName: string
}

const BASE = '/admin/onboarding'

export async function getOnboardings(params: { status?: OnboardingFilter; q?: string }): Promise<EmployeeOnboarding[]> {
  const { data } = await api.get<EmployeeOnboarding[]>(BASE, {
    params: { status: params.status || undefined, q: params.q || undefined },
  })
  return data
}

export async function getOnboardingSummary(): Promise<OnboardingSummary> {
  const { data } = await api.get<OnboardingSummary>(`${BASE}/summary`)
  return data
}

export async function getOnboarding(id: string): Promise<EmployeeOnboarding> {
  const { data } = await api.get<EmployeeOnboarding>(`${BASE}/${id}`)
  return data
}

export async function createOnboarding(payload: EmployeeOnboardingPayload): Promise<EmployeeOnboarding> {
  const { data } = await api.post<EmployeeOnboarding>(BASE, payload)
  return data
}

export async function updateOnboarding(id: string, payload: EmployeeOnboardingPayload): Promise<EmployeeOnboarding> {
  const { data } = await api.put<EmployeeOnboarding>(`${BASE}/${id}`, payload)
  return data
}

/** Qadamni belgilash / belgini olish (+ izoh). Javob — butun moslashuv (holat ham o'zgarishi mumkin). */
export async function setOnboardingTask(
  id: string,
  taskId: string,
  done: boolean,
  note: string,
): Promise<EmployeeOnboarding> {
  const { data } = await api.put<EmployeeOnboarding>(`${BASE}/${id}/tasks/${taskId}`, { done, note })
  return data
}

export async function cancelOnboarding(id: string): Promise<EmployeeOnboarding> {
  const { data } = await api.put<EmployeeOnboarding>(`${BASE}/${id}/cancel`)
  return data
}

export async function reopenOnboarding(id: string): Promise<EmployeeOnboarding> {
  const { data } = await api.put<EmployeeOnboarding>(`${BASE}/${id}/reopen`)
  return data
}

export async function deleteOnboarding(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`)
}

export async function getOnboardingPeople(): Promise<OnboardingPerson[]> {
  const { data } = await api.get<OnboardingPerson[]>(`${BASE}/people`)
  return data
}

/* ---------- Qadamlar katalogi ---------- */

export async function getOnboardingSteps(): Promise<OnboardingStep[]> {
  const { data } = await api.get<OnboardingStep[]>(`${BASE}/steps`)
  return data
}

export async function createOnboardingStep(payload: OnboardingStepPayload): Promise<OnboardingStep> {
  const { data } = await api.post<OnboardingStep>(`${BASE}/steps`, payload)
  return data
}

export async function updateOnboardingStep(id: string, payload: OnboardingStepPayload): Promise<OnboardingStep> {
  const { data } = await api.put<OnboardingStep>(`${BASE}/steps/${id}`, payload)
  return data
}

export async function deleteOnboardingStep(id: string): Promise<void> {
  await api.delete(`${BASE}/steps/${id}`)
}

export async function reorderOnboardingSteps(ids: string[]): Promise<OnboardingStep[]> {
  const { data } = await api.put<OnboardingStep[]>(`${BASE}/steps/reorder`, { ids })
  return data
}
