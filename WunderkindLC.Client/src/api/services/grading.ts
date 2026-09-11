import { api } from '../client'

/** Baholash mezonlari (kriteriya) — guruhga biriktiriladi, guruh ichida o'quvchilar baholanadi. */

export interface GradingCriterion {
  id: string
  name: string
  description: string
  maxScore: number
  order: number
  /** Mezon egasi (Teacher.Id). Bo'sh — umumiy mezon. */
  teacherId?: string | null
  /** Mezon egasining ismi (backend qaytaradi). */
  teacherName?: string | null
}
export interface GradingBoardCriterion {
  id: string
  name: string
  order: number
}
export interface GradingBoardStudent {
  studentId: string
  fullName: string
  /** "criterionId|date" — "bajardi" belgilangan kataklar */
  doneKeys: string[]
  /** A'zolik boshlanishi (aktivlashtirilgan bo'lsa ActivatedAt, aks holda JoinedAt, "YYYY-MM-DD").
   *  Undan oldingi sanaga mezon belgilab bo'lmaydi — katak bloklanadi. */
  memberStart: string
}
export interface GradingBoard {
  groupId: string
  groupName: string
  /** Mavjud oylar ("YYYY-MM") */
  months: string[]
  /** Joriy tanlangan oy */
  month: string
  /** Shu oydagi dars sanalari ("YYYY-MM-DD") */
  dates: string[]
  criteria: GradingBoardCriterion[]
  students: GradingBoardStudent[]
}
export interface SetGrade {
  groupId: string
  studentId: string
  criterionId: string
  date: string
  done: boolean
}
export interface BulkGrade {
  groupId: string
  criterionId: string
  date: string
  done: boolean
}

// ---- Mezonlar (pul) ----
export async function getCriteria(): Promise<GradingCriterion[]> {
  const { data } = await api.get<GradingCriterion[]>('/admin/grading/criteria')
  return data
}
export async function createCriterion(
  name: string,
  description: string,
  teacherId?: string,
): Promise<GradingCriterion> {
  const { data } = await api.post<GradingCriterion>('/admin/grading/criteria', {
    name,
    description,
    maxScore: 1,
    teacherId: teacherId || null,
  })
  return data
}
export async function updateCriterion(
  id: string,
  name: string,
  description: string,
  teacherId?: string,
): Promise<void> {
  await api.put(`/admin/grading/criteria/${id}`, { name, description, maxScore: 1, teacherId: teacherId || null })
}
export async function deleteCriterion(id: string): Promise<void> {
  await api.delete(`/admin/grading/criteria/${id}`)
}

// ---- Guruhga biriktirish ----
export async function getGroupCriteria(groupId: string): Promise<string[]> {
  const { data } = await api.get<string[]>(`/admin/grading/group/${groupId}/criteria`)
  return data
}
export async function setGroupCriteria(groupId: string, criterionIds: string[]): Promise<void> {
  await api.put(`/admin/grading/group/${groupId}/criteria`, { criterionIds })
}

// ---- Baholash grid'i (admin) ----
export async function getGradingBoard(groupId: string, month?: string): Promise<GradingBoard> {
  const { data } = await api.get<GradingBoard>(`/admin/grading/group/${groupId}/board`, {
    params: month ? { month } : {},
  })
  return data
}
export async function setGrade(req: SetGrade): Promise<void> {
  await api.post('/admin/grading/grade', req)
}
export async function bulkGrade(req: BulkGrade): Promise<void> {
  await api.post('/admin/grading/grade/bulk', req)
}

// ---- O'quvchi baholash xulosa ----
export interface MonthGradingSummary {
  month: string
  averageScore: number
  totalScore: number
  criteriaCount: number
}
export async function getStudentGradingSummary(studentId: string): Promise<MonthGradingSummary[]> {
  const { data } = await api.get<MonthGradingSummary[]>(`/admin/grading/student/${studentId}/summary`)
  return data
}
