import { Navigate } from 'react-router-dom'
import { usePerm } from '@/lib/permissions'
import { StaffPage } from './StaffPage'

/**
 * «BOSHQARUV → XODIMLAR» kirish nuqtasi (`/admin/boshqaruv/xodimlar`).
 *
 * Ro'yxat `staff` (panel xodimlari) YOKI `teachers.list` (o'qituvchilar) ruxsati bilan ochiladi —
 * har biri o'z qismini ko'radi. Ikkalasi ham yo'q, lekin o'qituvchilar bo'limining boshqa sahifasi
 * (davomat, o'rinbosarlar, hisobot) ochiq bo'lsa — o'shanga yo'naltiriladi: menyudagi «Xodimlar»
 * ilgari aynan shu sahifalarga olib borardi, bunday xodim "ruxsat yo'q" kartasiga tushib qolmasin.
 */
export function XodimlarEntry() {
  const { can } = usePerm()
  if (can('staff', 'view') || can('teachers.list', 'view')) return <StaffPage />
  if (can('teachers.attendance', 'view')) return <Navigate to="/admin/teachers/attendance" replace />
  if (can('teachers.substitutions', 'view')) return <Navigate to="/admin/teachers/substitutions" replace />
  if (can('teacherReports', 'view')) return <Navigate to="/admin/teacher-reports" replace />
  return <Navigate to="/admin" replace />
}
