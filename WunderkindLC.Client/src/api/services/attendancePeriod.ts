import { api } from '../client'

/**
 * «O'quvchilar davomati» → «Davr tahlili» (SPEC-backlog-2026-10-17 A1). Sanoqlar YAGONA davomat ta'rifidan
 * (kech = keldi, belgilanmagan dars foizga kirmaydi). Shakl `AttendancePeriodDtos.cs` bilan bir xil.
 */

export interface AttendanceCounts {
  held: number
  present: number
  late: number
  excused: number
  unexcused: number
  percent: number | null
}

export interface AttendancePeriodGroup {
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
  students: number
  current: AttendanceCounts
  previous: AttendanceCounts
  deltaPp: number | null
  dropped: boolean
}

export interface AttendanceReasonStat {
  reasonId: string
  name: string
  isExcused: boolean
  isLate: boolean
  count: number
  students: number
}

export interface AttendanceGradeStat {
  grade: number | null
  label: string
  students: number
  counts: AttendanceCounts
}

export interface AttendanceRiskStudent {
  studentId: string
  studentName: string
  phone: string
  groups: string[]
  held: number
  absent: number
  excused: number
  unexcused: number
  late: number
  percent: number | null
}

export interface AttendancePeriod {
  from: string
  to: string
  prevFrom: string
  prevTo: string
  days: number
  dropPp: number
  total: AttendanceCounts
  previousTotal: AttendanceCounts
  groups: AttendancePeriodGroup[]
  reasons: AttendanceReasonStat[]
  grades: AttendanceGradeStat[]
  atRisk: AttendanceRiskStudent[]
}

export async function getAttendancePeriod(params: {
  from: string
  to: string
  groupId?: string
  teacherId?: string
}): Promise<AttendancePeriod> {
  const { data } = await api.get<AttendancePeriod>('/admin/student-attendance/period', {
    params: { ...params, groupId: params.groupId || undefined, teacherId: params.teacherId || undefined },
  })
  return data
}
