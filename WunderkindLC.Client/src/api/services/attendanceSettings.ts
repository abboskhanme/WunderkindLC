import { api } from '../client'

/** «Kelmadi» xabarini tasdiqlash rejimi (SPEC P3.2). */
export type AbsentNoticeApproval = 'auto' | 'manual' | 'late_only'

/** Davomat intizomi + «kelmadi» xabari sozlamalari (`GET/PUT /admin/attendance/settings`). */
export interface AttendanceSettings {
  disciplineEnabled: boolean
  /** Davomat dars boshlangandan keyin necha daqiqada olinishi shart (5..120). */
  deadlineMinutes: number
  /** O'qituvchiga eslatmalar — dars boshlangandan keyingi daqiqalar (o'sib boruvchi, ≤ 3 ta). */
  reminderMinutes: number[]
  escalateEnabled: boolean
  fineEnabled: boolean
  /** Bir dars uchun jarima (so'm). */
  fineAmount: number
  /** «Kelmadi» xabari dars tugagandan keyin necha daqiqada (0..180). */
  absentNoticeDelayMinutes: number
  absentNoticeApproval: AbsentNoticeApproval
  /** Faqat javobda — server ogohlantirishlari (masalan ikki marta eslatma). */
  warnings?: string[]
}

export async function getAttendanceSettings(): Promise<AttendanceSettings> {
  const { data } = await api.get<AttendanceSettings>('/admin/attendance/settings')
  return data
}

export async function saveAttendanceSettings(s: AttendanceSettings): Promise<AttendanceSettings> {
  // `warnings` faqat javobda — yuborilmaydi.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { warnings, ...body } = s
  const { data } = await api.put<AttendanceSettings>('/admin/attendance/settings', body)
  return data
}
