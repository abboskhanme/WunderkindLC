import { Navigate } from 'react-router-dom'
import { Card } from '@/components/ui/Card'
import { usePerm } from '@/lib/permissions'

/** Bo'lim sahifalari — ustuvorlik TARTIBIDA (kundalik ishdan sozlamaga qarab). */
const PAGES: { perm: string; to: string }[] = [
  { perm: 'kpi.today', to: '/admin/boshqaruv/kpi/bugun' },
  { perm: 'kpi.month', to: '/admin/boshqaruv/kpi/oy' },
  { perm: 'kpi.tickets', to: '/admin/boshqaruv/kpi/tiketlar' },
  { perm: 'kpi.close', to: '/admin/boshqaruv/kpi/yopish' },
  { perm: 'kpi.rules', to: '/admin/boshqaruv/kpi/qoidalar' },
]

/**
 * «KPI» bo'limining KIRISH nuqtasi (`/admin/boshqaruv/kpi`).
 *
 * <p>Menyuda bo'lim BITTA band, ichida esa alohida beriladigan beshta sahifa bor. Agar bu
 * yerda yalang `RequirePerm perm="kpi.today"` tursa, faqat «Tiketlar» ruxsati berilgan xodim
 * menyudan kelib "ruxsatingiz yo'q" kartasiga tushib qolardi — cardlar ham shu sahifa ichida
 * bo'lgani uchun o'ziga OCHIQ sahifaga o'ta olmasdi. Shuning uchun u O'ZIGA ochiq BIRINCHI
 * sahifaga yo'naltiriladi (`TeachersEntry` / `FormsEntry` bilan bir xil usul).</p>
 *
 * <p>⚠️ Tartib ATAYIN kundalik ishdan boshlanadi: xodim uchun «Bugun» — har kuni ochiladigan
 * sahifa, «Qoidalar» esa oyda bir marta. Ikkalasi ham ochiq bo'lsa «Bugun» ochiladi.</p>
 */
export function KpiEntry() {
  const { can } = usePerm()
  const first = PAGES.find((p) => can(p.perm, 'view'))
  if (first) return <Navigate to={first.to} replace />

  // Bo'lim menyuda ko'rinib, ichida hech narsa ochilmasligi mumkin emas (menyu ham `kpi`
  // ruxsati bo'yicha filtrlanadi), lekin sahifa manzilini QO'LDA kiritish holati qoladi.
  return (
    <Card>
      <p className="py-12 text-center text-slate-400">Bu bo'limga ruxsatingiz yo'q.</p>
    </Card>
  )
}
