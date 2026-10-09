import { describe, expect, it } from 'vitest'
import { leadMatchesStatus } from '../leadLabels'

/**
 * «Buyurtmalar ro'yxati» va «Birinchi darsga yozilganlar» KESISHMAYDI (mijoz shikoyati, 2026-10-09:
 * "lid Birinchi darsga o'tkazilganda oldingi bosqichdan o'chmayapti — ikkala bo'limda qolib ketyapti").
 */
describe('leadMatchesStatus', () => {
  const order = { convertedStudentId: null, firstLessonAt: null }
  const firstLesson = { convertedStudentId: null, firstLessonAt: '2026-10-12T15:00' }
  const student = { convertedStudentId: 's1', firstLessonAt: null }

  it('buyurtmalar — faqat sinovga yozilmagan va aylantirilmagan lid', () => {
    expect(leadMatchesStatus(order, 'lead')).toBe(true)
    expect(leadMatchesStatus(firstLesson, 'lead')).toBe(false)
    expect(leadMatchesStatus(student, 'lead')).toBe(false)
  })

  it('har lid AYNAN bitta bo`limda (buyurtma / birinchi dars / o`quvchi)', () => {
    for (const l of [order, firstLesson, student]) {
      const hits = ['lead', 'first', 'student'].filter((s) => leadMatchesStatus(l, s))
      expect(hits).toHaveLength(1)
    }
  })

  it('«hammasi» va noma`lum qiymat — filtrsiz', () => {
    for (const l of [order, firstLesson, student]) {
      expect(leadMatchesStatus(l, 'all')).toBe(true)
      expect(leadMatchesStatus(l, 'eskirgan')).toBe(true)
    }
  })
})
