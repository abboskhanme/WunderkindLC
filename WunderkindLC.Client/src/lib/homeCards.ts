import {
  IconCash,
  IconCashBanknote,
  IconPlaylistX,
  IconSnowflake,
  IconUser,
  IconUserCheck,
  IconUserCog,
  IconUserMinus,
  IconUserOff,
  IconUserPlus,
  IconUserStar,
  IconUsers,
  type TablerIcon,
} from '@tabler/icons-react'
import type { DashboardSummary } from '@/api/services/dashboard'

/**
 * Bosh sahifaning 12 ta kartochkasi — edutizim tartibi va ranglari (`docs/edutizim/INVENTORY.md`
 * §1/§3). Komponentdan ALOHIDA (react-refresh: komponent fayli faqat komponent eksport qiladi).
 *
 * - `perm` — kartochka KO'RINADIMI (o'sha bo'lim ruxsati);
 * - `to` / `linkPerm` — bosilganda qaysi ro'yxatga o'tadi va u foydalanuvchiga ochiqmi.
 *   Maqsad sahifalar filtrni manzildan o'qimaydi, shuning uchun havola filtrsiz.
 */
export interface HomeCard {
  key: keyof Omit<DashboardSummary, 'month'>
  label: string
  color: string
  icon: TablerIcon
  perm: string
  to: string
  linkPerm: string
}

export const HOME_CARDS: HomeCard[] = [
  { key: 'orders', label: 'Buyurtmalar', color: '#00D66F', icon: IconUserPlus, perm: 'leads.list', to: '/admin/leads', linkPerm: 'leads.list' },
  { key: 'firstLesson', label: 'Birinchi darsga keladiganlar', color: '#3D68FF', icon: IconUserCheck, perm: 'leads.list', to: '/admin/leads', linkPerm: 'leads.list' },
  { key: 'newStudents', label: "Yangi o'quvchilar", color: '#9747FF', icon: IconUser, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'activeStudents', label: "Aktiv o'quvchilar", color: '#00D66F', icon: IconUserStar, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'ordersLeft', label: 'Buyurtmadan ketganlar', color: '#DE4141', icon: IconPlaylistX, perm: 'leads.list', to: '/admin/archive', linkPerm: 'settings.archive' },
  { key: 'newLeft', label: "Yangi o'quvchidan ketganlar", color: '#DE4141', icon: IconUserMinus, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'activeLeft', label: "Aktiv o'quvchidan ketganlar", color: '#DE4141', icon: IconUserOff, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'debtors', label: 'Qarzdorlar', color: '#1D1D1D', icon: IconCashBanknote, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'groups', label: 'Guruhlar', color: '#019EF7', icon: IconUsers, perm: 'classes.list', to: '/admin/classes', linkPerm: 'classes.list' },
  { key: 'firstPayments', label: "Birinchi to'lovni qilganlar", color: '#FBC400', icon: IconCash, perm: 'finance.main', to: '/admin/finance', linkPerm: 'finance.main' },
  { key: 'frozen', label: 'Muzlatilgan', color: '#019EF7', icon: IconSnowflake, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
  { key: 'archived', label: 'Arxivlar', color: '#BDBDBD', icon: IconUserCog, perm: 'students.list', to: '/admin/students', linkPerm: 'students.list' },
]
