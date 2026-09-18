import { describe, it, expect } from 'vitest'
import {
  dayKind,
  formatGroupDays,
  formatGroupPeriod,
  formatLessonTime,
  lessonCovers,
  lessonDurationLabel,
  membershipBadge,
  notAttendedGroupIds,
  toGroupHistoryRow,
} from '../groupDisplay'

describe('guruh kunlari — edutizim "KUN" ustuni', () => {
  it('toq/juft naqsh — "Toq kunlar" / "Juft kunlar"', () => {
    expect(formatGroupDays([4, 0, 2])).toBe('Toq kunlar')
    expect(formatGroupDays([1, 3, 5])).toBe('Juft kunlar')
  })

  it("aralash kunlar — qisqa ro'yxat, bo'sh — tire", () => {
    expect(formatGroupDays([1, 0])).toBe('Du, Se')
    expect(formatGroupDays([])).toBe('—')
  })

  it('dayKind — filtr uchun (qisman toq ham "odd")', () => {
    expect(dayKind([0, 2])).toBe('odd')
    expect(dayKind([1, 5])).toBe('even')
    expect(dayKind([0, 1])).toBe('other')
    expect(dayKind(undefined)).toBe('other')
  })
})

describe('vaqt va davr matnlari', () => {
  it('dars vaqti', () => {
    expect(formatLessonTime('07:00', '09:00')).toBe('07:00 - 09:00')
    expect(formatLessonTime('07:00', '')).toBe('07:00')
    expect(formatLessonTime()).toBe('—')
  })

  it('guruh vaqti chipi', () => {
    expect(formatGroupPeriod('2026-09-02', '2026-12-31')).toBe('02.09.2026 - 31.12.2026')
    expect(formatGroupPeriod('2026-09-02', '')).toBe('02.09.2026 - …')
    expect(formatGroupPeriod('', null)).toBe('')
  })

  it('dars davomiyligi — buzuq qatorda bo\'sh (schedule.md §1)', () => {
    expect(lessonDurationLabel('08:00', '10:00')).toBe('2 soat')
    expect(lessonDurationLabel('08:00', '09:30')).toBe('1 soat 30 daqiqa')
    expect(lessonDurationLabel('13:30', '03:00')).toBe('')
    expect(lessonDurationLabel('21:29', '')).toBe('')
  })

  it('"Dars vaqti" filtri — yarim-ochiq oraliq [start, end)', () => {
    expect(lessonCovers('08:00', '10:00', '08:00')).toBe(true)
    expect(lessonCovers('08:00', '10:00', '09:59')).toBe(true)
    expect(lessonCovers('08:00', '10:00', '10:00')).toBe(false)
    // Tugash vaqti buzuq — faqat boshlanish vaqti mos keladi.
    expect(lessonCovers('08:00', '', '08:00')).toBe(true)
    expect(lessonCovers('08:00', '', '09:00')).toBe(false)
  })
})

describe("a'zolik holati belgisi", () => {
  it('«Aktiv muzlatish» — yangi status EMAS, frozen ning aniqroq yozuvi', () => {
    expect(membershipBadge('frozen', true).label).toBe('Aktiv muzlatilgan')
    expect(membershipBadge('frozen').label).toBe('Muzlatilgan')
    expect(membershipBadge('active').label).toBe('Aktiv')
    expect(membershipBadge('trial').label).toBe('Sinov')
  })

  it('chiqqan/tugatgan a\'zolik', () => {
    expect(membershipBadge('completed', false, false).label).toBe('Tugatgan')
    expect(membershipBadge('active', false, false).label).toBe('Chiqqan')
  })
})

describe('bugun davomat qilinmagan guruhlar', () => {
  it('faqat darsi bor va davomati olinmaganlar', () => {
    const ids = notAttendedGroupIds([
      { groupId: 'a', attendanceDone: false },
      { groupId: 'b', attendanceDone: true },
    ])
    expect([...ids]).toEqual(['a'])
  })
})

describe('guruh tarixi qatori', () => {
  const names = new Map([['s1', 'Ali Valiyev']])

  it("o'qituvchi almashuvi — eski va yangi o'qituvchi ajratiladi", () => {
    const row = toGroupHistoryRow(
      {
        id: '1',
        timestamp: '2026-09-02T10:00:00',
        actorName: 'Admin',
        summary: "Guruh o'qituvchisi almashtirildi (Beginner kids): Aziza K. → Nodira S.",
      },
      names,
    )
    expect(row.oldTeacher).toBe('Aziza K.')
    expect(row.teacher).toBe('Nodira S.')
    expect(row.moderator).toBe('Admin')
  })

  it("a'zolik hodisasi — o'quvchi nomi, o'qituvchi ustunlari bo'sh", () => {
    const row = toGroupHistoryRow(
      { id: '2', timestamp: '2026-09-03T11:00:00', summary: "Muzlatildi …", studentId: 's1' },
      names,
    )
    expect(row.student).toBe('Ali Valiyev')
    expect(row.oldTeacher).toBe('')
    expect(row.type).toBe('Muzlatildi …')
  })

  it("studentId yo'q eski a'zolik yozuvi — o'quvchi EntityId dan", () => {
    const row = toGroupHistoryRow(
      { id: '3', timestamp: '2026-09-04T09:00:00', summary: 'x', entityType: 'Membership', entityId: 'g1:s1' },
      names,
    )
    expect(row.student).toBe('Ali Valiyev')
  })
})
