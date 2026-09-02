import type { CardTabItem } from '@/components/ui/CardTabs'

/**
 * Bir bo'limning ichki sahifalari — `CardTabs` uchun yagona manba.
 *
 * Menyuda bo'lim BITTA band bo'lib turadi, sahifalar orasida esa shu cardlar orqali o'tiladi
 * (marshrutlar o'zgarmaydi — eski havolalar ishlayveradi).
 */

/**
 * O'qituvchilar bo'limi: Ro'yxati · Davomati · O'rinbosarlar · Hisoboti.
 *
 * ⚠️ HAR BIR card — alohida beriladigan SAHIFA (`teachers.list`, `teachers.attendance`,
 * `teachers.substitutions`, `teacherReports`), shuning uchun ko'rinishi ham har biri uchun
 * alohida tekshiriladi: ruxsati yo'q sahifaning cardi chiqmaydi (bosib "ruxsat yo'q" ga
 * tushib qolmasin). Sidebar'dagi qoida bilan bir xil.
 */
export function teacherTabs(canSee: (perm: string) => boolean): CardTabItem[] {
  return [
    // `end` — `/admin/teachers/attendance` ochilganda «Ro'yxati» ham faol bo'lib qolmasin
    { label: "Ro'yxati", to: '/admin/teachers', end: true, hidden: !canSee('teachers.list') },
    { label: 'Davomati', to: '/admin/teachers/attendance', hidden: !canSee('teachers.attendance') },
    {
      label: "O'rinbosarlar",
      to: '/admin/teachers/substitutions',
      hidden: !canSee('teachers.substitutions'),
    },
    { label: 'Hisoboti', to: '/admin/teacher-reports', hidden: !canSee('teacherReports') },
  ]
}

/**
 * FORMALAR bo'limi (O'quv bo'limi ichida):
 * **Lid formalari · Lid kiritish formasi · Lid statistikasi · Daraja testlari · Test statistikasi**.
 *
 * Ikki turdagi forma bitta bo'limda turadi, lekin RUXSATLARI har xil: lid formalari — `leads`
 * (ular lid ishlab chiqaradi), daraja testi esa `schedule.levelTests` (kurs bilan bog'liq). Shu sabab
 * cardlar alohida-alohida yashiriladi — bir turi yopiq bo'lgan xodim ikkinchisini baribir ko'radi.
 *
 * ⚠️ Har turdan KEYIN uning statistikasi turadi — nomlari ham ATAYIN aniq ("Statistika" emas):
 * bo'limda ikkita har xil statistika bor va yalang nom qaysi turga tegishli ekani noaniq bo'lardi.
 * Statistika cardi ro'yxat sahifasi bilan bir xil ruxsatda (ichkarida ham `RequirePerm` bor).
 */
export function formTabs(canForms: boolean, canTests: boolean): CardTabItem[] {
  return [
    // `end` — `/admin/forms/statistika` ochilganda «Lid formalari» ham faol bo'lib qolmasin
    { label: 'Lid formalari', to: '/admin/forms', end: true, hidden: !canForms },
    // Lid TURI emas, SOZLAMA: «Yangi lid» oynasida qaysi maydon so'ralishi. Shu sabab
    // formalardan KEYIN, statistikadan OLDIN turadi (tur → sozlama → statistika).
    { label: 'Lid kiritish formasi', to: '/admin/forms/lid-kiritish', hidden: !canForms },
    { label: 'Lid statistikasi', to: '/admin/forms/statistika', hidden: !canForms },
    { label: 'Daraja testlari', to: '/admin/level-tests', end: true, hidden: !canTests },
    { label: 'Test statistikasi', to: '/admin/level-tests/stats', hidden: !canTests },
  ]
}

/** Xonalar (O'quv bo'limi ichida): Xonalar ro'yxati · Samaradorlik. */
export const roomTabs: CardTabItem[] = [
  { label: "Xonalar ro'yxati", to: '/admin/rooms', end: true },
  { label: 'Samaradorlik', to: '/admin/rooms/utilization' },
]
