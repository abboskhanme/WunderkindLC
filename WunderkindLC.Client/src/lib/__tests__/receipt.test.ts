import { describe, expect, it } from 'vitest'
import { defaultCheckSettings, parseCheckSettings, resolveCheckSettings } from '@/config/checkSettings'
import { receiptWidth } from '@/lib/receipt'

// 2026-10-17: the check was laid out across the whole paper and the printer cut the right edge.
// The check is now as wide as the printer's print area for the chosen paper.
describe('chek qog\'oz eni', () => {
  it("saqlangan JSON'da eni yo'q (eski sozlama) — standart 58mm", () => {
    expect(parseCheckSettings(JSON.stringify({ showLogo: false })).paperWidth).toBe(58)
    expect(parseCheckSettings('').paperWidth).toBe(58)
    expect(parseCheckSettings('{buzuq').paperWidth).toBe(58)
    expect(defaultCheckSettings.paperWidth).toBe(58)
  })

  it("noma'lum qiymat 58 ga tushadi, 80 saqlanadi", () => {
    expect(parseCheckSettings(JSON.stringify({ paperWidth: 72 })).paperWidth).toBe(58)
    expect(parseCheckSettings(JSON.stringify({ paperWidth: '80' })).paperWidth).toBe(58)
    expect(parseCheckSettings(JSON.stringify({ paperWidth: 80 })).paperWidth).toBe(80)
  })

  it('chek eni = printerning bosish maydoni (58 → 48mm, 80 → 72mm)', () => {
    expect(receiptWidth({ paperWidth: 58 })).toBe('48mm')
    expect(receiptWidth({ paperWidth: 80 })).toBe('72mm')
  })

  it('sinov cheki ham shu enni oladi', () => {
    const s = parseCheckSettings(JSON.stringify({ paperWidth: 80 }))
    expect(resolveCheckSettings(s, true).paperWidth).toBe(80)
  })
})
