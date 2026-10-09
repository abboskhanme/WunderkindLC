import type { Lead, LeadEventType, TrialLesson } from '@/types'

/**
 * Lid sahifasi va jadvallari uchun YORLIQLAR va kichik sof yordamchilar. Komponent fayllaridan
 * ATAYIN ajratilgan: bitta fayl ham komponent, ham konstanta eksport qilsa Vite fast-refresh
 * qoidasi buziladi (`react-refresh/only-export-components`).
 */

export const eventTypeLabels: Record<LeadEventType, string> = {
  note: 'Izoh',
  stage: 'Bosqich',
  call: "Qo'ng'iroq",
  trial: 'Sinov darsi',
  convert: 'Aylantirildi',
  created: 'Yaratildi',
  lost: "Yo'qotildi",
  restore: 'Tiklandi',
}

export const eventTypeColors: Record<LeadEventType, string> = {
  note: 'bg-slate-100 text-slate-600',
  stage: 'bg-blue-50 text-blue-600',
  call: 'bg-amber-50 text-amber-600',
  trial: 'bg-violet-50 text-violet-600',
  convert: 'bg-emerald-50 text-emerald-600',
  created: 'bg-slate-100 text-slate-500',
  lost: 'bg-rose-50 text-rose-700',
  restore: 'bg-sky-50 text-sky-700',
}

/**
 * ⚠️ IKKI BOSHQA savol: «Keldi/Kelmadi» — lid sinovga TASHRIF BUYURDIMI; «Qoldi/Ketdi» —
 * tashrifdan keyin markazda QOLDIMI. Ilgari faqat ikkinchisi bor edi, ya'ni "keldi, lekin
 * ketdi" bilan "umuman kelmadi" bir xil ko'rinardi — holbuki kiruvchi adminning asosiy
 * ko'rsatkichi aynan "lid → sinovga KELISH" konversiyasi.
 */
export const trialResultLabels: Record<TrialLesson['result'], string> = {
  pending: 'Kutilmoqda',
  came: 'Keldi',
  no_show: 'Kelmadi',
  stayed: 'Qoldi',
  left: 'Ketdi',
}

export const trialResultColors: Record<TrialLesson['result'], string> = {
  pending: 'bg-amber-50 text-amber-600',
  came: 'bg-sky-50 text-sky-600',
  no_show: 'bg-slate-100 text-slate-500',
  stayed: 'bg-emerald-50 text-emerald-600',
  left: 'bg-rose-50 text-rose-600',
}

/** Hafta kunlari qisqartmasi — 0=Dushanba .. 6=Yakshanba (backend Group.days bilan mos). */
export const WD_SHORT = ['Du', 'Se', 'Chor', 'Pay', 'Jum', 'Shan', 'Yak']

/** Hafta kunlari to'liq — "Kun" filtri uchun (0=Dushanba). */
export const WD_FULL = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']

/** JS getDay() (0=Yakshanba) → bizning indeks (0=Dushanba). */
export const toMonFirst = (jsDay: number) => (jsDay + 6) % 7

/** ISO sana "YYYY-MM-DD" (mahalliy, TZ siljishisiz). */
export const isoDate = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

/** Bugundan boshlab guruh kunlariga (days, 0=Du..6=Yak) mos keyingi `count` dars sanasi. */
export function nextLessonDates(days: number[], count: number): Date[] {
  const out: Date[] = []
  const base = new Date()
  base.setHours(0, 0, 0, 0)
  for (let i = 0; i < 90 && out.length < count; i++) {
    const d = new Date(base)
    d.setDate(base.getDate() + i)
    if (days.includes(toMonFirst(d.getDay()))) out.push(d)
  }
  return out
}

/** edutizim jadvallaridagi sana-vaqt: "dd.mm.yyyy | HH:mm" (vaqt bo'lmasa — faqat sana). */
export function tableDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}:\d{2}))?/.exec(iso)
  if (!m) return iso
  return m[4] ? `${m[3]}.${m[2]}.${m[1]} | ${m[4]}` : `${m[3]}.${m[2]}.${m[1]}`
}

/**
 * Jadvaldagi ID ustuni. Bizda lid id'si GUID (edutizimdagidek ketma-ket raqam YO'Q) — ekranda
 * uning qisqa boshi ko'rsatiladi, qidiruv ham shu bo'yicha ishlaydi.
 */
export const shortLeadId = (id: string) => id.replace(/-/g, '').slice(0, 6).toUpperCase()

/** Lidning qo'ng'iroq qilinadigan raqamlari (bo'shlari tashlanadi). */
export function leadCallNumbers(l: { phone?: string; fatherPhone?: string; motherPhone?: string }) {
  return [
    { label: "O'z raqami", number: l.phone ?? '' },
    { label: 'Otasi', number: l.fatherPhone ?? '' },
    { label: 'Onasi', number: l.motherPhone ?? '' },
  ].filter((n) => n.number)
}

/**
 * Lid YO'QOTILGANmi (serverdagi `LeadLoss.IsLost` bilan bitta ta'rif). Bu BOSQICH EMAS — lidning
 * `stage` i o'z joyida qoladi, kanbanda esa u faqat DOIMIY "Yo'qotilgan" ustunida chiziladi.
 */
export const isLeadLost = (l: { lostAt?: string | null }) => !!l.lostAt

/**
 * Yo'qotilgandan KEYIN yana murojaat qilgan (forma/daraja testi orqali). Takroriy murojaat
 * holatni ATAYIN o'zgartirmaydi (birinchi teginish qoidasi — `lead-forms.md` §4), shuning uchun
 * bunday karta "Yo'qotilgan" ustunida alohida belgi bilan ko'zga tashlanadi — aks holda qaytib
 * kelgan mijoz e'tibordan chetda qolardi.
 */
export const cameBackAfterLoss = (l: { lostAt?: string | null; lastRepeatAt?: string | null }) =>
  !!l.lostAt && !!l.lastRepeatAt && l.lastRepeatAt > l.lostAt

/**
 * «Buyurtmalar ro'yxati» STATUS filtri (2026-10-09). `lead` — haqiqiy BUYURTMA: o'quvchiga aylantirilmagan VA
 * «Birinchi darsga yozilganlar»da EMAS (`firstLessonAt` — serverdagi `LeadFirstLesson.Pick`, ikkinchi ta'rif yo'q;
 * bosh sahifadagi «Buyurtmalar» sanog'i — `DashboardSummary.OpenLeads` — AYNAN shu). `first` — birinchi darsda,
 * `student` — aylantirilgan, `all` (yoki noma'lum qiymat) — hammasi.
 * ⚠️ Ilgari sinovga yozilgan lid ikkala bo'limda ham turardi ("oldingi bosqichdan o'chmayapti").
 */
export function leadMatchesStatus(
  l: Pick<Lead, 'convertedStudentId' | 'firstLessonAt'>,
  status: string,
): boolean {
  if (status === 'lead') return !l.convertedStudentId && !l.firstLessonAt
  if (status === 'first') return !!l.firstLessonAt
  if (status === 'student') return !!l.convertedStudentId
  return true
}
