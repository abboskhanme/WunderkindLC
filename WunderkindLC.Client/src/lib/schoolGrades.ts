/**
 * O'QUVCHINING MAKTAB SINFI — klient yordamchilari.
 *
 * Qoidaning O'ZI serverda (`WunderkindLC.Domain/SchoolGrades.cs`): sinf har yili 1-sentabrda
 * o'zi oshadi va server JORIY qiymatni `Student.schoolGradeNow` da tayyor beradi. Klient uni
 * QAYTA HISOBLAMAYDI — faqat ko'rsatadi, filtrlaydi va sanaydi (ikki joyda hisoblansa, biri
 * o'zgarganda ikkinchisi jimgina eskirib qolardi).
 */

export const SCHOOL_GRADE_MIN = 1
export const SCHOOL_GRADE_MAX = 11
/** Maktabni bitirgan — serverdagi `SchoolGrades.Graduated` bilan bir xil. */
export const SCHOOL_GRADE_GRADUATED = 12

/** Tanlash ro'yxati: 1..11 va «Maktabni bitirgan». */
export const SCHOOL_GRADE_OPTIONS: { value: number; label: string }[] = [
  ...Array.from({ length: SCHOOL_GRADE_MAX }, (_, i) => ({ value: i + 1, label: `${i + 1}-sinf` })),
  { value: SCHOOL_GRADE_GRADUATED, label: 'Maktabni bitirgan' },
]

/** "7-sinf" / "Maktabni bitirgan"; kiritilmagan yoki noma'lum qiymat — bo'sh satr. */
export function schoolGradeLabel(grade: number | null | undefined): string {
  return SCHOOL_GRADE_OPTIONS.find((o) => o.value === grade)?.label ?? ''
}

/** Ro'yxat filtri kaliti: 'all' | 'none' (kiritilmagan) | "1".."12". */
export type SchoolGradeFilter = string

export function matchesSchoolGrade(grade: number | null | undefined, filter: SchoolGradeFilter): boolean {
  if (filter === 'all') return true
  if (filter === 'none') return grade == null
  return grade === Number(filter)
}

export interface SchoolGradeRow {
  /** Sinf (1..12) yoki `null` — kiritilmagan. */
  grade: number | null
  label: string
  count: number
}

/**
 * Sinflar kesimi: har sinf (1..11, bitirgan) va «kiritilmagan» qatori — HAR DOIM hammasi,
 * bo'sh sinf ham 0 bilan (jadvalda "7-sinf yo'q" ham ma'lumot). Tartib: 1 → 11 → bitirgan → kiritilmagan.
 */
export function schoolGradeDistribution(grades: (number | null | undefined)[]): SchoolGradeRow[] {
  const counts = new Map<number | null, number>()
  for (const g of grades) {
    const key = SCHOOL_GRADE_OPTIONS.some((o) => o.value === g) ? (g as number) : null
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  return [
    ...SCHOOL_GRADE_OPTIONS.map((o) => ({ grade: o.value, label: o.label, count: counts.get(o.value) ?? 0 })),
    { grade: null, label: 'Sinfi kiritilmagan', count: counts.get(null) ?? 0 },
  ]
}
