import { describe, expect, it } from 'vitest'
import { canCancelAssignment, cancelPlan, isoMinusDays } from '../substituteCancel'

describe('isoMinusDays — mahalliy kalendar', () => {
  it('oy va yil chegarasidan o\'tadi', () => {
    expect(isoMinusDays('2026-10-07', 14)).toBe('2026-09-23')
    expect(isoMinusDays('2026-01-05', 14)).toBe('2025-12-22')
    expect(isoMinusDays('2024-03-10', 14)).toBe('2024-02-25')
  })
})

describe('cancelPlan — server qoidasining nusxasi (MaxBackdateDays = 14)', () => {
  const today = '2026-10-07' // chegara: 2026-09-23

  it('hamma kun oyna ichida — hammasi bekor qilinadi', () => {
    const p = cancelPlan({ dates: ['2026-10-01', '2026-10-10'], date: '2026-10-01' }, today)
    expect(p.removable).toEqual(['2026-10-01', '2026-10-10'])
    expect(p.locked).toEqual([])
  })

  it('bir qismi 14 kundan eski — faqat yangilari bekor bo\'ladi, eskilari qoladi', () => {
    const p = cancelPlan({ dates: ['2026-09-10', '2026-09-23', '2026-10-09'], date: '2026-09-10' }, today)
    expect(p.locked).toEqual(['2026-09-10'])
    expect(p.removable).toEqual(['2026-09-23', '2026-10-09'])
  })

  it('hammasi eski — tugma ko\'rinmaydi (server baribir 400 qaytaradi)', () => {
    const a = { isActive: true, dates: ['2026-08-17', '2026-08-19'], date: '2026-08-17' }
    expect(canCancelAssignment(a, today)).toBe(false)
  })

  it('bekor qilingan tayinlovda tugma yo\'q', () => {
    expect(canCancelAssignment({ isActive: false, dates: ['2026-10-10'], date: '2026-10-10' }, today)).toBe(false)
  })

  it('eski oraliqli yozuv (dates yo\'q) — oxirgi sana bo\'yicha', () => {
    expect(canCancelAssignment({ isActive: true, dates: null, date: '2026-09-01', endDate: '2026-09-30' }, today)).toBe(true)
    expect(canCancelAssignment({ isActive: true, dates: [], date: '2026-08-01', endDate: '2026-08-31' }, today)).toBe(false)
  })
})
