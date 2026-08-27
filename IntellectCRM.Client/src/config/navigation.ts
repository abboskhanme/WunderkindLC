import type { LucideIcon } from 'lucide-react'
import {
  LayoutDashboard,
  UserPlus,
  Users,
  GraduationCap,
  School,
  NotebookText,
  Wallet,
  Banknote,
  MessageSquare,
  MessagesSquare,
  ClipboardCheck,
  Settings,
  Smartphone,
  Building2,
  BookOpen,
  Archive,
  Megaphone,
  PhoneCall,
  BarChart3,
} from 'lucide-react'
import type { Role } from '@/types'
import { navReports, reportPerms } from './reports'

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

export interface NavItem {
  label: string
  to: string
  icon: LucideIcon
  children?: NavChild[]
  /** Bo'lim ruxsat kaliti (o'qituvchi/xodim filtri uchun; yo'q = har doim ko'rinadi) */
  perm?: string
  /** Ruxsatlardan BIRORTASI yetarli — <see cref="NavChild.permAny"/> bilan bir xil qoida */
  permAny?: string[]
  /** Faqat shu rollarga ko'rinadi (yo'q = barcha rollarga) */
  roles?: Role[]
}

/** Har bir rol uchun yon menyu (sidebar) elementlari */
export const navByRole: Record<Role, NavItem[]> = {
  admin: [
    { label: 'Bosh sahifa', to: '/admin', icon: LayoutDashboard },
    // "CRM statistika" HISOBOTLAR bo'limiga ko'chdi (marshrut o'zgarmadi) — shundan keyin
    // guruhda bitta bola qolgani uchun band YOYILMAYDIGAN qilib tekislandi: bitta bolali
    // ochiladigan menyu ortiqcha bosish talab qilardi.
    { label: 'Lidlar', to: '/admin/leads', icon: UserPlus, perm: 'leads.list' },
    {
      // Guruhning O'ZIDA `perm` YO'Q — bolalarga ko'chirilgan ("Sozlamalar" bilan bir xil sabab):
      // "Bog'lanish kerak" boshqa ruxsat (`contacts`) bilan ishlaydi, guruhda `perm: 'students'`
      // qolsa faqat `contacts` berilgan operator uni umuman ko'rmasdi. Bolalari qolmagan guruhni
      // Sidebar o'zi yashiradi (filterNav) — ruxsatsiz xodimga guruh baribir ko'rinmaydi.
      label: "O'quvchilar",
      to: '/admin/students',
      icon: Users,
      children: [
        { label: "O'quvchilar ro'yxati", to: '/admin/students', end: true, perm: 'students.list' },
        { label: "Bog'lanish kerak", to: '/admin/students/boglanish', perm: 'contacts' },
        // Izohlarga javoblar — profillarga yozilgan izohlar bir joyda (izohning O'ZI profilda
        // yoziladi, bu yerda esa "kimda izoh bor" savoliga javob beriladi).
        { label: 'Izohlarga javoblar', to: '/admin/students/izohlar', perm: 'students.notes' },
        { label: "O'quvchilar davomati", to: '/admin/students/davomat', perm: 'students.attendance' },
        // Bonus — MOLIYA ruxsati (`finance.bonus`), menyudagi joyi esa o'quvchilar bilan.
        { label: 'Bonus hisoboti', to: '/admin/students/bonus', perm: 'finance.bonus' },
        { label: 'Turniket', to: '/admin/students/turniket', perm: 'students.turnstile' },
        // Yuz bilan kirish — o'quvchi ilovasiga yangi qurilmadan kirishdagi selfi tekshiruvi.
        { label: 'Yuz bilan kirish', to: '/admin/students/yuz', perm: 'students.face' },
      ],
    },
    // O'qituvchilar — BITTA band. Ro'yxati/Davomati/Hisoboti sahifalari orasida sahifa tepasidagi
    // cardlar orqali o'tiladi (`CardTabs` + `config/sectionTabs.ts`), marshrutlar o'zgarmagan.
    // ⚠️ `permAny` — sahifalari alohida beriladi (Ro'yxati / Davomati / O'rinbosarlar). Faqat
    // bittasiga ruxsati bor xodim ham bandni ko'rishi kerak; `/admin/teachers` esa `TeachersEntry`
    // orqali unga OCHIQ sahifaga yo'naltiradi (FormsEntry bilan bir xil usul).
    {
      label: "O'qituvchilar",
      to: '/admin/teachers',
      icon: GraduationCap,
      permAny: ['teachers.list', 'teachers.attendance', 'teachers.substitutions', 'teacherReports'],
    },
    { label: 'Guruhlar', to: '/admin/classes', icon: School, perm: 'classes.list' },
    {
      label: "O'quv bo'limi",
      to: '/admin/oquv-bolimi',
      icon: BookOpen,
      children: [
        { label: 'Kurslar', to: '/admin/subjects', perm: 'schedule.courses' },
        { label: "O'quv dasturi", to: '/admin/curricula', perm: 'schedule.curricula' },
        { label: 'Baholash mezonlari', to: '/admin/grading', perm: 'schedule.grading' },
        // Xonalar — ilgari yuqori darajadagi alohida menyu edi; ikkinchi sahifasi
        // ("Samaradorlik") endi sahifa tepasidagi cardlardan ochiladi.
        { label: 'Xonalar', to: '/admin/rooms', perm: 'classes.rooms' },
        { label: 'Testlar natijalari', to: '/admin/test-results', perm: 'classes.testResults' },
        // "Formalar" — BITTA band, ichida ikki turdagi forma: lid formalari (`leads`) va
        // daraja testlari (`schedule`). Menyuda ikkalasidan BIRORTASI bo'lsa ko'rinadi, ichkarida
        // esa har bir card/sahifa o'z ruxsati bilan darvozalangan (`formTabs`, `RequirePerm`).
        { label: 'Formalar', to: '/admin/forms', permAny: ['leads.forms', 'schedule.levelTests'] },
        { label: 'Kitoblar sotuvi', to: '/admin/books', perm: 'books' },
        { label: 'Sabablar', to: '/admin/reasons', perm: 'settings.reasons' },
        { label: 'Shartnomalar', to: '/admin/contracts', perm: 'contracts' },
      ],
    },
    {
      // Chats — jonli yozishmalar (Xabarlar bo'limidagi ommaviy yuborishdan alohida).
      label: 'Chats',
      to: '/admin/chats',
      icon: MessagesSquare,
      perm: 'messages',
      children: [
        { label: 'Guruh chati', to: '/admin/chats', perm: 'messages.chat' },
        { label: 'Support Telegram', to: '/admin/support-telegram', perm: 'messages.support' },
      ],
    },
    { label: 'Xabarlar', to: '/admin/messages', icon: MessageSquare, perm: 'messages.broadcast' },
    {
      label: 'Ilova',
      to: '/admin/ai-check',
      icon: Smartphone,
      perm: 'app',
      children: [
        { label: 'AI check', to: '/admin/ai-check', perm: 'app.aiCheck' },
        { label: 'Support', to: '/admin/support', perm: 'app.support' },
        { label: 'Joylashuv', to: '/admin/locations', perm: 'app.locations' },
        { label: 'Ota-onalar', to: '/admin/parents', perm: 'app.parents' },
        { label: "O'qituvchilar", to: '/admin/app/teachers', perm: 'app.teachers' },
      ],
    },
    {
      label: 'Call Center', to: '/admin/calls', icon: PhoneCall, perm: 'calls',
      children: [
        { label: "Bulut (MoiZvonki)", to: '/admin/calls', end: true, perm: 'calls.cloud' },
        { label: 'Local Call', to: '/admin/calls/local', perm: 'calls.local' },
      ],
    },
    { label: 'Kassa', to: '/admin/kassa', icon: Banknote, perm: 'kassa' },
    { label: 'Moliya', to: '/admin/finance', icon: Wallet, perm: 'finance.main' },
    {
      // HISOBOTLAR — barcha analitika/hisobot bir joyda. Ro'yxat `config/reports.ts` da:
      // hub sahifasi ham, shu menyu ham AYNAN o'sha katalogdan quriladi (ayrilib ketmasin).
      //
      // ⚠️ Guruhda `perm` emas, `permAny` — katalogdagi BARCHA ruxsatlar: birorta hisoboti
      // bo'lmagan xodimga bo'lim umuman ko'rinmaydi (bosib bo'sh sahifaga tushmasin).
      //
      // Menyuda faqat SOF hisobotlar (`inNav`) — ular eski bo'limidan KO'CHIRILDI. Ichida
      // yozuvchi amal bor sahifalar (Moliya, Kitoblar, "Bog'lanish kerak" ...) o'z operativ
      // bo'limida QOLDI va hub sahifasida havola bo'lib turadi — kundalik ish yo'li uzilmasin.
      label: 'Hisobotlar',
      to: '/admin/hisobotlar',
      icon: BarChart3,
      permAny: reportPerms,
      children: [
        { label: 'Barcha hisobotlar', to: '/admin/hisobotlar', end: true, permAny: reportPerms },
        ...navReports.map((r) => ({ label: r.label, to: r.to, perm: r.perm })),
      ],
    },
    {
      label: 'Marketing',
      to: '/admin/marketing',
      icon: Megaphone,
      perm: 'marketing',
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
      label: 'Boshqaruv',
      to: '/admin/boshqaruv/staff',
      icon: Building2,
      children: [
        { label: 'Vakansiyalar', to: '/admin/boshqaruv/vacancies', perm: 'vacancies' },
        { label: 'Kameralar', to: '/admin/boshqaruv/cameras', perm: 'cameras' },
        { label: 'Filiallar', to: '/admin/boshqaruv/branches', roles: ['superadmin'] },
        { label: 'Adminga topshiriq', to: '/admin/boshqaruv/staff-tasks', roles: ['superadmin'] },
        { label: 'Xodimlar va rollar', to: '/admin/boshqaruv/staff', perm: 'staff' },
        { label: 'Taklif va shikoyatlar', to: '/admin/boshqaruv/feedback', perm: 'feedback' },
      ],
    },
    {
      // Guruhning O'ZIDA `perm` YO'Q — u bolalarga ko'chirilgan. Tarixiy sabab: bu yerda
      // "O'zgarishlar tarixi" turardi va u boshqa ruxsat (`audit`) bilan ishlardi; endi u
      // HISOBOTLAR bo'limida (marshrut o'zgarmadi). Qoida ATAYIN saqlanyapti — Sidebar
      // bolalari qolmagan guruhni o'zi yashiradi (filterNav), ya'ni ruxsatsiz xodimga guruh
      // baribir ko'rinmaydi va yangi band qo'shilganda qayta o'ylash kerak bo'lmaydi.
      label: 'Sozlamalar',
      to: '/admin/settings/school',
      icon: Settings,
      children: [
        { label: "Markaz ma'lumotlari", to: '/admin/settings/school', perm: 'settings.school' },
        { label: 'Landing Boshqaruvi', to: '/admin/landing', perm: 'settings.landing' },
        { label: 'Tuman va maktablar', to: '/admin/districts', perm: 'settings.districts' },
        { label: 'Xabar kanallari', to: '/admin/settings/channels', perm: 'settings.channels' },
        { label: 'Zaxira nusxa', to: '/admin/settings/backup', perm: 'settings.backup' },
        { label: 'Mobil ilova (APK)', to: '/admin/settings/apk', perm: 'settings.apk' },
        { label: 'Speaking (Azure)', to: '/admin/settings/azure-speech', perm: 'settings.azure-speech' },
        { label: 'AI Tahlil (Gemini)', to: '/admin/settings/gemini', perm: 'settings.gemini' },
        { label: "To'lov cheki", to: '/admin/settings/check', perm: 'settings.check' },
        { label: 'Turniket integratsiya', to: '/admin/settings/turnstile', perm: 'settings.turnstile' },
        { label: 'Kamera integratsiya', to: '/admin/settings/cameras', perm: 'settings.cameras' },
        { label: 'PostHog Analitika', to: '/admin/settings/posthog', perm: 'settings.posthog' },
      ],
    },
    { label: 'Arxiv', to: '/admin/archive', icon: Archive, perm: 'settings.archive' },
  ],
  teacher: [
    { label: 'Bosh sahifa', to: '/teacher', icon: LayoutDashboard },
    { label: 'Jurnal', to: '/teacher/journal', icon: NotebookText, perm: 'journal' },
    // Asosiy navigatsiyada "Test" (onlayn/oflayn test yaratish).
    { label: 'Test', to: '/teacher/tests', icon: ClipboardCheck, perm: 'journal' },
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
