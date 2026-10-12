import { api } from '../client'

/** Davomat jarimasi (CRM Phase 3, U3c) — davomat o'z vaqtida olinmagan dars uchun. */
export interface AttendanceFine {
  id: string
  teacherId: string
  teacherName: string
  groupId: string
  groupName: string
  lessonDate: string
  lessonStart: string
  /** Davomat olinishi kerak bo'lgan oxirgi vaqt (ISO, markaz vaqti). */
  deadline: string
  amount: number
  status: 'active' | 'waived'
  /** Davomat keyinroq olingan bo'lsa — o'sha vaqt (ma'lumot uchun). */
  attendanceTakenAt: string | null
  waivedByName: string | null
  waivedAt: string | null
  waiveReason: string | null
}

export interface AttendanceFineList {
  rows: AttendanceFine[]
  totalActive: number
  countActive: number
}

export async function getAttendanceFines(params: {
  month?: string
  teacherId?: string
  status?: '' | 'active' | 'waived'
}): Promise<AttendanceFineList> {
  const { data } = await api.get<AttendanceFineList>('/admin/attendance-fines', { params })
  return data
}

export async function waiveAttendanceFine(id: string, reason: string): Promise<AttendanceFine> {
  const { data } = await api.put<AttendanceFine>(`/admin/attendance-fines/${id}/waive`, { reason })
  return data
}

export async function restoreAttendanceFine(id: string): Promise<AttendanceFine> {
  const { data } = await api.put<AttendanceFine>(`/admin/attendance-fines/${id}/restore`)
  return data
}

/** Bir kunning barcha faol jarimalari (bayram). */
export async function waiveAttendanceFinesDay(date: string, reason: string): Promise<{ waived: number }> {
  const { data } = await api.put<{ waived: number }>('/admin/attendance-fines/waive-day', { date, reason })
  return data
}

/** O'qituvchi portali — o'z jarimalari (faqat o'qish). */
export async function getMyAttendanceFines(month?: string): Promise<AttendanceFineList> {
  const { data } = await api.get<AttendanceFineList>('/teacher/attendance-fines', { params: { month } })
  return data
}
