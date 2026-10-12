import { describe, expect, it } from 'vitest'
import {
  cellError,
  formatSentAt,
  livePercent,
  maxSolved,
  nextRowIndex,
  parsePastedColumn,
  pastedError,
  rangeMessage,
  sendSummary,
} from '../testScoreGrid'

/** CRM U4a — Excel'dek natijalar jadvali (docs/SPEC-crm-upgrade.md P4.2, P4.9 #1). */
describe('parsePastedColumn — Excel ustunini qo\'yish', () => {
  it("har qator — bitta o'quvchi, 18 qatorli ustun 18 ta qiymat", () => {
    const col = Array.from({ length: 18 }, (_, i) => String(i + 10)).join('\r\n') + '\r\n'
    const parsed = parsePastedColumn(col)
    expect(parsed).toHaveLength(18)
    expect(parsed[0]).toBe(10)
    expect(parsed[17]).toBe(27)
  })

  it('"25/30" → 25, bo\'sh → tozalash, birinchi ustun olinadi', () => {
    expect(parsePastedColumn('25/30\n\n7\tAli\n 12 / 30 ')).toEqual([25, null, 7, 12])
  })

  it('tanib bo\'lmagan matn va kasr — xato (qiymat o\'zgarmaydi)', () => {
    const [a, b] = parsePastedColumn('abc\n12.5')
    expect(a).toEqual({ invalid: 'abc' })
    expect(b).toEqual({ invalid: '12.5' })
    expect(pastedError(a, 30)).toBe('Raqam emas: abc')
    expect(pastedError(31, 30)).toBe(rangeMessage(30))
    expect(pastedError(null, 30)).toBeNull()
  })
})

describe('katak qoidalari', () => {
  it("0..savollar soni oralig'idagi BUTUN son; 31 — qizil, server matni bilan bir xil", () => {
    expect(cellError(null, 30)).toBeNull()
    expect(cellError(0, 30)).toBeNull()
    expect(cellError(30, 30)).toBeNull()
    expect(cellError(31, 30)).toBe("Natija 0 dan 30 gacha butun son bo'lsin")
    expect(cellError(2.5, 30)).toBe(rangeMessage(30))
  })

  it('eski testda chegara — maksimal ballning butun qismi', () => {
    expect(maxSolved(30, 30)).toBe(30)
    expect(maxSolved(0, 37.5)).toBe(37)
  })

  it('jonli foiz = yechgan ÷ savollar, bir kasr; noto\'g\'ri katakda yo\'q', () => {
    expect(livePercent(25, 30, 30)).toBe(83.3)
    expect(livePercent(30, 30, 30)).toBe(100)
    expect(livePercent(31, 30, 30)).toBeNull()
    expect(livePercent(null, 30, 30)).toBeNull()
  })
})

describe('klaviatura — Enter/↓ pastga, Shift+Enter/↑ yuqoriga', () => {
  it('chekkada joyida qoladi, boshqa tugma — navigatsiya emas', () => {
    expect(nextRowIndex('Enter', false, 0, 5)).toBe(1)
    expect(nextRowIndex('ArrowDown', false, 4, 5)).toBe(4)
    expect(nextRowIndex('Enter', true, 2, 5)).toBe(1)
    expect(nextRowIndex('ArrowUp', false, 0, 5)).toBe(0)
    expect(nextRowIndex('Tab', false, 1, 5)).toBeNull()
    expect(nextRowIndex('5', false, 1, 5)).toBeNull()
  })
})

describe('yuborish yorliqlari', () => {
  it('«Yuborilgan: 12.10 15:40» va xulosa', () => {
    expect(formatSentAt('2026-10-12T15:40:12')).toBe('12.10 15:40')
    expect(formatSentAt(null)).toBe('')
    expect(sendSummary({ queued: 12, noTelegram: 3, skippedUnchanged: 5 })).toBe(
      "12 ta xabar navbatga qo'yildi · o'zgarmagan: 5 · Telegram'ga ulanmagan: 3",
    )
  })
})
