import { describe, it, expect } from 'vitest'
import type { TodayLessonMonitor } from '@/types'
import { notTakenLessons, overdueCount, phaseLabel } from '../attendanceWatch'

const L = (id: string, patch: Partial<TodayLessonMonitor> = {}): TodayLessonMonitor => ({
  groupId: id,
  groupName: id.toUpperCase(),
  courseName: '',
  teacherId: 't1',
  teacherName: 'Ali',
  room: '',
  startTime: '14:00',
  endTime: '15:30',
  studentsCount: 10,
  attendanceDone: false,
  gradesDone: false,
  ...patch,
})

describe("«Davomat olinmagan guruhlar» — ro'yxat (qoida serverda, bu yerda faqat filtr/tartib)", () => {
  const lessons = [
    L('later', { startTime: '18:00', phase: 'upcoming', overdue: false }),
    L('done', { attendanceDone: true, phase: 'ended', overdue: false }),
    L('late2', { startTime: '16:00', phase: 'started', overdue: true }),
    L('late1', { startTime: '09:00', phase: 'ended', overdue: true }),
  ]

  it('davomati olingan dars hech qachon chiqmaydi', () => {
    expect(notTakenLessons(lessons, false).map((l) => l.groupId)).not.toContain('done')
  })

  it("«Kechikkanlar» — faqat boshlangan/tugaganlari, hali boshlanmagan KIRMAYDI", () => {
    expect(notTakenLessons(lessons, true).map((l) => l.groupId)).toEqual(['late1', 'late2'])
    expect(overdueCount(lessons)).toBe(2)
  })

  it('«Hammasi» — kechikkanlar tepada, keyin vaqt bo\'yicha', () => {
    expect(notTakenLessons(lessons, false).map((l) => l.groupId)).toEqual(['late1', 'late2', 'later'])
  })

  it('bosqich yorlig\'i', () => {
    expect(phaseLabel(L('a', { phase: 'upcoming' }))).toBe('Hali boshlanmagan')
    expect(phaseLabel(L('a', { phase: 'ended' }))).toBe('Dars tugagan')
    expect(phaseLabel(L('a', { phase: 'started' }))).toBe('Dars ketyapti')
  })
})
