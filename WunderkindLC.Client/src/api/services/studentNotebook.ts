import type { Subject, AttendanceReasonCount } from '@/types'
import { api } from '../client'

/** O'quvchining bitta OYDAGI ("yyyy-MM") uy vazifa/xulq jamlamasi */
export interface MonthMarks {
  month: string
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
}

/** Davomat — har metrika OY ("yyyy-MM") → son */
export interface NotebookAttendance {
  missedDays: Record<string, number>
  illnessDays: Record<string, number>
  missedLessons: Record<string, number>
  illnessLessons: Record<string, number>
  lateCount: Record<string, number>
}

/** O'zlashtirish darajalarining taqsimoti (darsga munosabat) — % bo'yicha */
export interface MasteryDistribution {
  nonReactive: number
  reactive: number
  active: number
  proActive: number
  totalLessons: number
}

/** O'quvchi shaxsiy daftari — bitta o'quvchi haqida barcha ma'lumot */
export interface StudentNotebook {
  // Profil
  id: string
  fullName: string
  className: string
  homeroomTeacher: string
  parentFullName: string
  parentPhone: string
  gender: string
  birthDate: string
  enrollmentDate: string
  balance: number
  photoUrl?: string | null
  // Shaxsiy ma'lumotlar
  address: string
  discountPct: number
  discountAmount: number
  discountNote: string
  parentPassportUrl?: string | null
  // O'zlashtirish
  subjects: Subject[]
  /** subjectId → OY ("yyyy-MM") → o'rtacha baho */
  grades: Record<string, Record<string, number>>
  avgGrade: number
  // Davomat
  attendance: NotebookAttendance
  conducted: number
  attended: number
  attendancePct: number
  reasons: AttendanceReasonCount[]
  // Uy vazifa + xulq
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
  marksTrend: MonthMarks[]
  // O'zlashtirish darajasi taqsimoti
  masteryDistribution?: MasteryDistribution | null
}

export async function getStudentNotebook(id: string): Promise<StudentNotebook> {
  const { data } = await api.get<StudentNotebook>(`/admin/students/${id}/profile`)
  return data
}
