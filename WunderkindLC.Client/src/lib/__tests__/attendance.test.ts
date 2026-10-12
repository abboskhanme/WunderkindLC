import { describe, expect, it } from 'vitest'
import { absencePeriodRange, attendanceMark, attendanceTally } from '@/lib/attendance'
import { autoPresent } from '@/pages/admin/classes/detail/shared'

const base = {
  blocked: false,
  hasReason: false,
  reasonIsLate: false,
  explicitPresent: false,
  hasGrade: false,
  hasMastery: false,
  conducted: true,
  attendanceTaken: false,
  date: '2026-10-05',
}

describe('attendanceMark — server AttendanceRules nusxasi (SPEC P3.1)', () => {
  it('kech = KELGAN (late), sabab bahodan ustun', () => {
    expect(attendanceMark({ ...base, hasReason: true, reasonIsLate: true, hasGrade: true })).toBe('late')
  })
  it('baho / o\'zlashtirish / aniq keldi = present', () => {
    expect(attendanceMark({ ...base, hasGrade: true })).toBe('present')
    expect(attendanceMark({ ...base, hasMastery: true })).toBe('present')
    expect(attendanceMark({ ...base, explicitPresent: true })).toBe('present')
  })
  it('davomat olingan + belgisiz = present, faqat presentDefaultFrom dan', () => {
    expect(attendanceMark({ ...base, attendanceTaken: true })).toBe('present')
    expect(attendanceMark({ ...base, attendanceTaken: true, presentDefaultFrom: '2026-10-06' })).toBe('unmarked')
  })
  it("faqat o'tildi + belgisiz = unmarked; o'tilmagan = notHeld; bloklangan = blocked", () => {
    expect(attendanceMark(base)).toBe('unmarked')
    expect(attendanceMark({ ...base, conducted: false })).toBe('notHeld')
    expect(attendanceMark({ ...base, blocked: true, hasReason: true })).toBe('blocked')
  })
})

describe('attendanceTally', () => {
  it('kech kelgan foizga KELGAN, belgilanmagan kirmaydi', () => {
    const t = attendanceTally(['present', 'late', 'absent', 'unmarked', 'notHeld', 'blocked'])
    expect(t).toEqual({ held: 3, attended: 2, absent: 1, late: 1, pct: 67 })
  })
  it("dars bo'lmasa foiz yo'q", () => {
    expect(attendanceTally(['unmarked']).pct).toBeNull()
  })
})

describe('autoPresent — attendanceTaken (conducted EMAS)', () => {
  const cell = { hasGrade: false, hasReason: false, explicitPresent: false, date: '2026-10-05' }
  it("davomati olinmagan (faqat o'tildi) darsda avto-keldi YO'Q", () => {
    expect(autoPresent({ ...cell, attendanceTaken: false })).toBe(false)
  })
  it('davomat olingan darsda belgisiz = keldi, kiritilishidan oldin emas', () => {
    expect(autoPresent({ ...cell, attendanceTaken: true })).toBe(true)
    expect(autoPresent({ ...cell, attendanceTaken: true, presentDefaultFrom: '2026-10-06' })).toBe(false)
    expect(autoPresent({ ...cell, attendanceTaken: true, blocked: true })).toBe(false)
  })
  it('aniq keldi belgisi har doim yashil, sabab/baho bo\'lsa emas', () => {
    expect(autoPresent({ ...cell, attendanceTaken: false, explicitPresent: true })).toBe(true)
    expect(autoPresent({ ...cell, attendanceTaken: true, hasReason: true })).toBe(false)
    expect(autoPresent({ ...cell, attendanceTaken: true, hasGrade: true })).toBe(false)
  })
})

describe('absencePeriodRange', () => {
  const now = new Date(2026, 9, 12)
  it('shu oy — oyning 1-kunidan', () => {
    expect(absencePeriodRange('month', now)).toEqual({ from: '2026-10-01', to: '2026-10-12' })
  })
  it('3 oy / 1 yil — orqaga, server chegarasidan oshmaydi', () => {
    expect(absencePeriodRange('3m', now)).toEqual({ from: '2026-07-13', to: '2026-10-12' })
    expect(absencePeriodRange('1y', now)).toEqual({ from: '2025-10-13', to: '2026-10-12' })
  })
})
