import type { ComponentType } from 'react'
import { LayoutDashboard, NotebookText, MessageSquare } from 'lucide-react'
// Admin menyusi — edutizim bilan BIR XIL ikonkalar (Tabler outline, edutizim ham shularni ishlatadi).
import {
  IconClipboardCheck,
  IconClipboardList,
  IconUsers,
  IconSchool,
  IconBook,
  IconWallet,
  IconShieldCheck,
  IconBriefcase,
  IconSpeakerphone,
  IconChartPie,
  IconSettings,
  IconSparkles,
} from '@tabler/icons-react'
import type { Role } from '@/types'
import { navReports, reportPerms } from './reports'

/**
 * edutizimdagi "Hisobotlar" guruhida O'Z joyi bor hisobotlar — ular "Future → Hisobotlar" ga
 * TUSHMAYDI (Future faqat edutizimda YO'Q narsalar uchun).
 */
export const edutizimReportRoutes = new Set([
  '/admin/crm-stats',
  '/admin/rooms/utilization',
  '/admin/teacher-reports',
])

/**
 * edutizimning O'ZIDA ikki guruhda turadigan bandlar (masalan "Kirim chiqim" — Moliya va
 * Hisobotlar, "Xodimlar reytingi" — Nazorat va Hisobotlar). Xodim ularni ikkala joydan ham
 * qidiradi, shuning uchun bizda ham ikki joyda. Boshqa takrorlar TAQIQLANGAN (test qulflaydi).
 */
export const edutizimDuplicateRoutes = new Set([
  '/admin/moliya/kirim-chiqim',
  '/admin/teacher-reports',
  // edutizimda "Shartnoma" IKKI bo'limda: O'quv bo'limi (`/course/contracts`) va Moliya
  // (`/finance/contract`). Bizda ikkalasi ham BITTA shartnomalar sahifasi.
  '/admin/contracts',
])

export interface NavChild {
  label: string
  to: string
  /** NavLink exact match (faqat shu manzilda faol) */
  end?: boolean
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
  /** Ruxsat kaliti — xodim (staff) shu bo'limga ega bo'lsagina ko'rinadi */
  perm?: string
  /**
   * Ruxsatlardan BIRORTASI yetarli (bir necha ruxsat bilan ishlaydigan band uchun — masalan
   * "Formalar" ichida lid formalari `leads`, daraja testi `schedule` ruxsatida). Sahifaning O'ZI
   * baribir `RequirePerm` bilan darvozalanadi; bu faqat MENYUda ko'rinish qoidasi.
   */
  permAny?: string[]
  /** Ichki bo'lim (3-daraja) — masalan "O'quv bo'limi" → "Guruhlar" → "Reyting" */
  children?: NavChild[]
}

/** Menyu ikonkasi — lucide ham, Tabler ham (ikkalasi ham `className` qabul qiladi). */
export type NavIcon = ComponentType<{ className?: string }>

export interface NavItem {
  label: string
  to: string
  icon: NavIcon
  children?: NavChild[]
  /** Bo'lim ruxsat kaliti (o'qituvchi/xodim filtri uchun; yo'q = har doim ko'rinadi) */
  perm?: string
  /** Ruxsatlardan BIRORTASI yetarli — <see cref="NavChild.permAny"/> bilan bir xil qoida */
  permAny?: string[]
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
  /**
   * Yon ro'yxat (flyout) ichidagi kichik guruhlar YONMA-YON ustun bo'lib chizilsin
   * (edutizimdagi "Moliya"). Yo'q bo'lsa — avvalgidek ustma-ust.
   */
  columns?: boolean
}

/** Har bir rol uchun yon menyu (sidebar) elementlari */
export const navByRole: Record<Role, NavItem[]> = {
  // ⚠️ ADMIN MENYUSI edutizim.uz bilan AYNAN BIR XIL (xodimlar o'sha tizimga o'rgangan —
  // docs/EDUTIZIM-PARITY.md). Guruhlar, ularning TARTIBI va NOMLARI edutizimdagidek; bizda
  // YO'Q bandlar (Gamifikatsiya, Onlayn kurs ...) ATAYIN qo'shilmagan. Bizda BOR, lekin
  // edutizimda yo'q bo'limlar eng pastdagi "Future" guruhiga yig'ilgan.
  //
  // Guruh bandlarining `to` si — SAHIFA EMAS, faqat KALIT ('#...'): edutizimda guruh bosilganda
  // sahifa ochilmaydi, yon tomonda ro'yxat chiqadi. '#' bilan boshlangan kalit hech qaysi
  // manzilga mos kelmaydi, ya'ni `activeNavTo` faqat BOLALAR bo'yicha qaror qiladi.
  admin: [
    { label: 'Topshiriqlar', to: '/admin/topshiriqlar', icon: IconClipboardCheck, perm: 'tasks' },
    {
      label: 'Lidlar',
      to: '#lidlar',
      icon: IconClipboardList,
      children: [
        { label: "Buyurtmalar ro'yxati", to: '/admin/leads', perm: 'leads.list' },
        { label: 'Birinchi darsga yozilganlar', to: '/admin/leads/birinchi-dars', perm: 'leads.list' },
      ],
    },
    {
      label: 'Guruh',
      to: '#guruh',
      icon: IconUsers,
      children: [
        { label: 'Guruh', to: '/admin/classes', perm: 'classes.list' },
        { label: 'Dars jadvali', to: '/admin/jadval', end: true, perm: 'schedule.timetable' },
        { label: 'Xonalar', to: '/admin/rooms', end: true, perm: 'classes.rooms' },
        { label: "Guruh o'quvchilari", to: '/admin/classes/oquvchilar', perm: 'classes.list' },
      ],
    },
    {
      label: "O'quvchilar",
      to: '#oquvchilar',
      icon: IconSchool,
      // Tartib edutizimdagidek: yangi → aktiv → arxiv → ro'yxat → ota-ona → obuna → manzillar.
      children: [
        { label: "Yangi o'quvchilar", to: '/admin/students/yangi', perm: 'students.list' },
        { label: "Aktiv o'quvchilar", to: '/admin/students/aktiv', perm: 'students.list' },
        { label: "Arxiv o'quvchilar", to: '/admin/students/arxiv', perm: 'students.list' },
        { label: "O'quvchilar ro'yxati", to: '/admin/students', end: true, perm: 'students.list' },
        { label: 'Ota-ona', to: '/admin/students/ota-ona', perm: 'students.list' },
        { label: 'Joriy oyda obunasi tugaydiganlar', to: '/admin/students/obuna-tugaydi', perm: 'students.list' },
        { label: "O'quvchilar manzillari", to: '/admin/locations', perm: 'app.locations' },
      ],
    },
    {
      label: "O'quv bo'limi",
      to: '#oquv-bolimi',
      icon: IconBook,
      children: [
        { label: 'Oflayn kurslar', to: '/admin/subjects', end: true, perm: 'schedule.courses' },
        { label: 'Shartnoma', to: '/admin/contracts', perm: 'contracts' },
      ],
    },
    {
      // ⚠️ Bir nechta band BITTA sahifaning TABLARI (`/admin/finance?tab=...`) — Sidebar ularni
      // `?tab=` bilan ham solishtiradi, aks holda to'rttasi birdan faol bo'lib ko'rinardi.
      label: 'Moliya',
      to: '#moliya',
      icon: IconWallet,
      // ⚠️ USTUNLARGA BO'LINGAN (edutizim): 14 ta band bitta ustunda juda uzun ro'yxat bo'lardi.
      // `columns: true` — flyout guruhlarni YONMA-YON chizadi (Sidebar → FlyoutColumns).
      columns: true,
      children: [
        {
          label: 'Amallar',
          to: '#moliya-amallar',
          children: [
            // edutizimdagi "Kassalar" — kassa QOLDIQLARI/kesimi. Bizdagi eng yaqin sahifa —
            // Moliya → "Kassirlar" tabi. To'lov QABUL QILISH oynamiz ("Kassa") edutizimda alohida
            // band emas (u yerda Kirim paneli), shuning uchun u "Future" da.
            { label: 'Kassalar', to: '/admin/finance?tab=cashiers', perm: 'finance.main' },
            { label: 'Bonus', to: '/admin/finance?tab=bonuses', perm: 'finance.main' },
            { label: 'Jarima', to: '/admin/moliya/jarima', perm: 'finance.main' },
            { label: 'Oylik chiqarish', to: '/admin/finance?tab=teachers', perm: 'finance.main' },
          ],
        },
        {
          label: 'Hisobotlar',
          to: '#moliya-hisobotlar',
          children: [
            { label: 'Kirim chiqim', to: '/admin/moliya/kirim-chiqim', perm: 'finance.main' },
            { label: 'Tushum rejasi', to: '/admin/moliya/tushum-rejasi', perm: 'finance.main' },
            { label: 'Moliya analitikasi', to: '/admin/moliya/analitika', perm: 'finance.main' },
            { label: 'Moliya hisobotlari', to: '/admin/moliya/hisobotlar', perm: 'finance.main' },
            { label: "Moliya hisobotlari (P&L)", to: '/admin/moliya/pnl', perm: 'finance.main' },
            { label: 'Pul oqimi', to: '/admin/moliya/pul-oqimi', perm: 'finance.main' },
          ],
        },
        {
          label: "Ma'lumotlar",
          to: '#moliya-malumotlar',
          children: [
            { label: 'Tranzaksiya turi', to: '/admin/moliya/tranzaksiya-turi', perm: 'finance.main' },
            { label: 'Tranzakisyalar', to: '/admin/finance?tab=payments', perm: 'finance.main' },
            { label: 'Rejalashtirilgan xarajatlar', to: '/admin/moliya/rejalashtirilgan-xarajatlar', perm: 'finance.main' },
            { label: 'Shartnoma', to: '/admin/contracts', perm: 'contracts' },
          ],
        },
      ],
    },
    {
      label: 'Nazorat',
      to: '#nazorat',
      icon: IconShieldCheck,
      children: [
        { label: 'Davomat', to: '/admin/students/davomat', perm: 'students.attendance' },
        { label: 'Fikr-mulohaza', to: '/admin/boshqaruv/feedback', perm: 'feedback' },
        { label: 'Xodimlar reytingi', to: '/admin/teacher-reports', perm: 'teacherReports' },
        { label: 'Davomat qilinmagan guruhlar', to: '/admin/nazorat/davomat-qilinmagan', perm: 'classes.notAttended' },
      ],
    },
    {
      // O'qituvchilar edutizimda alohida bo'lim emas — ular "Xodimlar" ichida.
      label: 'Boshqaruv',
      to: '#boshqaruv',
      icon: IconBriefcase,
      children: [
        {
          label: 'Xodimlar',
          to: '/admin/teachers',
          permAny: ['teachers.list', 'teachers.attendance', 'teachers.substitutions', 'teacherReports'],
        },
        { label: 'Rollar', to: '/admin/boshqaruv/staff', perm: 'staff' },
        { label: 'Filiallar', to: '/admin/boshqaruv/branches', roles: ['superadmin'] },
      ],
    },
    {
      label: 'Sotuv va marketing',
      to: '#sotuv',
      icon: IconSpeakerphone,
      children: [
        { label: "Xabarlar ro'yhati", to: '/admin/messages', perm: 'messages.broadcast' },
      ],
    },
    {
      label: 'Hisobotlar',
      to: '#hisobotlar',
      icon: IconChartPie,
      children: [
        { label: 'Sotuv voronkasi', to: '/admin/crm-stats', perm: 'leads.stats' },
        { label: 'Kirim chiqim', to: '/admin/moliya/kirim-chiqim', perm: 'finance.main' },
        { label: 'Xonalar analitikasi', to: '/admin/rooms/utilization', perm: 'classes.rooms' },
        { label: 'Xodimlar reytingi', to: '/admin/teacher-reports', perm: 'teacherReports' },
      ],
    },
    {
      label: 'Sozlamalar',
      to: '#sozlamalar',
      icon: IconSettings,
      children: [
        { label: 'Umumiy sozlamalar', to: '/admin/settings/school', perm: 'settings.school' },
        { label: 'Moliya', to: '/admin/settings/check', perm: 'settings.check' },
        { label: 'Ilova sozlamalari', to: '/admin/settings/apk', perm: 'settings.apk' },
      ],
    },
    {
      // FUTURE — bizda BOR, lekin edutizimda YO'Q bo'limlar (foydalanuvchi qarori). Ichi
      // 3-darajali: har kichik guruh yon ro'yxatda SARLAVHA bo'lib chiqadi.
      label: 'Future',
      to: '#future',
      icon: IconSparkles,
      children: [
        {
          label: "O'quvchilar",
          to: '#future-students',
          children: [
            { label: 'Arxiv (barcha turdagi)', to: '/admin/archive', perm: 'settings.archive' },
            { label: "Kassa (to'lov qabul qilish)", to: '/admin/kassa', perm: 'kassa' },
            { label: "Bog'lanish kerak", to: '/admin/students/boglanish', perm: 'contacts' },
            { label: 'Izohlarga javoblar', to: '/admin/students/izohlar', perm: 'students.notes' },
            { label: 'Ushlab turish bonusi', to: '/admin/students/bonus', perm: 'finance.bonus' },
            { label: 'Yuz bilan kirish', to: '/admin/students/yuz', perm: 'students.face' },
          ],
        },
        {
          label: "O'quv bo'limi",
          to: '#future-edu',
          children: [
            { label: "Jadval: bo'sh oraliqlar", to: '/admin/jadval/bosh-oraliqlar', perm: 'schedule.timetable' },
            { label: "O'quv dasturi", to: '/admin/curricula', perm: 'schedule.curricula' },
            { label: 'Baholash mezonlari', to: '/admin/grading', perm: 'schedule.grading' },
            { label: 'Testlar natijalari', to: '/admin/test-results', perm: 'classes.testResults' },
            { label: 'Formalar', to: '/admin/forms', permAny: ['leads.forms', 'schedule.levelTests'] },
            { label: 'Kitoblar sotuvi', to: '/admin/books', perm: 'books' },
            { label: 'Sabablar', to: '/admin/reasons', perm: 'settings.reasons' },
          ],
        },
        {
          label: 'Aloqa',
          to: '#future-comms',
          children: [
            { label: 'Guruh chati', to: '/admin/chats', perm: 'messages.chat' },
            { label: 'Support Telegram', to: '/admin/support-telegram', perm: 'messages.support' },
            { label: 'Call Center — Bulut', to: '/admin/calls', end: true, perm: 'calls.cloud' },
            { label: 'Call Center — Local Call', to: '/admin/calls/local', perm: 'calls.local' },
          ],
        },
        {
          label: 'Ilova',
          to: '#future-app',
          children: [
            { label: 'AI check', to: '/admin/ai-check', perm: 'app.aiCheck' },
            { label: 'Ota-onalar (ilova akkauntlari)', to: '/admin/parents', perm: 'app.parents' },
            { label: 'Support', to: '/admin/support', perm: 'app.support' },
            { label: "O'qituvchilar (ilova)", to: '/admin/app/teachers', perm: 'app.teachers' },
          ],
        },
        {
          label: 'Instagram marketing',
          to: '#future-marketing',
          children: [
            { label: 'Boshqaruv paneli', to: '/admin/marketing', end: true, perm: 'marketing.dashboard' },
            { label: 'Inbox', to: '/admin/marketing/inbox', perm: 'marketing.inbox' },
            { label: 'Javob qoidalari', to: '/admin/marketing/rules', perm: 'marketing.rules' },
            { label: 'FAQ tugmalari', to: '/admin/marketing/faq', perm: 'marketing.rules' },
            { label: 'Bilim bazasi', to: '/admin/marketing/knowledge', perm: 'marketing.knowledge' },
            { label: 'Reklama lidlari', to: '/admin/marketing/reklama-lidlari', perm: 'marketing.leadads' },
            { label: 'Reklama statistikasi', to: '/admin/marketing/reklama-statistikasi', perm: 'marketing.adsstats' },
            { label: 'Kontent', to: '/admin/marketing/kontent', perm: 'marketing.content' },
            { label: 'Sozlamalar', to: '/admin/marketing/settings', perm: 'marketing.settings' },
          ],
        },
        {
          // Hisobotlar hub'i va edutizimda yo'q hisobotlar — `config/reports.ts` katalogidan
          // (`navReports`), ya'ni katalogga qo'shilgan hisobot menyuda o'zi paydo bo'ladi.
          label: 'Hisobotlar',
          to: '#future-reports',
          permAny: reportPerms,
          children: [
            { label: 'Barcha hisobotlar', to: '/admin/hisobotlar', end: true, permAny: reportPerms },
            ...navReports
              .filter((r) => !edutizimReportRoutes.has(r.to))
              .map((r) => ({ label: r.label, to: r.to, perm: r.perm })),
          ],
        },
        {
          label: 'Boshqaruv',
          to: '#future-admin',
          children: [
            { label: 'KPI', to: '/admin/boshqaruv/kpi', perm: 'kpi' },
            { label: 'Vakansiyalar', to: '/admin/boshqaruv/vacancies', perm: 'vacancies' },
            { label: 'Kameralar', to: '/admin/boshqaruv/cameras', perm: 'cameras' },
          ],
        },
        {
          label: 'Sozlamalar',
          to: '#future-settings',
          children: [
            { label: 'Landing Boshqaruvi', to: '/admin/landing', perm: 'settings.landing' },
            { label: 'Tuman va maktablar', to: '/admin/districts', perm: 'settings.districts' },
            { label: 'Xabar kanallari', to: '/admin/settings/channels', perm: 'settings.channels' },
            { label: 'Zaxira nusxa', to: '/admin/settings/backup', perm: 'settings.backup' },
            { label: 'Speaking (Azure)', to: '/admin/settings/azure-speech', perm: 'settings.azure-speech' },
            { label: 'AI Tahlil (Gemini)', to: '/admin/settings/gemini', perm: 'settings.gemini' },
            { label: 'Turniket integratsiya', to: '/admin/settings/turnstile', perm: 'settings.turnstile' },
            { label: 'Kamera integratsiya', to: '/admin/settings/cameras', perm: 'settings.cameras' },
            { label: 'PostHog Analitika', to: '/admin/settings/posthog', perm: 'settings.posthog' },
          ],
        },
      ],
    },
  ],
  teacher: [
    { label: 'Bosh sahifa', to: '/teacher', icon: LayoutDashboard },
    { label: 'Jurnal', to: '/teacher/journal', icon: NotebookText, perm: 'journal' },
    // Asosiy navigatsiyada "Test" (onlayn/oflayn test yaratish).
    { label: 'Test', to: '/teacher/tests', icon: IconClipboardCheck, perm: 'journal' },
    { label: 'Xabarlar', to: '/teacher/messages', icon: MessageSquare, perm: 'messages' },
  ],
  student: [{ label: 'Bosh sahifa', to: '/student', icon: LayoutDashboard }],
  parent: [{ label: 'Bosh sahifa', to: '/parent', icon: LayoutDashboard }],
  // Superadmin admin bilan bir xil nav'ni ishlatadi (qo'shimcha menyusiz, faqat ruxsat farqli)
  superadmin: [],
  // Xodim ham admin nav'ini ishlatadi — Sidebar uni permissions bo'yicha filtrlaydi
  staff: [],
}

// Superadmin va xodim admin nav'ini qayta ishlatadi (Sidebar rol/ruxsat bo'yicha filtrlaydi).
navByRole.superadmin = navByRole.admin
navByRole.staff = navByRole.admin

/**
 * Manzil shu marshrutga to'g'ri keladimi — kelsa marshrut UZUNLIGI, aks holda 0.
 * Uzunlik "qanchalik ANIQ mos kelgani" o'lchovi bo'lib ishlatiladi.
 */
function matchLength(to: string, pathname: string): number {
  // `?tab=` kabi parametr solishtirishga kirmaydi — u sahifani emas, sahifa ICHIDAGI
  // bo'limni tanlaydi (`lib/tabParam.ts`).
  const path = to.split('?')[0]
  return pathname === path || pathname.startsWith(path + '/') ? path.length : 0
}

/** Band (yoki uning bolalaridan biri) manzilga qanchalik ANIQ mos kelgani. */
export function navMatchScore(
  item: { to: string; children?: NavChild[] },
  pathname: string,
): number {
  let best = matchLength(item.to, pathname)
  for (const c of item.children ?? []) best = Math.max(best, navMatchScore(c, pathname))
  return best
}

/**
 * Joriy manzil QAYSI yuqori darajadagi menyu bandiga tegishli — ENG ANIQ (eng uzun)
 * moslik g'olib. Mos band yo'q bo'lsa `null`.
 *
 * ⚠️ NEGA KERAK: ilgari har guruh o'zini MUSTAQIL tekshirardi (`pathname.startsWith(...)`),
 * shuning uchun bitta manzil bir NECHTA guruhni ochib yuborardi. "Hisobotlar" bo'limi
 * paydo bo'lgach bu ko'zga tashlandi: `/admin/subjects/analitika` ochilganda
 * "Kurslar analitikasi" ham, ESKI "O'quv bo'limi" ham (chunki u yerda `/admin/subjects`
 * bor) birga ochilardi. Endi manzil FAQAT bitta bandga tegishli bo'ladi — eng aniq
 * mos kelganiga.
 */
export function activeNavTo(items: NavItem[], pathname: string): string | null {
  let bestTo: string | null = null
  let bestScore = 0
  for (const item of items) {
    const score = navMatchScore(item, pathname)
    if (score > bestScore) {
      bestScore = score
      bestTo = item.to
    }
  }
  return bestTo
}

/** Rol bo'yicha asosiy sahifa manzili */
export const homeByRole: Record<Role, string> = {
  superadmin: '/admin',
  admin: '/admin',
  teacher: '/teacher',
  // O'quvchi va ota-ona — o'quvchi portali (mobil web ilova).
  student: '/student',
  parent: '/student',
  staff: '/admin',
}

/**
 * FAQAT KASSA xodimimi? — ruxsatlari orasida boshqa bo'lim yo'q (masalan `["kassa"]` yoki
 * `["kassa:create"]`). Bunday xodim uchun admin paneli (bosh sahifa, yon menyu) KERAK EMAS:
 * u telefondagi kassa portalida (`/kassa`) ishlaydi.
 */
export function isKassaOnly(user: { role: Role; permissions?: string[] | null } | null): boolean {
  if (!user || user.role !== 'staff') return false
  const perms = user.permissions ?? []
  if (perms.length === 0) return false
  const sections = new Set(perms.map((p) => p.split(':')[0]))
  return sections.size === 1 && sections.has('kassa')
}

/** Foydalanuvchining bosh sahifasi — rol, kassa xodimi uchun esa kassa portali. */
export function homeFor(user: { role: Role; permissions?: string[] | null } | null): string {
  if (!user) return '/login'
  if (isKassaOnly(user)) return '/kassa'
  return homeByRole[user.role]
}

export const roleLabels: Record<Role, string> = {
  superadmin: 'Tizim egasi',
  admin: 'Administrator',
  teacher: "O'qituvchi",
  student: "O'quvchi",
  parent: 'Ota-ona',
  staff: 'Xodim',
}
