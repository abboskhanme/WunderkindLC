import { api } from '../client'

/* ===== Turlar ===== */

/** Admin "Support" ro'yxati elementi. */
export interface SupportTeacher {
  id: string
  fullName: string
  phone: string
  photoUrl?: string | null
  openCount: number
  bookedCount: number
  doneCount: number
}

/** Bitta support slot/dars (admin + o'qituvchi ko'rinishi). */
export interface SupportSlot {
  id: string
  teacherId: string
  date: string
  startTime: string
  endTime: string
  status: 'open' | 'booked' | 'done'
  studentId?: string | null
  studentName: string
  topic: string
  notes: string
  bookedAt?: string | null
}

/** Admin: support tafsiloti — barcha slot/darslari. */
export interface SupportTeacherDetail {
  id: string
  fullName: string
  phone: string
  photoUrl?: string | null
  slots: SupportSlot[]
}

/** O'quvchi ko'rinishidagi support + bo'sh slotlari. */
export interface StudentSupportTeacher {
  teacherId: string
  fullName: string
  photoUrl?: string | null
  subject: string
  openSlots: { id: string; date: string; startTime: string; endTime: string }[]
}
/** O'quvchining o'z broni. */
export interface StudentSupportBooking {
  id: string
  teacherId: string
  teacherName: string
  date: string
  startTime: string
  endTime: string
  status: 'open' | 'booked' | 'done'
  topic: string
  notes: string
}
export interface StudentSupport {
  supports: StudentSupportTeacher[]
  myBookings: StudentSupportBooking[]
}

export interface CreateSupportSlotPayload {
  date: string
  startTime: string
  endTime: string
  /** Har odamga ajratilgan davomiylik (daqiqa). 0 = butun blok bitta slot. */
  slotMinutes?: number
  /** Shu hafta kuni keyingi N haftaga ham takrorlash (0 = faqat shu sana). */
  repeatWeeks?: number
  /** Har kunlik takrorlash rejimi. */
  repeatMode?: 'daily'
  /** Kunlik takror tugash sanasi ("yyyy-MM-dd"). */
  endDate?: string
}

/* ===== Admin ===== */

export async function getSupportTeachers(): Promise<SupportTeacher[]> {
  const { data } = await api.get<SupportTeacher[]>('/admin/support/teachers')
  return data
}
export async function getSupportTeacher(id: string): Promise<SupportTeacherDetail> {
  const { data } = await api.get<SupportTeacherDetail>(`/admin/support/teachers/${id}`)
  return data
}

/* ===== O'qituvchi (support) ===== */

export async function getMySupportSlots(month?: string): Promise<SupportSlot[]> {
  const { data } = await api.get<SupportSlot[]>('/teacher/support/slots', { params: month ? { month } : undefined })
  return data
}
export async function addSupportSlot(payload: CreateSupportSlotPayload): Promise<{ created: number }> {
  const { data } = await api.post<{ created: number }>('/teacher/support/slots', payload)
  return data
}
export async function deleteSupportSlot(id: string): Promise<void> {
  await api.delete(`/teacher/support/slots/${id}`)
}
export async function completeSupportSlot(id: string, topic: string, notes: string): Promise<void> {
  await api.post(`/teacher/support/slots/${id}/complete`, { topic, notes })
}

/* ===== O'quvchi ===== */

export async function getStudentSupport(): Promise<StudentSupport> {
  const { data } = await api.get<StudentSupport>('/student/support')
  return data
}
export async function bookSupportSlot(id: string): Promise<void> {
  await api.post(`/student/support/slots/${id}/book`)
}
export async function cancelSupportSlot(id: string): Promise<void> {
  await api.post(`/student/support/slots/${id}/cancel`)
}
