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
 *   `linkPerm` maqsad marshrutning `RequirePerm` i bilan AYNAN bir xil bo'lishi shart
 *   (`homeCards.test.ts` qulflaydi) — aks holda bosilgan kartochka "ruxsat yo'q" sahifasiga olib borardi.
 *
 * ⚠️ Kartochka SONI bilan ochiladigan ro'yxat MOS kelsin: ilgari 7 ta kartochka umumiy
 * `/admin/students` ga olib borardi, holbuki "Yangi/Aktiv/Arxiv o'quvchilar" va "Birinchi
 * darsga yozilganlar" sahifalari AYNAN shu kartochkalar ta'rifi bilan qurilgan. "Shu oy"
 * kartochkalari (`{month}` — `cardHref`) ro'yxatni KARTOCHKA hisoblangan oy bilan ochadi.
 * Yagona istisno — "Guruhlar": ro'yxatdagi saqlangan filtr (arxiv/kurs) sonni o'zgartirishi mumkin.
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
  // `status=lead&lost=hide` — aylantirilmagan, yo'qotilmagan VA birinchi darsga yozilmagan lidlar
  // (kartochka: `DashboardSummary.OpenLeads`; birinchi darsdagilar — keyingi kartochkada, 2026-10-09).
  { key: 'orders', label: 'Buyurtmalar', color: '#00D66F', icon: IconUserPlus, perm: 'leads.list', to: '/admin/leads?status=lead&lost=hide', linkPerm: 'leads.list' },
  // `color=upcoming` — qizil (o'tib ketgan) qatorlarsiz: kartochka faqat bugun va keyingilarini sanaydi.
  { key: 'firstLesson', label: 'Birinchi darsga keladiganlar', color: '#3D68FF', icon: IconUserCheck, perm: 'leads.list', to: '/admin/leads/birinchi-dars?color=upcoming', linkPerm: 'leads.list' },
  { key: 'newStudents', label: "Yangi o'quvchilar", color: '#9747FF', icon: IconUser, perm: 'students.list', to: '/admin/students/yangi', linkPerm: 'students.list' },
  { key: 'activeStudents', label: "Aktiv o'quvchilar", color: '#00D66F', icon: IconUserStar, perm: 'students.list', to: '/admin/students/aktiv', linkPerm: 'students.list' },
  // O'chirilgan lidlar arxivi ("Lidlar" tabi), faqat kartochka oyida o'chirilganlari.
  { key: 'ordersLeft', label: 'Buyurtmadan ketganlar', color: '#DE4141', icon: IconPlaylistX, perm: 'leads.list', to: '/admin/archive?month={month}', linkPerm: 'settings.archive' },
  // Ro'yxat serverda kartochka sonini hisoblaydigan funksiyaning O'ZIDAN (`DashboardSummary.MovementAsync`).
  { key: 'newLeft', label: "Yangi o'quvchidan ketganlar", color: '#DE4141', icon: IconUserMinus, perm: 'students.list', to: '/admin/students/ketganlar?type=new&month={month}', linkPerm: 'students.list' },
  { key: 'activeLeft', label: "Aktiv o'quvchidan ketganlar", color: '#DE4141', icon: IconUserOff, perm: 'students.list', to: '/admin/students/ketganlar?type=active&month={month}', linkPerm: 'students.list' },
  { key: 'debtors', label: 'Qarzdorlar', color: '#1D1D1D', icon: IconCashBanknote, perm: 'students.list', to: '/admin/students?preset=debtors', linkPerm: 'students.list' },
  { key: 'groups', label: 'Guruhlar', color: '#019EF7', icon: IconUsers, perm: 'classes.list', to: '/admin/classes', linkPerm: 'classes.list' },
  { key: 'firstPayments', label: "Birinchi to'lovni qilganlar", color: '#FBC400', icon: IconCash, perm: 'finance.main', to: '/admin/students/birinchi-tolov?month={month}', linkPerm: 'students.list' },
  { key: 'frozen', label: 'Muzlatilgan', color: '#019EF7', icon: IconSnowflake, perm: 'students.list', to: '/admin/students?preset=frozen', linkPerm: 'students.list' },
  { key: 'archived', label: 'Arxivlar', color: '#BDBDBD', icon: IconUserCog, perm: 'students.list', to: '/admin/students/arxiv', linkPerm: 'students.list' },
]

/**
 * Kartochka havolasi: `{month}` o'rniga kartochka HISOBLANGAN oy (`DashboardSummary.month`, server
 * vaqti). Oy hali noma'lum bo'lsa (javob kelmagan) — parametr olib tashlanadi va sahifa joriy oyni
 * oladi. ⚠️ `month={month}` havolada DOIM oxirgi parametr bo'lishi kerak (`homeCards.test.ts`).
 */
export function cardHref(card: Pick<HomeCard, 'to'>, month: string | null | undefined): string {
  if (!card.to.includes('{month}')) return card.to
  return month ? card.to.replace('{month}', encodeURIComponent(month)) : card.to.replace(/[?&]month=\{month\}$/, '')
}

/**
 * "O'quvchilar ro'yxati"ga kartochkadan kirish: `?preset=debtors|frozen`. Ro'yxat filtrlari
 * sessionStorage'da saqlanadi (`usePersistentState`), ya'ni oddiy havola avvalgi qidiruv/guruh
 * filtri bilan ochilib, kartochkadagi sondan BOSHQA son ko'rsatardi. Preset kelganda saqlangan
 * filtrlar tashlanadi va faqat kartochkaning sharti qo'yiladi (`StudentsPage`).
 */
export const STUDENT_LIST_PRESETS = ['debtors', 'frozen'] as const
export type StudentListPreset = (typeof STUDENT_LIST_PRESETS)[number]

/** Noma'lum yoki yo'q qiymat — `null` (eskirgan havola sahifani buzmasin). */
export function studentListPresetFromUrl(search: string): StudentListPreset | null {
  const raw = new URLSearchParams(search).get('preset')
  return raw && (STUDENT_LIST_PRESETS as readonly string[]).includes(raw) ? (raw as StudentListPreset) : null
}
