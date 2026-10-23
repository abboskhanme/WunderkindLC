import { describe, expect, it } from 'vitest'
import { splitPayment } from '../paymentSplit'

/**
 * Klientdagi `splitPayment` serverdagi `PaymentMonthSplit.Allocate` bilan bir xil qolishini qulflaydi
 * (`WunderkindLC.Tests/PaymentMonthSplitTests.cs` dagi holatlar bilan bir xil).
 */
const ROWS = [
  { month: '2026-09', remaining: 20_000 },
  { month: '2026-10', remaining: 225_000 },
  { month: '2026-11', remaining: 225_000 },
]

describe("splitPayment — to'lovni oylarga taqsimlash", () => {
  it("qoldiqqa sig'sa — bitta qism, tanlangan oyda", () => {
    expect(splitPayment(ROWS, '2026-09', 20_000)).toEqual([{ month: '2026-09', amount: 20_000 }])
  })

  it("qoldiqdan oshsa — ortig'i keyingi oyga", () => {
    expect(splitPayment(ROWS, '2026-09', 225_000)).toEqual([
      { month: '2026-09', amount: 20_000 },
      { month: '2026-10', amount: 205_000 },
    ])
  })

  it('hammasidan ortgani oxirgi qatorda qoladi', () => {
    expect(splitPayment(ROWS, '2026-09', 500_000)).toEqual([
      { month: '2026-09', amount: 20_000 },
      { month: '2026-10', amount: 225_000 },
      { month: '2026-11', amount: 255_000 },
    ])
  })

  it("oradagi to'langan oy o'tkazib yuboriladi", () => {
    expect(
      splitPayment(
        [
          { month: '2026-09', remaining: 20_000 },
          { month: '2026-10', remaining: 0 },
          { month: '2026-11', remaining: 225_000 },
        ],
        '2026-09',
        120_000,
      ),
    ).toEqual([
      { month: '2026-09', amount: 20_000 },
      { month: '2026-11', amount: 100_000 },
    ])
  })

  it("to'langan oy ataylab tanlansa yoki ro'yxatda yo'q bo'lsa — bo'linmaydi", () => {
    expect(
      splitPayment(
        [
          { month: '2026-09', remaining: 0 },
          { month: '2026-10', remaining: 225_000 },
        ],
        '2026-09',
        225_000,
      ),
    ).toEqual([{ month: '2026-09', amount: 225_000 }])
    expect(splitPayment(ROWS, '2027-05', 225_000)).toEqual([{ month: '2027-05', amount: 225_000 }])
    expect(splitPayment([], '2026-10', 1)).toEqual([{ month: '2026-10', amount: 1 }])
  })

  it("orqaga ketmaydi — tanlangan oydan oldingi qarz tegilmaydi", () => {
    expect(splitPayment(ROWS, '2026-10', 250_000)).toEqual([
      { month: '2026-10', amount: 225_000 },
      { month: '2026-11', amount: 25_000 },
    ])
  })
})
