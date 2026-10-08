import { describe, expect, it } from 'vitest'
import { localIsoDate, shiftLocalDay } from '../localDate'

describe('localIsoDate — mahalliy sana (UTC emas)', () => {
  it('yarim tundan keyin (01:30) ham BUGUNGI kunni beradi', () => {
    // UTC+5 da 01:30 — UTC bo'yicha kechagi 20:30; toISOString() kechani berardi.
    expect(localIsoDate(new Date(2026, 9, 7, 1, 30))).toBe('2026-10-07')
  })
  it('oy va kun ikki xonali', () => {
    expect(localIsoDate(new Date(2026, 0, 5, 12, 0))).toBe('2026-01-05')
  })
})

describe('shiftLocalDay', () => {
  it('oy va yil chegarasidan o\'tadi', () => {
    expect(shiftLocalDay('2026-12-31', 1)).toBe('2027-01-01')
    expect(shiftLocalDay('2026-03-01', -1)).toBe('2026-02-28')
    expect(shiftLocalDay('2024-03-01', -1)).toBe('2024-02-29')
  })
  it('"7 kun" — bugun bilan birga 7 kun (−6)', () => {
    expect(shiftLocalDay('2026-10-07', -6)).toBe('2026-10-01')
  })
})
