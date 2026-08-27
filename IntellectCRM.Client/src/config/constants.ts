import type { ClassLanguage, FinanceDirection, Gender, MonthStatus } from '@/types'

export const genderLabels: Record<Gender, string> = {
  male: 'Erkak',
  female: 'Ayol',
}

export const genderOptions: { value: Gender; label: string }[] = [
  { value: 'male', label: 'Erkak' },
  { value: 'female', label: 'Ayol' },
]

/** O'qituvchi toifalari (vestigial — maosh endi per-guruh, o'qituvchi "Maosh" tabida belgilanadi) */
export const teacherCategories: { value: string; label: string }[] = [
  { value: 'oliy', label: 'Oliy toifa' },
  { value: '1', label: '1-toifa' },
  { value: '2', label: '2-toifa' },
  { value: 'mutaxasis', label: 'Mutaxasis' },
]
export const teacherCategoryLabel = (c?: string): string =>
  teacherCategories.find((x) => x.value === c)?.label ?? '—'

/**
 * Lid manbalari (CRM) — FALLBACK: manbalar endi serverdan (`/admin/lead-sources`,
 * Sozlamalar → Sabablar → "Lid manbalari") keladi; bu ro'yxat faqat server bo'sh/xato
 * bo'lganda ishlatiladi (LeadFormModal, LeadsPage filtri).
 */
export const leadSourceOptions: string[] = [
  'Instagram',
  'Referral',
  'Sayt',
  'Telegram',
  'Tashrif',
  'Boshqa',
]

export const languageLabels: Record<ClassLanguage, string> = {
  uz: "O'zbek",
  ru: 'Rus',
}

export const languageOptions: { value: ClassLanguage; label: string }[] = [
  { value: 'uz', label: "O'zbek" },
  { value: 'ru', label: 'Rus' },
]

/**
 * Xodimlar (barcha o'qituvchi + admin) umumiy guruh chati uchun maxsus kanal kaliti.
 * Backenddagi ChatService.StaffChannel bilan bir xil bo'lishi shart.
 */
export const STAFF_CHANNEL = '__xodimlar__'
/** Xodimlar kanalining ko'rsatiladigan nomi */
export const STAFF_CHANNEL_LABEL = 'Xodimlar'

/** O'qituvchi web paneli bo'limlari (admin ruxsat beradi). Kalitlar backend bilan bir xil. */
export const teacherPermissions: { key: string; label: string }[] = [
  { key: 'journal', label: 'Jurnal' },
  { key: 'schedule', label: 'Dars jadvali' },
  { key: 'messages', label: 'Xabarlar (chat)' },
  { key: 'salary', label: 'Maosh' },
]

/** Bo'lim ichidagi BITTA sahifa — alohida berilishi/berilmasligi mumkin. */
export interface AdminPermPage {
  /** Ruxsat kaliti: `"bolim.sahifa"` (nuqta ajratkich). */
  key: string
  label: string
}

/** Ruxsat matritsasidagi bo'lim (va uning sahifalari). */
export interface AdminPermSection {
  key: string
  label: string
  /** Bo'limning alohida beriladigan sahifalari (yo'q = bo'lim bitta sahifadan iborat). */
  pages?: AdminPermPage[]
}

/**
 * Xodim (role="staff") admin panelida ko'ra oladigan BO'LIMLAR va ularning SAHIFALARI.
 * Kalitlar nav (navigation.ts), route himoyasi (RequirePerm) va serverdagi `[AdminPerm]` bilan
 * AYNAN bir xil. Superadmin "Xodimlar va rollar" bo'limida belgilaydi.
 * (Filiallar bu ro'yxatda yo'q — u faqat superadmin uchun.)
 *
 * ⚠️ **BO'LIM = "hammasi"**: yalang `"students"` tokeni shu bo'limning BARCHA sahifalarini
 * ochadi (eski xodim ruxsatlari shu sababdan o'zgarmadi). Bitta sahifa kerak bo'lsa —
 * `"students.turnstile"` kabi SAHIFA kaliti beriladi. Qoida `lib/permissions.ts` da
 * (`can`), server tomonda esa `PermissionRules` da — ikkalasi bir xil.
 *
 * ⚠️ **YANGI SAHIFA QO'SHSANGIZ** — uni shu yerga ham qo'shing va `navigation.ts` + `App.tsx`
 * da AYNAN shu kalitni ishlating; serverda sahifaning o'z controlleri bo'lsa `[AdminPerm]` ni
 * ham sahifa kalitiga o'tkazing. Aks holda sahifa "bo'lim ruxsati" ichida yashirinib qoladi va
 * uni alohida berib bo'lmaydi (`.claude/rules/permissions.md`).
 */
export const adminPermissions: AdminPermSection[] = [
  {
    key: 'marketing',
    label: 'Marketing',
    pages: [
      { key: 'marketing.dashboard', label: 'Boshqaruv paneli' },
      { key: 'marketing.inbox', label: 'Inbox' },
      { key: 'marketing.rules', label: 'Javob qoidalari' },
      { key: 'marketing.knowledge', label: 'Bilim bazasi' },
      { key: 'marketing.analytics', label: 'Analitika' },
      { key: 'marketing.leadads', label: 'Reklama lidlari' },
      { key: 'marketing.adsstats', label: 'Reklama statistikasi' },
      { key: 'marketing.content', label: 'Kontent' },
      { key: 'marketing.quality', label: 'Javob sifati' },
      { key: 'marketing.settings', label: 'Sozlamalar' },
    ],
  },
  {
    key: 'leads',
    label: 'Lidlar',
    pages: [
      { key: 'leads.list', label: 'Lidlar (Kanban)' },
      { key: 'leads.stats', label: 'CRM statistika' },
      { key: 'leads.forms', label: 'Lid formalari' },
    ],
  },
  {
    key: 'students',
    label: "O'quvchilar",
    pages: [
      { key: 'students.list', label: "O'quvchilar ro'yxati va profili" },
      { key: 'students.notes', label: 'Izohlarga javoblar' },
      { key: 'students.attendance', label: "O'quvchilar davomati" },
      { key: 'students.turnstile', label: 'Turniket' },
      { key: 'students.face', label: 'Yuz bilan kirish' },
    ],
  },
  // BOG'LANISH KERAK — o'quvchi bilan bog'lanish navbati (follow-up) va uning hisobotlari.
  // O'quvchilar bo'limidan ATAYIN alohida: navbat bilan ishlaydigan operatorga o'quvchilar
  // bo'limini to'liq ochish shart emas ("Kassa" "Moliya"dan alohida bo'lgani bilan bir xil
  // mantiq). Bu ruxsat o'quvchi profilidagi "Bog'lanish kerak" tugmasini ham ochadi.
  { key: 'contacts', label: "Bog'lanish kerak" },
  {
    key: 'teachers',
    label: "O'qituvchilar",
    pages: [
      { key: 'teachers.list', label: "O'qituvchilar ro'yxati va profili" },
      { key: 'teachers.attendance', label: "O'qituvchilar davomati" },
      { key: 'teachers.substitutions', label: "O'rinbosarlar" },
    ],
  },
  {
    // Tarixiy kalit `schedule` (ilgari "Dars jadvali") — menyuda "O'quv bo'limi".
    key: 'schedule',
    label: "O'quv bo'limi (kurslar)",
    pages: [
      { key: 'schedule.courses', label: 'Kurslar' },
      { key: 'schedule.timetable', label: 'Dars jadvali' },
      { key: 'schedule.analytics', label: 'Kurslar analitikasi' },
      { key: 'schedule.curricula', label: "O'quv dasturi" },
      { key: 'schedule.grading', label: 'Baholash mezonlari' },
      { key: 'schedule.levelTests', label: 'Daraja testlari' },
    ],
  },
  {
    key: 'classes',
    label: 'Guruhlar',
    pages: [
      { key: 'classes.list', label: 'Guruhlar va jurnal' },
      { key: 'classes.rooms', label: 'Xonalar' },
      { key: 'classes.testResults', label: 'Testlar natijalari' },
    ],
  },
  {
    key: 'messages',
    label: 'Xabarlar',
    pages: [
      { key: 'messages.broadcast', label: 'Ommaviy xabarlar (SMS/Push)' },
      { key: 'messages.chat', label: 'Guruh chati' },
      { key: 'messages.support', label: 'Support Telegram' },
    ],
  },
  {
    key: 'app',
    label: 'Ilova',
    pages: [
      { key: 'app.aiCheck', label: 'AI check' },
      { key: 'app.support', label: 'Support (ilova)' },
      { key: 'app.locations', label: 'Joylashuv' },
      { key: 'app.parents', label: 'Ota-onalar' },
      { key: 'app.teachers', label: "O'qituvchilar (ilova)" },
    ],
  },
  { key: 'teacherReports', label: "O'qituvchilar hisoboti" },
  { key: 'contracts', label: 'Shartnomalar' },
  // Kitoblar sotuvi — ombor (kitob/narx/qoldiq), botdan tushgan buyurtmalarni tasdiqlash va hisobotlar.
  { key: 'books', label: 'Kitoblar sotuvi' },
  // Kassa — FAQAT pul qabul qilish ish o'rni (o'quvchini topib to'lov kiritish). Moliyadan farqi:
  // hisobotlar, maosh, chiqimlar KO'RINMAYDI — kassirga shu bittasini berish kifoya.
  { key: 'kassa', label: 'Kassa' },
  {
    key: 'finance',
    label: 'Moliya',
    // ⚠️ «Bonus hisoboti» menyuda O'QUVCHILAR guruhida turadi, lekin ruxsati MOLIYAda:
    // u pul beradigan amal (o'qituvchi/o'quvchiga bonus). Ilgari uni KO'RISH `students`,
    // YOZISH esa `finance` talab qilardi — nomuvofiqlik shu bilan tugatildi.
    pages: [
      { key: 'finance.main', label: 'Moliya (kirim/chiqim, hisobotlar, maosh)' },
      { key: 'finance.bonus', label: 'Bonus hisoboti' },
    ],
  },
  {
    key: 'calls',
    label: 'Call Center',
    pages: [
      { key: 'calls.cloud', label: 'Bulut (MoiZvonki)' },
      { key: 'calls.local', label: 'Local Call' },
    ],
  },
  {
    key: 'settings',
    label: 'Sozlamalar',
    // ⚠️ Kalitning ikkinchi qismi `/admin/settings/:section` URL segmenti bilan AYNAN bir xil —
    // marshrut darvozasi shu bo'yicha quriladi (`settingsPagePerm`). Landing/Tuman/Sabablar/Arxiv
    // esa alohida manzillarda, lekin ruxsat lineyasi bo'yicha shu bo'limga tegishli.
    pages: [
      { key: 'settings.school', label: "Markaz ma'lumotlari" },
      { key: 'settings.landing', label: 'Landing Boshqaruvi' },
      { key: 'settings.districts', label: 'Tuman va maktablar' },
      { key: 'settings.reasons', label: 'Sabablar' },
      { key: 'settings.channels', label: 'Xabar kanallari' },
      { key: 'settings.backup', label: 'Zaxira nusxa' },
      { key: 'settings.apk', label: 'Mobil ilova (APK)' },
      { key: 'settings.azure-speech', label: 'Speaking (Azure)' },
      { key: 'settings.gemini', label: 'AI Tahlil (Gemini)' },
      { key: 'settings.check', label: "To'lov cheki" },
      { key: 'settings.turnstile', label: 'Turniket integratsiya' },
      { key: 'settings.cameras', label: 'Kamera integratsiya' },
      { key: 'settings.posthog', label: 'PostHog Analitika' },
      { key: 'settings.archive', label: 'Arxiv' },
    ],
  },
  // O'ZGARISHLAR TARIXI (audit) — "kim, qachon, nimani o'zgartirdi". Ikki joyni ochadi:
  //   1) Sozlamalar → "O'zgarishlar tarixi" sahifasi (bo'limlarga ajratilgan umumiy tarix);
  //   2) o'quvchi/guruh/o'qituvchi/moliya sahifalaridagi "Tarix" bo'limlari.
  // ⚠️ Bu BUTUN tarixni ochadi — to'lov summalari, maosh va ruxsat o'zgarishlari ham ko'rinadi.
  // Bo'limlarga bo'lib berish ATAYIN qilinmagan: bitta tushunarli kalit, kimga berilishi o'ylab
  // tanlansin. Admin/superadmin bu ruxsatsiz ham ko'radi (odatdagi qoida).
  { key: 'audit', label: "O'zgarishlar tarixi" },
  { key: 'staff', label: 'Xodimlar' },
  { key: 'feedback', label: 'Taklif va shikoyatlar' },
  { key: 'cameras', label: 'Kameralar' },
  // Vakansiyalar — ishga qabul moduli (Intellect Career boti + `/vakansiya` Mini App):
  // vakansiya e'lonlari, nomzod arizalari va "Biz haqimizda" bloki.
  { key: 'vacancies', label: 'Vakansiyalar' },
  // Bosh sahifadagi markaz AI tahlili — DEFAULT faqat superadmin ko'radi; xodimga shu yerdan
  // ruxsat beriladi ("Ko'rish" = karta ko'rinadi, "Qo'shish" = qo'lda tahlil yaratish tugmasi).
  { key: 'ai', label: 'AI tahlil (bosh sahifa)' },
  // A'zolikni AKTIVLASHTIRISHDAGI «Bonus hisoblansin» ptichkasi. DIQQAT: bu ruxsat boshqalardan
  // QATTIQROQ — oddiy `admin` roli ham KO'RMAYDI, faqat SUPERADMIN va shu yerdan ruxsat berilgan
  // xodim (markaz egasi bonusga kirishni o'zida qoldirishni tanlagan). Tekshiruv:
  // `useSuperOrGranted('retentionBonus')` — `can()` bu yerda YARAMAYDI, u admin uchun true qaytaradi.
  { key: 'retentionBonus', label: 'Bonus ptichkasi (aktivlashtirishda)' },
]

/** Bo'lim kaliti bo'yicha uning sahifa kalitlari (matritsa va yoyish/ixchamlash uchun). */
export function permPagesOf(section: string): string[] {
  return adminPermissions.find((s) => s.key === section)?.pages?.map((p) => p.key) ?? []
}

/** Ruxsat kalitining (bo'lim yoki sahifa) o'qiladigan nomi; topilmasa kalitning o'zi. */
export function permLabel(key: string): string {
  for (const s of adminPermissions) {
    if (s.key === key) return s.label
    const page = s.pages?.find((p) => p.key === key)
    if (page) return `${s.label} → ${page.label}`
  }
  return key
}

/**
 * `/admin/settings/:section` uchun ruxsat kaliti. Katalogda bo'lmagan (yangi/noma'lum) segment
 * bo'lim kalitiga tushadi — sahifa JIMGINA yopilib qolmasin.
 */
export function settingsPagePerm(section: string | undefined): string {
  const key = `settings.${section ?? ''}`
  return permPagesOf('settings').includes(key) ? key : 'settings'
}

/* ---------- Moliya ---------- */

export const financeDirectionLabels: Record<FinanceDirection, string> = {
  income: 'Kirim',
  expense: 'Chiqim',
}

export interface CategoryOption {
  value: string
  label: string
}

/** Kirim toifalari */
export const incomeCategories: CategoryOption[] = [
  { value: 'tuition', label: "O'quvchi to'lovi" },
  { value: 'donation', label: 'Homiylik' },
  { value: 'rent_in', label: 'Ijaradan kirim' },
  { value: 'other', label: 'Boshqa kirim' },
]

/** Chiqim toifalari */
export const expenseCategories: CategoryOption[] = [
  { value: 'salary', label: 'Oylik maosh' },
  { value: 'utilities', label: 'Kommunal' },
  { value: 'supplies', label: 'Jihoz/materiallar' },
  { value: 'rent', label: 'Ijara' },
  { value: 'repair', label: "Ta'mirlash" },
  { value: 'other', label: 'Boshqa chiqim' },
]

export const categoriesByDirection: Record<FinanceDirection, CategoryOption[]> = {
  income: incomeCategories,
  expense: expenseCategories,
}

/** Toifa kodini o'qiladigan nomga aylantirish */
export function financeCategoryLabel(category: string): string {
  // Vozvrat — qo'lda kiritilmaydi (faqat to'lovdan qaytariladi), shuning uchun kategoriya ro'yxatida yo'q.
  if (category === 'refund') return 'Vozvrat'
  const all = [...incomeCategories, ...expenseCategories]
  return all.find((c) => c.value === category)?.label ?? category
}

/** To'lov usullari (kirim/to'lov uchun): kod -> yorliq. */
export const paymentMethods: { value: string; label: string }[] = [
  { value: 'cash', label: 'Naqd' },
  { value: 'card', label: 'Karta' },
  { value: 'bank', label: 'Bank orqali' },
]
/** To'lov usuli kodidan yorliq ("cash" -> "Naqd"). Bo'sh/noma'lum -> "—". */
export function paymentMethodLabel(method?: string | null): string {
  if (!method) return '—'
  return paymentMethods.find((m) => m.value === method)?.label ?? method
}

/** Qisqa o'zbekcha oy nomlari (1-12) */
export const monthShortNames: string[] = [
  'Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn',
  'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek',
]

/** "YYYY-MM" -> "May 2026" */
export function formatMonth(ym: string): string {
  const [y, m] = ym.split('-')
  return `${monthShortNames[Number(m) - 1] ?? m} ${y}`
}

export const monthStatusLabels: Record<MonthStatus, string> = {
  paid: "To'langan",
  partial: 'Qisman',
  unpaid: "To'lanmagan",
}

/**
 * O'quvchining holat belgisi (qidiruv natijalari va ro'yxatlar uchun). Arxiv eng ustun,
 * so'ng a'zolik holati (`Student.memberState`: active | trial | frozen | "").
 * `null` — normal holat (aktiv), belgi ko'rsatilmaydi.
 */
export function studentStateBadge(
  memberState?: string,
  isArchived?: boolean,
): { label: string; className: string } | null {
  if (isArchived) return { label: 'arxiv', className: 'bg-amber-100 text-amber-700' }
  if (memberState === 'frozen') return { label: 'muzlatilgan', className: 'bg-sky-100 text-sky-700' }
  if (memberState === 'trial') return { label: 'sinov', className: 'bg-violet-100 text-violet-700' }
  return null
}
