import { Navigate } from 'react-router-dom'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { usePerm } from '@/lib/permissions'
import { TeachersPage } from './TeachersPage'

/**
 * «O'QITUVCHILAR» bo'limining KIRISH nuqtasi (`/admin/teachers`).
 *
 * <p>Bo'lim menyuda BITTA band, ichida esa alohida beriladigan sahifalar bor: Ro'yxati
 * (`teachers.list`), Davomati (`teachers.attendance`), O'rinbosarlar (`teachers.substitutions`)
 * va Hisoboti (`teacherReports`). Sahifalar orasida sahifa tepasidagi cardlar orqali o'tiladi.</p>
 *
 * <p>Agar bu yerda yalang `RequirePerm perm="teachers.list"` tursa, faqat «Davomati» ruxsati
 * berilgan xodim menyudan kelib "ruxsatingiz yo'q" kartasiga tushib qolardi va cardlar ham
 * shu sahifa ichida bo'lgani uchun o'ziga OCHIQ sahifaga o'ta olmasdi. Shuning uchun u
 * birinchi ochiq sahifaga yo'naltiriladi (`FormsEntry` bilan bir xil usul).</p>
 */
export function TeachersEntry() {
  const { can } = usePerm()
  if (!can('teachers.list', 'view')) {
    if (can('teachers.attendance', 'view')) return <Navigate to="/admin/teachers/attendance" replace />
    if (can('teachers.substitutions', 'view'))
      return <Navigate to="/admin/teachers/substitutions" replace />
    if (can('teacherReports', 'view')) return <Navigate to="/admin/teacher-reports" replace />
  }
  return (
    <RequirePerm perm="teachers.list">
      <TeachersPage />
    </RequirePerm>
  )
}
