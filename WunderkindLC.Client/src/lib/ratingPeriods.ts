/**
 * REYTING DAVRLARI — klient tomoni (tanlash ro'yxatlari, yorliqlar, o'rin o'zgarishi).
 *
 * Davrning O'ZI (chegaralar, kesish) SERVERDA hal qilinadi (`RatingPeriods.Resolve`); bu fayl faqat
 * tanlovni quradi va server bilan BIR XIL yorliq yozadi: «Oktabr 2026», «Avgust – Oktabr 2026»,
 * «2026/27 o'quv yili». Komponentlar bu yerda YO'Q (`react-refresh/only-export-components`).
 */
import type { RatingPeriodKind } from '@/api/services/rating'

export const RATING_MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** Davr turlari — «3 oy» va «chorak» egasining qarori bilan BITTA (sirpanuvchi 3 oy). */
export const RATING_PERIOD_KINDS: { kind: RatingPeriodKind; label: string }[] = [
  { kind: 'month', label: 'Oy' },
  { kind: 'quarter', label: '3 oy' },
  { kind: 'year', label: "O'quv yili" },
]

export interface RatingPeriodValue {
  kind: RatingPeriodKind
  key: string
}

const pad2 = (n: number) => String(n).padStart(2, '0')

/** Date → "yyyy-MM" (mahalliy vaqt bo'yicha). */
export function monthKeyOf(d: Date): string {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}`
}

/** O'quv yili (boshlanish yili): sentabrdan boshlab — shu yil, aks holda — o'tgan yil. */
export function academicYearOf(d: Date): number {
  return d.getMonth() + 1 >= 9 ? d.getFullYear() : d.getFullYear() - 1
}

/** "2026-10" ± n oy. */
export function shiftMonth(key: string, delta: number): string {
  const y = Number(key.slice(0, 4))
  const m = Number(key.slice(5, 7)) - 1 + delta
  const yy = y + Math.floor(m / 12)
  const mm = ((m % 12) + 12) % 12
  return `${yy}-${pad2(mm + 1)}`
}

/** Turning standart (joriy) kaliti. */
export function defaultRatingKey(kind: RatingPeriodKind, today: Date): string {
  return kind === 'year' ? String(academicYearOf(today)) : monthKeyOf(today)
}

export function defaultRatingPeriod(today: Date, kind: RatingPeriodKind = 'month'): RatingPeriodValue {
  return { kind, key: defaultRatingKey(kind, today) }
}

/** "2026-10" → "Oktabr 2026" */
export function monthLabel(key: string): string {
  const m = Number(key.slice(5, 7))
  return m >= 1 && m <= 12 ? `${RATING_MONTHS[m - 1]} ${key.slice(0, 4)}` : key
}

/** "2026-10" → "Okt" (grafik o'qi uchun qisqa). */
export function shortMonth(key: string): string {
  const m = Number(key.slice(5, 7))
  return m >= 1 && m <= 12 ? RATING_MONTHS[m - 1].slice(0, 3) : key
}

/** 3 oy, oxirgi oy kaliti bo'yicha: «Avgust – Oktabr 2026» / «Noyabr 2025 – Yanvar 2026». */
export function quarterLabel(endKey: string): string {
  const fromKey = shiftMonth(endKey, -2)
  return fromKey.slice(0, 4) === endKey.slice(0, 4)
    ? `${RATING_MONTHS[Number(fromKey.slice(5, 7)) - 1]} – ${monthLabel(endKey)}`
    : `${monthLabel(fromKey)} – ${monthLabel(endKey)}`
}

/** 2026 → «2026/27 o'quv yili» */
export function yearLabel(start: number): string {
  return `${start}/${pad2((start + 1) % 100)} o'quv yili`
}

export function ratingPeriodLabel(p: RatingPeriodValue): string {
  if (p.kind === 'year') return yearLabel(Number(p.key))
  return p.kind === 'quarter' ? quarterLabel(p.key) : monthLabel(p.key)
}

/**
 * Tanlash ro'yxati (eng yangisi birinchi): oy va 3 oyda — oxirgi `months` oy, o'quv yilida — oxirgi
 * `years` yil. Kelajakdagi davr ro'yxatga TUSHMAYDI (server uni baribir 400 bilan rad etadi).
 */
export function ratingKeyOptions(
  kind: RatingPeriodKind,
  today: Date,
  months = 24,
  years = 6,
): { key: string; label: string }[] {
  if (kind === 'year') {
    const cur = academicYearOf(today)
    return Array.from({ length: years }, (_, i) => {
      const y = cur - i
      return { key: String(y), label: yearLabel(y) }
    })
  }
  const cur = monthKeyOf(today)
  return Array.from({ length: months }, (_, i) => {
    const key = shiftMonth(cur, -i)
    return { key, label: kind === 'quarter' ? quarterLabel(key) : monthLabel(key) }
  })
}

/**
 * O'rin o'zgarishi oldingi davrga nisbatan. O'rin RAQAMI kichraysa — yuqoriga ko'tarilgan (▲).
 * Oldingi yoki joriy o'rin bo'lmasa — `null` (strelka chizilmaydi).
 */
export function rankDelta(rank: number | null, prevRank: number | null): { dir: 'up' | 'down' | 'same'; n: number } | null {
  if (rank == null || prevRank == null) return null
  if (rank === prevRank) return { dir: 'same', n: 0 }
  return rank < prevRank ? { dir: 'up', n: prevRank - rank } : { dir: 'down', n: rank - prevRank }
}

/** Ball/foiz: bir kasr, ".0" tashlanadi; yo'q bo'lsa «—». */
export function fmtScore(n: number | null | undefined): string {
  if (n == null || Number.isNaN(n)) return '—'
  const s = (Math.round(n * 10) / 10).toFixed(1)
  return s.endsWith('.0') ? s.slice(0, -2) : s
}

/** Test natijasi: "25/30" (kasrli eski ballar ham to'g'ri chiqadi). */
export function fmtSolved(solved: number, questions: number): string {
  return `${fmtScore(solved)}/${fmtScore(questions)}`
}

/** Ulushlar (server `RatingFormula.Weights` bilan bir xil): uy vazifasi testlar bilan birga 100 dan oshmaydi. */
export function ratingWeights(testWeight: number, homeworkWeight = 0): { test: number; homework: number; ball: number } {
  const test = Math.max(0, Math.min(100, testWeight))
  const homework = Math.max(0, Math.min(100 - test, homeworkWeight))
  return { test, homework, ball: 100 - test - homework }
}

/** Sarlavhadagi qisqa yozuv: «testlar 60% + ball 40%» (uy vazifasi ulushi bo'lsa — u ham). */
export function weightsLabel(testWeight: number, homeworkWeight = 0): string {
  const w = ratingWeights(testWeight, homeworkWeight)
  return w.homework > 0
    ? `testlar ${w.test}% + ball ${w.ball}% + uy vazifasi ${w.homework}%`
    : `testlar ${w.test}% + ball ${w.ball}%`
}

/**
 * Reyting bali izohi: «Testlar 88% × 60% + Ball 79 × 40%». Komponentlardan biri yo'q bo'lsa —
 * faqat borini (server ham shunday hisoblaydi: faqat test → test, faqat ball → ball). Uy vazifasi ulushi
 * bo'lsa (SPEC-backlog A3) — «+ Uy vazifasi 75% × 20%»; yo'q komponentning ulushi qolganlariga o'tadi.
 */
export function scoreFormulaText(
  testAvg: number | null, ballNorm: number | null, testWeight: number,
  homeworkAvg: number | null = null, homeworkWeight = 0,
): string {
  const w = ratingWeights(testWeight, homeworkWeight)
  const hw = w.homework > 0 && homeworkAvg != null ? ` + Uy vazifasi ${fmtScore(homeworkAvg)}% × ${w.homework}%` : ''
  if (testAvg != null && ballNorm != null)
    return `Testlar ${fmtScore(testAvg)}% × ${w.test}% + Ball ${fmtScore(ballNorm)} × ${w.ball}%${hw}`
  if (testAvg != null) return hw ? `Testlar ${fmtScore(testAvg)}%${hw}` : `Faqat testlar: ${fmtScore(testAvg)}%`
  if (ballNorm != null) return hw ? `Ball ${fmtScore(ballNorm)}${hw}` : `Faqat ball: ${fmtScore(ballNorm)}`
  if (hw) return `Faqat uy vazifasi: ${fmtScore(homeworkAvg ?? 0)}%`
  return "Bu davrda test ham, ball ham yo'q"
}

/** Test turi yorlig'i (U4a dan keyin keladi): monthly → «Oylik», interim → «Oraliq». */
export function testKindLabel(kind: string): string {
  return kind === 'monthly' ? 'Oylik' : kind === 'interim' ? 'Oraliq' : ''
}
