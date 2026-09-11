import type { Subject } from '@/types'
import { delay, uid } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { subjectsMock } from '../mock/subjects'

export interface SubjectPayload {
  name: string
  /** Kurs narxi (so'm) */
  price: number
  /** Bir dars uchun yaxlit narx (so'm) — 12 tadan kam dars qolganda qisman-oy hisobida ishlatiladi */
  lessonPrice?: number
}

export async function getSubjects(): Promise<Subject[]> {
  if (USE_MOCK) {
    await delay()
    return subjectsMock
  }
  const { data } = await api.get<Subject[]>('/admin/subjects')
  return data
}

export async function createSubject(payload: SubjectPayload): Promise<Subject> {
  if (USE_MOCK) {
    await delay(200)
    return { id: uid(), ...payload }
  }
  const { data } = await api.post<Subject>('/admin/subjects', payload)
  return data
}

/**
 * Kursni yangilash. Narx o'zgargan bo'lsa, `applyFee` orqali yangi narx shu kursga bog'langan
 * guruhlardagi o'quvchilarning joriy oyiga qo'llanishini boshqaramiz: true = joriy oydan,
 * false = keyingi oydan.
 */
export async function updateSubject(
  id: string,
  payload: SubjectPayload,
  applyFee?: boolean,
): Promise<Subject> {
  if (USE_MOCK) {
    await delay(200)
    return { id, ...payload }
  }
  const { data } = await api.put<Subject>(`/admin/subjects/${id}`, payload, {
    params: applyFee === undefined ? undefined : { applyFee },
  })
  return data
}

export async function deleteSubject(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(200)
    return
  }
  await api.delete(`/admin/subjects/${id}`)
}

// ---- Kursga biriktirilgan o'quv dasturlari (ko'p-ko'pga) ----

export interface SubjectCurriculumLink {
  curriculumId: string
  name: string
  order: number
}

export async function getSubjectCurricula(subjectId: string): Promise<SubjectCurriculumLink[]> {
  const { data } = await api.get<SubjectCurriculumLink[]>(`/admin/subjects/${subjectId}/curricula`)
  return data
}
export async function attachCurriculumToSubject(subjectId: string, curriculumId: string): Promise<void> {
  await api.post(`/admin/subjects/${subjectId}/curricula/${curriculumId}`)
}
export async function detachCurriculumFromSubject(subjectId: string, curriculumId: string): Promise<void> {
  await api.delete(`/admin/subjects/${subjectId}/curricula/${curriculumId}`)
}
