import { describe, expect, it } from 'vitest'
import { canStudentCreds, maskPassword, passwordSourceLabel } from '@/lib/studentCreds'

describe("canStudentCreds — serverdagi HasFullAccess(\"students.list\") nusxasi", () => {
  it('admin va superadmin har doim', () => {
    expect(canStudentCreds('superadmin', null)).toBe(true)
    expect(canStudentCreds('admin', [])).toBe(true)
  })
  it("xodim — faqat YALANG bo'lim yoki sahifa kaliti", () => {
    expect(canStudentCreds('staff', ['students'])).toBe(true)
    expect(canStudentCreds('staff', ['students.list'])).toBe(true)
    expect(canStudentCreds('staff', ['students.list:edit'])).toBe(false)
    expect(canStudentCreds('staff', ['students:create'])).toBe(false)
    expect(canStudentCreds('staff', ['students.turnstile'])).toBe(false)
    expect(canStudentCreds('staff', null)).toBe(false)
  })
  it("o'qituvchi, o'quvchi, ota-ona — hech qachon (SPEC §10 #5)", () => {
    expect(canStudentCreds('teacher', ['students'])).toBe(false)
    expect(canStudentCreds('student', ['students'])).toBe(false)
    expect(canStudentCreds('parent', ['students'])).toBe(false)
    expect(canStudentCreds(undefined, ['students'])).toBe(false)
  })
})

describe('passwordSourceLabel / maskPassword', () => {
  it('manbalar', () => {
    expect(passwordSourceLabel('self_change')).toBe("o'quvchi o'zi almashtirgan")
    expect(passwordSourceLabel('reset')).toBe('qayta yaratilgan')
    expect(passwordSourceLabel('initial')).toBe('dastlabki parol')
    expect(passwordSourceLabel(undefined)).toBe('')
  })
  it('yashirish uzunligi', () => {
    expect(maskPassword('abc')).toBe('••••••')
    expect(maskPassword('abcdefghij')).toBe('••••••••••')
  })
})
