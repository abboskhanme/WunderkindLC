import { api } from '../client'

/** Moliya → «Jarima» — o'qituvchiga QO'LDA jarima (maoshdan ushlanadi, kassaga tegmaydi). Server `TeacherFinesController`. */
export interface TeacherFine {
  id: string
  teacherId: string
  teacherName: string
  /** "yyyy-MM" — qaysi oy maoshidan ushlanadi */
  month: string
  amount: number
  reason: string
  status: 'active' | 'waived'
  createdAt: string
  createdByName: string
  waivedAt?: string | null
  waivedByName?: string | null
  waiveReason?: string | null
}

export async function getTeacherFines(filter: { month?: string; teacherId?: string } = {}): Promise<TeacherFine[]> {
  const { data } = await api.get<TeacherFine[]>('/admin/teacher-fines', { params: filter })
  return data
}

export async function createTeacherFine(body: { teacherId: string; month: string; amount: number; reason: string }): Promise<TeacherFine> {
  const { data } = await api.post<TeacherFine>('/admin/teacher-fines', body)
  return data
}

export async function waiveTeacherFine(id: string, reason: string): Promise<TeacherFine> {
  const { data } = await api.post<TeacherFine>(`/admin/teacher-fines/${id}/waive`, { reason })
  return data
}

export async function restoreTeacherFine(id: string): Promise<TeacherFine> {
  const { data } = await api.post<TeacherFine>(`/admin/teacher-fines/${id}/restore`)
  return data
}
