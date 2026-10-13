import { describe, expect, it } from 'vitest'
import { dmyToIso, formatTyped, isValidIso, isoToDmy, splitLayoutClasses } from '../dateInput'

describe('dateInput — sana har doim kun.oy.yil', () => {
  it('ISO ↔ ko\'rinish', () => {
    expect(isoToDmy('2026-10-15')).toBe('15.10.2026')
    expect(isoToDmy('')).toBe('')
    expect(isoToDmy(null)).toBe('')
    expect(isoToDmy('15.10.2026')).toBe('')
    expect(dmyToIso('15.10.2026')).toBe('2026-10-15')
    expect(dmyToIso('')).toBe('')
  })

  it('yozishda nuqtalar o\'zi qo\'yiladi, faqat raqam, 8 tagacha; ISO joylashtirish ham', () => {
    expect(formatTyped('1')).toBe('1')
    expect(formatTyped('15')).toBe('15')
    expect(formatTyped('151')).toBe('15.1')
    expect(formatTyped('15.10')).toBe('15.10')
    expect(formatTyped('15102026')).toBe('15.10.2026')
    expect(formatTyped('15/10/2026 99')).toBe('15.10.2026')
    expect(formatTyped('ab')).toBe('')
    expect(formatTyped('2026-10-15')).toBe('15.10.2026')
  })

  it('kalendarda yo\'q yoki chala sana — null (yuborilmaydi)', () => {
    expect(dmyToIso('31.02.2026')).toBeNull()
    expect(dmyToIso('29.02.2026')).toBeNull()
    expect(dmyToIso('29.02.2028')).toBe('2028-02-29')
    expect(dmyToIso('15.13.2026')).toBeNull()
    expect(dmyToIso('15.10.20')).toBeNull()
    expect(isValidIso('2026-04-31')).toBe(false)
  })

  it('min / max chegarasi', () => {
    expect(dmyToIso('15.10.2026', '2026-10-16')).toBeNull()
    expect(dmyToIso('15.10.2026', undefined, '2026-10-14')).toBeNull()
    expect(dmyToIso('15.10.2026', '2026-10-15', '2026-10-15')).toBe('2026-10-15')
  })

  it('joylashuv klasslari o\'ramga, qolgani maydonga', () => {
    expect(splitLayoutClasses('h-9 w-40 rounded-lg px-3 mt-2 sm:w-[150px] flex-1 shrink-0')).toEqual({
      wrapper: 'w-40 mt-2 sm:w-[150px] flex-1 shrink-0',
      input: 'h-9 rounded-lg px-3',
    })
    expect(splitLayoutClasses(undefined)).toEqual({ wrapper: '', input: '' })
    expect(splitLayoutClasses('max-h-9 min-h-[34px]').input).toBe('max-h-9 min-h-[34px]')
    // base `w-full` + caller `w-[150px]` — the caller's (last) width wins; responsive widths are separate families
    expect(splitLayoutClasses('h-[34px] w-full px-3 w-[150px] sm:w-full').wrapper).toBe('w-[150px] sm:w-full')
  })
})
