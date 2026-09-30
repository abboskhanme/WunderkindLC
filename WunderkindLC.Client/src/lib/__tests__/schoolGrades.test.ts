import { describe, expect, it } from 'vitest'
import {
  matchesSchoolGrade,
  schoolGradeDistribution,
  schoolGradeLabel,
  SCHOOL_GRADE_GRADUATED,
  SCHOOL_GRADE_OPTIONS,
} from '../schoolGrades'

describe('maktab sinfi', () => {
  it("tanlov: 1..11 va «bitirgan» (serverdagi SchoolGrades bilan bir xil qiymatlar)", () => {
    expect(SCHOOL_GRADE_OPTIONS.map((o) => o.value)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
    expect(SCHOOL_GRADE_GRADUATED).toBe(12)
  })

  it('yorliq', () => {
    expect(schoolGradeLabel(7)).toBe('7-sinf')
    expect(schoolGradeLabel(12)).toBe('Maktabni bitirgan')
    expect(schoolGradeLabel(null)).toBe('')
    expect(schoolGradeLabel(undefined)).toBe('')
    expect(schoolGradeLabel(0)).toBe('')
  })

  it("filtr: 'all', 'none' (kiritilmagan) va aniq sinf", () => {
    expect(matchesSchoolGrade(5, 'all')).toBe(true)
    expect(matchesSchoolGrade(null, 'all')).toBe(true)
    expect(matchesSchoolGrade(null, 'none')).toBe(true)
    expect(matchesSchoolGrade(undefined, 'none')).toBe(true)
    expect(matchesSchoolGrade(5, 'none')).toBe(false)
    expect(matchesSchoolGrade(5, '5')).toBe(true)
    expect(matchesSchoolGrade(5, '6')).toBe(false)
    expect(matchesSchoolGrade(null, '5')).toBe(false)
  })

  it("taqsimot: HAR sinf qatori (0 bilan ham) + «kiritilmagan», noma'lum qiymat kiritilmaganga tushadi", () => {
    const rows = schoolGradeDistribution([7, 7, 12, null, undefined, 99])
    expect(rows).toHaveLength(13)
    expect(rows.find((r) => r.grade === 7)?.count).toBe(2)
    expect(rows.find((r) => r.grade === 1)?.count).toBe(0)
    expect(rows.find((r) => r.grade === 12)?.count).toBe(1)
    const none = rows[rows.length - 1]
    expect(none.grade).toBeNull()
    expect(none.count).toBe(3)
    // Jami yo'qolmaydi — har o'quvchi aynan bitta qatorda.
    expect(rows.reduce((s, r) => s + r.count, 0)).toBe(6)
  })
})
