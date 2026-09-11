import { Navigate } from 'react-router-dom'
import { RequirePerm } from '@/components/auth/RequirePerm'
import { usePerm } from '@/lib/permissions'
import { FormsPage } from './FormsPage'

/**
 * «FORMALAR» bo'limining KIRISH nuqtasi (`/admin/forms`).
 *
 * <p>Bo'lim ichida ikki turdagi forma bor va ularning ruxsatlari HAR XIL: lid formalari —
 * `leads.forms`, daraja testlari — `schedule.levelTests`. Menyudagi band ikkalasidan biri bo'lsa ko'rinadi
 * (`permAny`), ya'ni faqat `schedule` ruxsati bor xodim ham shu manzilga keladi. Agar bu yerda
 * yalang `RequirePerm perm="leads"` tursa, u xodim "ruxsatingiz yo'q" kartasiga tushib qolar va
 * o'ziga OCHIQ bo'lgan daraja testlariga menyudan o'ta olmasdi (cardlar ham shu sahifa ichida).</p>
 *
 * <p>Shuning uchun: `leads` yo'q-u, `schedule` bor bo'lsa — darhol daraja testlariga
 * yo'naltiriladi. Ikkalasi ham yo'q bo'lsa — odatdagi "ruxsat yo'q" ko'rinishi.</p>
 */
export function FormsEntry() {
  const { can } = usePerm()
  if (!can('leads.forms', 'view') && can('schedule.levelTests', 'view'))
    return <Navigate to="/admin/level-tests" replace />
  return (
    <RequirePerm perm="leads.forms">
      <FormsPage />
    </RequirePerm>
  )
}
