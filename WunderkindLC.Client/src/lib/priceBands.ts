import type { SubjectPriceBand } from '@/types'
import { SCHOOL_GRADE_GRADUATED } from '@/lib/schoolGrades'

/**
 * KURSNING SINF ORALIQLARI va narxlari — sof yordamchilar (`lib/__tests__/priceBands.test.ts`).
 *
 * ⚠️ Narx QOIDASI serverda (`TuitionPricing`): o'quvchining oyligi uning JORIY sinfi tushgan
 * oraliqdan, topilmasa guruh (asosiy) narxidan. Bu yerda faqat yorliq, forma tekshiruvi (server
 * bilan bir xil matnlar — darhol ko'rinsin) va o'zgarganini aniqlash. Narx bu yerda HISOBLANMAYDI.
 */

export const MAX_PRICE_BANDS = 12

/** "3–7-sinf" / "5-sinf" / "Maktabni bitirgan" — serverdagi `TuitionPricing.Label` bilan bir xil. */
export function bandLabel(b: Pick<SubjectPriceBand, 'gradeFrom' | 'gradeTo'>): string {
  const g = (n: number) => (n === SCHOOL_GRADE_GRADUATED ? 'bitirgan' : String(n))
  if (b.gradeFrom === b.gradeTo)
    return b.gradeFrom === SCHOOL_GRADE_GRADUATED ? 'Maktabni bitirgan' : `${b.gradeFrom}-sinf`
  return `${g(b.gradeFrom)}–${g(b.gradeTo)}-sinf`
}

/** Formadagi oraliq: narxlar hali kiritilmagan bo'lishi mumkin (`null`). */
export interface BandDraft {
  gradeFrom: number
  gradeTo: number
  price: number | null
  lessonPrice: number | null
}

/** Xato matni yoki `null`. Server (`TuitionPricing.Validate`) baribir qayta tekshiradi. */
export function validateBands(bands: BandDraft[]): string | null {
  if (bands.length > MAX_PRICE_BANDS) return `Ko'pi bilan ${MAX_PRICE_BANDS} ta oraliq`
  for (const b of bands) {
    if (b.gradeFrom > b.gradeTo) return `Oraliq noto'g'ri: ${b.gradeFrom}–${b.gradeTo}`
    if (!b.price || b.price <= 0) return `${bandLabel(b)} uchun oylik narxni kiriting`
  }
  const sorted = [...bands].sort((a, b) => a.gradeFrom - b.gradeFrom)
  for (let i = 1; i < sorted.length; i++)
    if (sorted[i].gradeFrom <= sorted[i - 1].gradeTo)
      return `Oraliqlar ustma-ust tushmoqda: ${bandLabel(sorted[i - 1])} va ${bandLabel(sorted[i])}`
  return null
}

/** Formadan serverga: sinf bo'yicha tartiblangan, bo'sh bir dars narxi = 0 (pro-rata). */
export function toPayloadBands(bands: BandDraft[]): SubjectPriceBand[] {
  return [...bands]
    .sort((a, b) => a.gradeFrom - b.gradeFrom)
    .map((b) => ({ gradeFrom: b.gradeFrom, gradeTo: b.gradeTo, price: b.price ?? 0, lessonPrice: b.lessonPrice ?? 0 }))
}

/** Ikki ro'yxat bir xilmi (tartibdan qat'i nazar) — "joriy oydan qo'llash" so'rovi uchun. */
export function sameBands(a: SubjectPriceBand[] | undefined, b: SubjectPriceBand[] | undefined): boolean {
  const key = (l: SubjectPriceBand[] | undefined) =>
    JSON.stringify(
      [...(l ?? [])]
        .sort((x, y) => x.gradeFrom - y.gradeFrom)
        .map((x) => [x.gradeFrom, x.gradeTo, x.price, x.lessonPrice ?? 0]),
    )
  return key(a) === key(b)
}

/** Yangi oraliq uchun taklif: oxirgi oraliqdan keyingi sinf (yoki 1-sinf). */
export function nextBandStart(bands: BandDraft[]): number {
  const last = bands.reduce((m, b) => Math.max(m, b.gradeTo), 0)
  return Math.min(last + 1, SCHOOL_GRADE_GRADUATED)
}
