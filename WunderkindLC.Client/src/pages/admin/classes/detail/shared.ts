import type { GroupJournal } from '@/api/services/journal'
import type { GradingBoard } from '@/api/services/grading'

/** Guruh sahifasi bo'laklari uchun umumiy (komponent bo'lmagan) yordamchilar. */

export const weekdayShort = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']
export const uzMonths = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
export const monthLabel = (m: string) =>
  m && m.length >= 7 ? `${uzMonths[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m

/** Reyting tabidagi bitta oy uchun jurnal+baholash ma'lumoti. */
export interface RatingMonthData {
  month: string
  journal: GroupJournal | null
  grading: GradingBoard | null
}

/**
 * Katak AVTOMATIK "keldi" (yashil ✓) hisoblanadimi — YAGONA ta'rif (`AttendanceRules`, SPEC P3.1): baho yo'q +
 * sabab yo'q + (ANIQ "keldi" belgisi BOR yoki dars davomati TO'LIQ OLINGAN (`attendanceTaken`) va sana o'quvchi
 * tizimga kiritilganidan — `presentDefaultFrom` — keyin). `blocked` — muzlatilgandan keyingi / a'zolikdan oldingi
 * dars: u yerda faqat ANIQ belgi hisobga olinadi.
 *
 * ⚠️ Ilgari `conducted` ("o'tildi") ga qarardi: bitta o'quvchiga baho qo'yilgan darsda qolgan HAMMA o'quvchi
 * "keldi" bo'lib ko'rinardi. Endi davomati olinmagan dars — BELGILANMAGAN (bo'sh, foizga kirmaydi).
 *
 * ⚠️ YAGONA joy: "Jurnal" va "Davomat" tablari ikkalasi ham shuni chaqiradi — qoida ayrilib ketmasin.
 */
export function autoPresent(a: {
  hasGrade: boolean
  hasReason: boolean
  attendanceTaken: boolean
  explicitPresent: boolean
  date: string
  presentDefaultFrom?: string
  blocked?: boolean
}): boolean {
  return (
    !a.hasGrade &&
    !a.hasReason &&
    (a.explicitPresent ||
      (a.attendanceTaken && (!a.presentDefaultFrom || a.date >= a.presentDefaultFrom) && !a.blocked))
  )
}
