import { describe, expect, it } from 'vitest'
import type { ActivationQuote } from '@/api/services/classes'
import { activationNextMonthsText, activationQuoteText } from '../activationQuote'

const q = (p: Partial<ActivationQuote>): ActivationQuote => ({
  month: '2026-10', monthly: 300000, lessonsInMonth: 13, lessonsLeft: 8, lessonFee: 25000, amount: 200000, fullMonth: false, ...p,
})

describe('activationQuoteText — birinchi oy qanday hisoblangani', () => {
  it('qisman oy: qolgan darslar × bir dars narxi', () => {
    const t = activationQuoteText(q({}))
    expect(t).toContain('13 darsdan 8 tasi qoladi')
    expect(t).toContain('8 dars ×')
    expect(t).toMatch(/200\s000/)
  })
  it('bir dars narxi yo\'q — pro-rata ko\'rinishi', () => {
    expect(activationQuoteText(q({ lessonFee: 0, amount: 184615 }))).toContain('÷ 13 dars × 8')
  })
  it('birinchi darsdan — to\'liq oylik; darssiz qolgan oy va narxsiz guruh ochiq aytiladi', () => {
    expect(activationQuoteText(q({ fullMonth: true, lessonsLeft: 13, amount: 300000 }))).toContain("to'liq oylik")
    expect(activationQuoteText(q({ lessonsLeft: 0, amount: 0 }))).toContain('hisob yozilmaydi')
    expect(activationQuoteText(q({ monthly: 0, amount: 0 }))).toContain("oylik narx yo'q")
  })
  it('keyingi oylar — to\'liq oylik', () => {
    expect(activationNextMonthsText(q({}))).toMatch(/to'liq oylik 300\s000/)
    expect(activationNextMonthsText(q({ monthly: 0 }))).toBe('')
  })
})
