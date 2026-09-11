import type { TeacherReportDetail, TeacherReportOverview } from '@/types'
import { api, USE_MOCK } from '../client'

/** Barcha o'qituvchilar faollik hisoboti. month bo'sh/berilmagan = Umumiy (barcha oylar yig'indisi). */
export async function getTeacherReport(month?: string): Promise<TeacherReportOverview> {
  if (USE_MOCK) return { months: [], month: '', rows: [] }
  const q = month ? `?month=${encodeURIComponent(month)}` : ''
  const { data } = await api.get<TeacherReportOverview>(`/admin/teacher-reports${q}`)
  return data
}

/** Bitta o'qituvchining batafsil hisoboti (guruh/fan yoyilmasi). month bo'sh = Umumiy. */
export async function getTeacherReportDetail(
  id: string,
  month?: string,
): Promise<TeacherReportDetail> {
  const q = month ? `?month=${encodeURIComponent(month)}` : ''
  const { data } = await api.get<TeacherReportDetail>(`/admin/teacher-reports/${id}${q}`)
  return data
}
