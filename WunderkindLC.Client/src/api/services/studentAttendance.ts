import { api } from '../client'
import type { MasteryLevel } from '@/types'

/* ============================================================
   O'quvchilar davomati (admin) — /api/admin/student-attendance
   ============================================================ */

/** Berilgan kunda darsga kelmagan (yoki kechikkan) o'quvchi */
export interface AbsentStudent {
  studentId: string
  fullName: string
  phone: string
  parentFullName: string
  parentPhone: string
  fatherPhone: string
  motherPhone: string
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
  startTime: string
  endTime: string
  room: string
  reasonId: string
  reasonName: string
  reasonShort: string
  /** true — kech kelgan (darsda qatnashgan), false — kelmagan */
  isLate: boolean
}

/** Bir kunlik davomat xulosasi */
export interface DailyAbsence {
  date: string
  /** Shu kunda darsi o'tilgan guruhlar soni */
  conductedGroups: number
  /** Davomat olingan o'quvchilar soni */
  markedStudents: number
  absentCount: number
  lateCount: number
  rows: AbsentStudent[]
}

/** Berilgan kundagi kelmagan/kechikkan o'quvchilar (sana bo'sh = bugun) */
export async function getDailyAbsence(date?: string): Promise<DailyAbsence> {
  const { data } = await api.get<DailyAbsence>('/admin/student-attendance/absent', { params: { date } })
  return data
}

/* ---------- O'quvchining shaxsiy jurnali (faqat o'qish) ---------- */

/** Jurnaldagi bitta dars kataki */
export interface StudentJournalCell {
  date: string
  /** Dars o'tilgan (davomat olingan) */
  conducted: boolean
  /** O'quvchi guruhga qo'shilgunga qadar — katak bo'sh */
  blocked: boolean
  /** Keldi (sababsiz, bahosiz, dars o'tilgan) */
  present: boolean
  grade?: number | null
  reasonName?: string | null
  reasonShort?: string | null
  isLate: boolean
  homework: number
  behavior: number
  mastery?: MasteryLevel | null
}

export interface StudentJournalGroup {
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
}

export interface StudentJournal {
  studentId: string
  fullName: string
  groups: StudentJournalGroup[]
  groupId: string
  months: string[]
  month: string
  cells: StudentJournalCell[]
  /** Shu oyda o'tilgan (davomat olingan) darslar */
  conducted: number
  attended: number
  absent: number
  late: number
  avgGrade: number
}

/** O'quvchining guruh jurnalidagi o'z qatori (guruh/oy bo'yicha, read-only) */
export async function getStudentJournal(
  studentId: string,
  groupId?: string,
  month?: string,
): Promise<StudentJournal> {
  const { data } = await api.get<StudentJournal>('/admin/student-attendance/journal', {
    params: { studentId, groupId, month },
  })
  return data
}
