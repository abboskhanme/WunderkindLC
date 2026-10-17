import { describe, expect, it } from 'vitest'
import {
  ageDaysOf, ageTone, dueDateOf, invoiceStatusLabel, matchesSearch, memberStatusLabel, pctLabel,
} from '@/lib/financeReports'

describe('moliya hisobotlari — yordamchilar', () => {
  it("to'lov muddati server bilan bir xil: kun oy uzunligiga qisiladi", () => {
    expect(dueDateOf('2026-02', 31)?.getDate()).toBe(28)
    expect(dueDateOf('2026-10', 10)?.getDate()).toBe(10)
    expect(dueDateOf('buzuq', 10)).toBeNull()
  })

  it('qarz yoshi va rangi', () => {
    const today = new Date(2026, 9, 25)
    expect(ageDaysOf('2026-10', 10, today)).toBe(15)
    expect(ageDaysOf('2026-11', 10, today)).toBeLessThan(0)
    expect(ageTone(-1)).toContain('slate')
    expect(ageTone(15)).toContain('amber')
    expect(ageTone(45)).toContain('orange')
    expect(ageTone(60)).toContain('rose')
  })

  it('yorliqlar', () => {
    expect(pctLabel(72.46)).toBe('72.5%')
    expect(pctLabel(100)).toBe('100%')
    expect(memberStatusLabel('active')).toBe('')
    expect(memberStatusLabel('left')).toBe('Chiqgan')
    expect(invoiceStatusLabel('partial')).toBe('Qisman')
  })

  it('qidiruv ism bo\'yicha ham, telefon raqamlari bo\'yicha ham', () => {
    expect(matchesSearch('ali', ['Aliyev Vali'])).toBe(true)
    expect(matchesSearch('901112233', ['X'], '+998 90 111 22 33')).toBe(true)
    expect(matchesSearch('12', ['X'], '+998 90 111 22 33')).toBe(false)
    expect(matchesSearch('', ['X'])).toBe(true)
  })
})
