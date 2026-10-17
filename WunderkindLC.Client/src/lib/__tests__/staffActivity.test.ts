import { describe, expect, it } from 'vitest'
import { activitySummary, daysAgo, isStale, lastSeenLabel } from '../staffActivity'

describe('staffActivity', () => {
  it('kun farqi markaz sanasi bo\'yicha', () => {
    expect(daysAgo('2026-10-17T23:50:00', '2026-10-17')).toBe(0)
    expect(daysAgo('2026-10-16T08:00:00', '2026-10-17')).toBe(1)
    expect(daysAgo('2026-09-30T08:00:00', '2026-10-17')).toBe(17)
  })

  it('yorliq: bugun / kecha / N kun oldin / hech qachon', () => {
    expect(lastSeenLabel('2026-10-17T09:30:00', '2026-10-17')).toBe('bugun 09:30')
    expect(lastSeenLabel('2026-10-16T18:05:00', '2026-10-17')).toBe('kecha 18:05')
    expect(lastSeenLabel('2026-10-10T18:05:00', '2026-10-17')).toBe('7 kun oldin')
    expect(lastSeenLabel(null, '2026-10-17')).toBe('hech qachon')
  })

  it('7 kundan ko\'p kirmagan — sust', () => {
    expect(isStale('2026-10-10T00:00:00', '2026-10-17')).toBe(false)
    expect(isStale('2026-10-09T00:00:00', '2026-10-17')).toBe(true)
    expect(isStale(null, '2026-10-17')).toBe(true)
  })

  it('faollik matni faqat nol bo\'lmaganlardan', () => {
    expect(activitySummary({ auditActions: 12, tasksClosed: 0, leadsHandled: 5, attendanceLessons: 0 })).toBe('12 amal · 5 lid')
    expect(activitySummary({ auditActions: 0, tasksClosed: 0, leadsHandled: 0, attendanceLessons: 0 })).toBe('—')
  })
})
