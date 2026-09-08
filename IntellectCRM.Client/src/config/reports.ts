import type { LucideIcon } from 'lucide-react'
import { UserPlus, Users, GraduationCap, BookOpen, Wallet, Megaphone, History } from 'lucide-react'

/**
 * HISOBOTLAR KATALOGI — markazdagi barcha analitika/hisobot yuzalarining YAGONA manbai.
 *
 * Shu ro'yxatdan IKKALASI quriladi:
 *   1. yon menyudagi "Hisobotlar" guruhi (`inNav: true` bo'lganlar);
 *   2. `/admin/hisobotlar` hub sahifasi (HAMMASI).
 * Ikki joyda alohida yozilsa ular vaqt o'tib ayrilib ketardi — menyuda bor, hub'da yo'q
 * (yoki teskarisi) bo'lib, foydalanuvchiga "hisobot yo'qolgan" ko'rinardi.
 *
 * ⚠️ MARSHRUTLAR O'ZGARMAYDI. Katalog mavjud sahifalarga YO'L ko'rsatadi, ya'ni eski
 * havolalar, xatcho'plar va sahifalar orasidagi ichki linklar ishlayveradi.
 */
export interface ReportLink {
  /** Ekranda ko'rinadigan nom. */
  label: string
  /** Manzil. Sahifa ICHIDAGI hisobot bo'lsa `?tab=` bilan (qarang `lib/tabParam.ts`). */
  to: string
  /** Ko'rish uchun kerakli ruxsat kaliti — sahifaning O'ZIDAGI `RequirePerm` bilan bir xil. */
  perm: string
  /** "Bu hisobot qaysi savolga javob beradi" — nom o'zi ko'pincha yetarli emas. */
  description: string
  /**
   * Yon menyudagi "Hisobotlar" guruhida ham chiqadimi.
   *
   * FAQAT **sof** hisobotlarda `true`: ular eski bo'limidan KO'CHIRILDI, chunki u yerda
   * hech qanday amal bilan bog'liq emas edi. Ichida yozuvchi amal bor sahifalar (masalan
   * Moliya — tranzaksiya kiritish, O'quvchilar davomati — SMS yuborish) o'z operativ
   * bo'limida QOLADI va bu yerda faqat hub sahifasidan havola bo'lib turadi: aks holda
   * kundalik ish yo'li uzilib qolardi.
   */
  inNav?: boolean
  /**
   * FAQAT superadmin ko'radi (oddiy `admin` ham, ruxsat berilgan xodim ham EMAS).
   *
   * ⚠️ `perm` bilan buni ifodalab BO'LMAYDI: `can()` da `permissions == null` — ya'ni
   * admin/superadmin — har doim `true` qaytadi, demak ruxsat kaliti adminni CHETLATA olmaydi.
   * Shuning uchun alohida bayroq va HARD rol tekshiruvi
   * (`.claude/rules/year-freeze.md` §3 dagi bilan bir xil sabab).
   *
   * ⚠️ Bunday hisobot yon menyuga QO'SHILMAYDI (`inNav` bilan birga ishlatilmaydi) — Sidebar
   * rolni bilmaydi. Buni `reports.test.ts` qulflaydi.
   */
  superadminOnly?: boolean
}

export interface ReportGroup {
  key: string
  label: string
  icon: LucideIcon
  items: ReportLink[]
}

export const reportGroups: ReportGroup[] = [
  {
    key: 'leads',
    label: 'Lidlar va sotuv',
    icon: UserPlus,
    items: [
      {
        label: 'CRM statistika',
        to: '/admin/crm-stats',
        perm: 'leads.stats',
        description: 'Lid voronkasi, manbalar kesimi va menejerlar samaradorligi',
        inNav: true,
      },
      {
        label: 'Lid formalari statistikasi',
        to: '/admin/forms/statistika',
        perm: 'leads.forms',
        description: "Forma ko'rildi → to'ldirildi → lid bo'ldi konversiyasi (AI tahlil bilan)",
        inNav: true,
      },
      {
        label: 'Daraja testi statistikasi',
        to: '/admin/level-tests/stats',
        perm: 'schedule.levelTests',
        description: 'Daraja testlarini topshirish, natijalar taqsimoti va konversiya',
        inNav: true,
      },
    ],
  },
  {
    key: 'students',
    label: "O'quvchilar",
    icon: Users,
    items: [
      {
        label: "Bog'lanish hisoboti",
        to: '/admin/students/boglanish?tab=hisobot',
        perm: 'contacts',
        description: "Qo'ng'iroqlar, natijalar, sabablar va xodimlar kesimi + kunlik jurnal",
        // Navbatning O'ZI `contacts` ruxsatiga ega hammaga ochiq; HISOBOT esa faqat
        // superadminga (serverda ham shunday — `ContactsController.MaySeeReports`).
        superadminOnly: true,
      },
      {
        label: "O'quvchilar davomati",
        to: '/admin/students/davomat',
        perm: 'students.attendance',
        description: 'Kim qancha dars qoldirdi — davr va guruh kesimida',
      },
      {
        label: 'Ushlab turish bonusi',
        to: '/admin/students/bonus',
        perm: 'finance.bonus',
        description: "Bonusga chiqqan o'quvchilar, berilgan va bekor qilingan bonuslar",
      },
    ],
  },
  {
    key: 'teachers',
    label: "O'qituvchilar",
    icon: GraduationCap,
    items: [
      {
        label: "O'qituvchilar hisoboti",
        to: '/admin/teacher-reports',
        perm: 'teacherReports',
        description: "Oylik ko'rsatkichlar: guruhlar, o'quvchilar, davomat va ushlab qolish",
        inNav: true,
      },
      {
        label: "O'qituvchilar davomati",
        to: '/admin/teachers/attendance',
        perm: 'teachers.attendance',
        description: "O'qituvchilarning oylik davomat jadvali",
      },
    ],
  },
  {
    key: 'academic',
    label: "O'quv bo'limi",
    icon: BookOpen,
    items: [
      {
        label: 'Kurslar analitikasi',
        to: '/admin/subjects/analitika',
        perm: 'schedule.analytics',
        description: "Kurs kesimida keldi/ketdi oqimi, faol o'quvchilar va kurslar kesishuvi",
        inNav: true,
      },
      {
        label: 'Xonalar samaradorligi',
        to: '/admin/rooms/utilization',
        perm: 'classes.rooms',
        description: 'Xonalar bandligi — qaysi xona qachon bo\'sh turadi',
        inNav: true,
      },
      {
        label: 'Testlar natijalari',
        to: '/admin/test-results',
        perm: 'classes.testResults',
        description: "Guruhlar bo'yicha test natijalari va sertifikatlar",
      },
      {
        label: 'Kitoblar sotuvi analitikasi',
        to: '/admin/books?tab=analytics',
        perm: 'books',
        description: 'Davr kesimida sotilgan kitoblar, tushum va qoldiq',
      },
      {
        label: 'Kitob nasiyalari',
        to: '/admin/books?tab=credits',
        perm: 'books',
        description: "To'langan va muddati o'tgan kitob nasiyalari",
      },
    ],
  },
  {
    key: 'finance',
    label: 'Moliya',
    icon: Wallet,
    items: [
      {
        label: 'Moliya — umumiy',
        to: '/admin/finance?tab=overview',
        perm: 'finance.main',
        description: 'Kirim/chiqim xulosasi va kunlik hisobot kalendari',
      },
      {
        label: "Guruhlar bo'yicha to'lov",
        to: '/admin/finance?tab=groups',
        perm: 'finance.main',
        description: "Har guruh/kurs bo'yicha hisoblangan va yig'ilgan summa",
      },
      {
        label: "O'qituvchilar maoshi",
        to: '/admin/finance?tab=teachers',
        perm: 'finance.main',
        description: "Oylik maosh hisobi — o'qituvchi va guruh kesimida",
      },
      {
        label: "To'lovlar ro'yxati",
        to: '/admin/finance?tab=payments',
        perm: 'finance.main',
        description: "Barcha to'lovlar: filtr, saralash va kvitansiya",
      },
      {
        label: 'Vozvratlar',
        to: '/admin/finance?tab=refunds',
        perm: 'finance.main',
        description: 'Qaytarilgan summalar va ularning sabablari',
      },
      {
        label: 'Kassirlar kesimi',
        to: '/admin/finance?tab=cashiers',
        perm: 'finance.main',
        description: 'Qaysi kassir qancha qabul qildi — har biri bo\'yicha batafsil',
      },
      {
        label: "Bonus hisoboti (o'qituvchilar)",
        to: '/admin/finance?tab=bonuses',
        perm: 'finance.main',
        description: "O'qituvchilarga hisoblangan ushlab turish bonuslari",
      },
      {
        // SOF hisobot: sahifada hech qanday yozuvchi amal yo'q (chegirma BERISH avvalgidek
        // o'quvchi profilida), shuning uchun yon menyudagi "Hisobotlar" guruhiga ham tushadi.
        label: 'Chegirmalar hisoboti',
        to: '/admin/hisobotlar/chegirmalar',
        perm: 'finance.main',
        description: "Kimga qancha chegirma berilgan — o'quvchi, guruh va o'qituvchi kesimida",
        inNav: true,
      },
    ],
  },
  {
    key: 'marketing',
    label: 'Marketing',
    icon: Megaphone,
    items: [
      {
        label: 'Marketing analitikasi',
        to: '/admin/marketing/analytics',
        perm: 'marketing.analytics',
        description: 'Instagram: yozishmalar, javob tezligi va lidga aylanish',
        inNav: true,
      },
      {
        label: 'Javob sifati',
        to: '/admin/marketing/javob-sifati',
        perm: 'marketing.quality',
        description: 'AI javoblarining sifati va qo\'lda aralashuv ulushi',
        inNav: true,
      },
      {
        label: 'Reklama statistikasi',
        to: '/admin/marketing/reklama-statistikasi',
        perm: 'marketing.adsstats',
        description: 'Kampaniyalar, platformalar va sarf-natija ko\'rsatkichlari',
      },
    ],
  },
  {
    key: 'system',
    label: 'Tizim',
    icon: History,
    items: [
      {
        label: "O'zgarishlar tarixi",
        to: '/admin/settings/history',
        perm: 'audit',
        description: "Kim, qachon, nimani o'zgartirdi — bo'limlar bo'yicha audit jurnali",
        inNav: true,
      },
    ],
  },
]

/** Katalogdagi barcha hisobotlar (guruhlarsiz tekis ro'yxat). */
export const allReports: ReportLink[] = reportGroups.flatMap((g) => g.items)

/**
 * "Hisobotlar" bo'limi menyuda ko'rinishi uchun `permAny` — birorta hisoboti bo'lmagan
 * xodimga bo'lim umuman ko'rinmaydi (bosib bo'sh sahifaga tushib qolmasin).
 *
 * ⚠️ `superadminOnly` hisobotlarning kaliti bu yerga KIRMAYDI. Aks holda faqat `contacts`
 * ruxsati bor operator menyuda "Hisobotlar"ni ko'rib, ichidan hech narsa topmasdi — hub uni
 * baribir rol bo'yicha filtrlaydi. Superadmin uchun bu hech narsani o'zgartirmaydi: unda
 * `can()` har qanday kalitga `true` qaytaradi.
 *
 * Kalit HAM oddiy, HAM superadmin-only hisobotda ishlatilsa — u qoladi (oddiysi saqlab qoladi).
 */
export const reportPerms: string[] = [
  ...new Set(allReports.filter((r) => !r.superadminOnly).map((r) => r.perm)),
]

/** Yon menyudagi "Hisobotlar" guruhiga tushadigan (eski bo'limidan KO'CHIRILGAN) hisobotlar. */
export const navReports: ReportLink[] = allReports.filter((r) => r.inNav)

/**
 * Ruxsati bor hisobotlar — bo'sh qolgan guruh umuman qaytarilmaydi
 * (Sidebar'dagi `filterNav` qoidasi bilan bir xil).
 */
export function visibleReportGroups(
  canSee: (perm: string) => boolean,
  /** Joriy foydalanuvchi superadminmi — `superadminOnly` hisobotlar shu bilan filtrlanadi. */
  isSuperAdmin: boolean,
): ReportGroup[] {
  return reportGroups
    .map((g) => ({
      ...g,
      items: g.items.filter((i) => (!i.superadminOnly || isSuperAdmin) && canSee(i.perm)),
    }))
    .filter((g) => g.items.length > 0)
}
