import { describe, expect, it } from 'vitest'
import type { TeacherLesson } from '@/api/services/teacher'
import {
  TEACHER_NAV,
  TEACHER_TABS,
  addDays,
  canTakeAttendance,
  daysLabel,
  lessonPhase,
  navMatches,
  navVisible,
  relativeDay,
  weekRangeLabel,
  weekStartOf,
  weekdayIndex,
} from '../teacherPortal'

/**
 * O'qituvchi portali — sof funksiyalar: sana (hafta kuni server bilan bir xil: 0=Du … 6=Ya),
 * dars holati (bosh sahifa / jadval / davomatda bir xil rang) va menyu katalogi.
 */

const lesson = (p: Partial<TeacherLesson> = {}): TeacherLesson => ({
  groupId: 'g1', groupName: 'A1', courseName: 'Ingliz tili', date: '2026-09-27',
  startTime: '14:00', endTime: '15:30', room: '101', studentCount: 10,
  isSubstitute: false, substitutedBy: '', moved: false, conducted: false, attendanceTaken: false, ...p,
})

describe('sana yordamchilari', () => {
  it('hafta kuni: dushanba 0, yakshanba 6 (guruh `days` bilan bir xil)', () => {
    expect(weekdayIndex('2026-09-21')).toBe(0)
    expect(weekdayIndex('2026-09-27')).toBe(6)
  })

  it('haftaning dushanbasi va oy chegarasidan o\'tish', () => {
    expect(weekStartOf('2026-09-27')).toBe('2026-09-21')
    expect(weekStartOf('2026-10-01')).toBe('2026-09-28')
    expect(addDays('2026-09-30', 1)).toBe('2026-10-01')
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31')
  })

  it('hafta oralig\'i yorlig\'i', () => {
    expect(weekRangeLabel('2026-09-21', '2026-09-27')).toBe('21–27 sentabr')
    expect(weekRangeLabel('2026-09-28', '2026-10-04')).toBe('28 sentabr – 4 oktabr')
  })

  it('nisbiy kun', () => {
    expect(relativeDay('2026-09-27', '2026-09-27')).toBe('Bugun')
    expect(relativeDay('2026-09-28', '2026-09-27')).toBe('Ertaga')
    expect(relativeDay('2026-09-26', '2026-09-27')).toBe('Kecha')
    expect(relativeDay('2026-10-02', '2026-09-27')).toBe('2-oktabr')
  })

  it('guruh kunlari: toq/juft kunlar nomi bilan', () => {
    expect(daysLabel([4, 0, 2])).toBe('Toq kunlar (Du, Cho, Ju)')
    expect(daysLabel([1, 3, 5])).toBe('Juft kunlar (Se, Pa, Sha)')
    expect(daysLabel([0, 3])).toBe('Du, Pa')
  })
})

describe('lessonPhase — dars holati', () => {
  const today = '2026-09-27'

  it("o'tilgan dars boshqa hamma narsadan ustun", () => {
    expect(lessonPhase(lesson({ attendanceTaken: true, substitutedBy: 'X' }), today, '10:00')).toBe('done')
    // Yagona ta'rif: bitta baho bilan "o'tildi" bo'lgan dars davomati OLINGAN emas — hali kutilmoqda.
    expect(lessonPhase(lesson({ conducted: true, date: '2026-09-01' }), today, '10:00')).toBe('pending')
  })

  it("boshqa odam o'tadigan dars — davomat kutilmaydi", () => {
    const l = lesson({ substitutedBy: 'Vali' })
    expect(lessonPhase(l, today, '16:00')).toBe('substituted')
    expect(canTakeAttendance(l, today)).toBe(false)
  })

  it('vaqt bo\'yicha: keyinroq → hozir → kutilmoqda', () => {
    expect(lessonPhase(lesson(), today, '13:59')).toBe('upcoming')
    expect(lessonPhase(lesson(), today, '14:00')).toBe('now')
    expect(lessonPhase(lesson(), today, '15:30')).toBe('pending')
  })

  it("o'tgan kundagi belgilanmagan dars — kutilmoqda; kelajak — rejada", () => {
    expect(lessonPhase(lesson({ date: '2026-09-26' }), today, '09:00')).toBe('pending')
    expect(lessonPhase(lesson({ date: '2026-09-28' }), today, '09:00')).toBe('future')
    expect(canTakeAttendance(lesson({ date: '2026-09-28' }), today)).toBe(false)
  })

  it('vaqti yo\'q dars bugun — kutilmoqda (davomat qilish mumkin)', () => {
    const l = lesson({ startTime: '', endTime: '' })
    expect(lessonPhase(l, today, '09:00')).toBe('pending')
    expect(canTakeAttendance(l, today)).toBe(true)
  })
})

describe('menyu katalogi', () => {
  const all = () => true

  it('har bir tab katalogda bor', () => {
    for (const to of TEACHER_TABS) expect(TEACHER_NAV.some((i) => i.to === to)).toBe(true)
  })

  it("ruxsati yo'q bo'lim ko'rinmaydi, Support faqat Support o'qituvchiga", () => {
    const salary = TEACHER_NAV.find((i) => i.to === '/teacher/salary')!
    const support = TEACHER_NAV.find((i) => i.to === '/teacher/support')!
    expect(navVisible(salary, (p) => p !== 'salary', false)).toBe(false)
    expect(navVisible(support, all, false)).toBe(false)
    expect(navVisible(support, all, true)).toBe(true)
  })

  it('bosh sahifa faqat aniq manzilda, guruhlar ichki sahifada ham faol', () => {
    const home = TEACHER_NAV.find((i) => i.to === '/teacher')!
    const groups = TEACHER_NAV.find((i) => i.to === '/teacher/groups')!
    expect(navMatches(home, '/teacher')).toBe(true)
    expect(navMatches(home, '/teacher/groups')).toBe(false)
    expect(navMatches(groups, '/teacher/groups/abc')).toBe(true)
    expect(navMatches(groups, '/teacher/groupsx')).toBe(false)
  })
})
