import { api } from '../client'

/** «Qizil ro'yxat» qatori — ketma-ket SABABSIZ dars qoldirgan o'quvchi (bitta guruh bo'yicha). */
export interface RedListRow {
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  teacherName: string
  /** Ketma-ket sababsiz qoldirilgan darslar soni. */
  streak: number
  /** 5 — 5+ dars, 3 — 3..4 dars, 0 — undan kam (faqat `min` < 3 so'ralganda). */
  level: number
  dates: string[]
  lastDate: string
  /** «Bog'lanish kerak» talabi (ochig'i yoki shu seriya uchun ochilgani). */
  contact: { requestId: string; status: string } | null
}

export async function getRedList(params: { min?: number; groupId?: string; teacherId?: string } = {}): Promise<RedListRow[]> {
  const { data } = await api.get<RedListRow[]>('/admin/student-attendance/red-list', { params })
  return data
}

export type AbsenceKind = 'late' | 'absent_excused' | 'absent_unexcused'

export interface StudentAbsences {
  from: string
  to: string
  counts: {
    held: number
    attended: number
    present: number
    late: number
    absentExcused: number
    absentUnexcused: number
    unmarked: number
    percent: number | null
  }
  byReason: { reasonId: string; name: string; isExcused: boolean; isLate: boolean; count: number }[]
  items: { date: string; groupId: string; groupName: string; courseName: string; reasonName: string; kind: AbsenceKind }[]
}

/** O'quvchi profili → «Qoldirilgan darslar» (standart — oxirgi 180 kun). */
export async function getStudentAbsences(studentId: string, from?: string, to?: string): Promise<StudentAbsences> {
  const { data } = await api.get<StudentAbsences>(`/admin/students/${studentId}/absences`, { params: { from, to } })
  return data
}
