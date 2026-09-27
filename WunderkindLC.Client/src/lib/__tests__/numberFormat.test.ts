import { describe, expect, it } from 'vitest'
import {
  formatNumber,
  formatNumberInput,
  formatNumberValue,
  formatPhone,
  formatPhoneInput,
  groupDigits,
  parseNumberInput,
  typedPhoneInput,
} from '../numberFormat'

/**
 * Raqam va telefon formati — O'zbekiston odatlari: summa `1 250 000`, kasr `12,5`,
 * telefon `+998 90 123 45 67`. Asosiy qulf: bo'sh maydon `null` (ilgari `0` qaytib yozilardi).
 */

describe('summa — yozish paytida', () => {
  it('3 xonadan bo\'sh joy bilan ajratadi', () => {
    expect(groupDigits('1250000')).toBe('1 250 000')
    expect(formatNumberInput('1250000', { group: true })).toBe('1 250 000')
    expect(formatNumberInput('999', { group: true })).toBe('999')
  })

  it("bo'sh qator bo'sh qoladi va null bo'ladi (0 EMAS)", () => {
    expect(formatNumberInput('', { group: true })).toBe('')
    expect(parseNumberInput('')).toBeNull()
    expect(parseNumberInput('   ')).toBeNull()
  })

  it("boshidagi nol olib tashlanadi: 0 dan keyin yozilsa '05' emas '5'", () => {
    expect(formatNumberInput('05000', { group: true })).toBe('5 000')
    expect(formatNumberInput('0', { group: true })).toBe('0')
  })

  it('harf va begona belgilar tashlanadi, yopishtirilgan formatli summa ham o\'qiladi', () => {
    expect(formatNumberInput('1 250 000 so\'m', { group: true })).toBe('1 250 000')
    expect(formatNumberInput('abc', { group: true })).toBe('')
  })

  it('kasr: vergul ham nuqta ham qabul qilinadi, ko\'rsatishda vergul, xonalar cheklanadi', () => {
    expect(formatNumberInput('12.5', { decimals: 2 })).toBe('12,5')
    expect(formatNumberInput('12,555', { decimals: 2 })).toBe('12,55')
    expect(formatNumberInput(',5', { decimals: 1 })).toBe('0,5')
    expect(formatNumberInput('12.5', { decimals: 0 })).toBe('125')
    expect(parseNumberInput('12,5')).toBe(12.5)
  })

  it("guruhsiz rejim (yil, port, kod) ajratmaydi", () => {
    expect(formatNumberInput('2026', { group: false })).toBe('2026')
  })

  it('manfiy faqat ruxsat berilganda', () => {
    expect(formatNumberInput('-500', { group: true })).toBe('500')
    expect(formatNumberInput('-5000', { group: true, allowNegative: true })).toBe('-5 000')
    expect(parseNumberInput('-5 000')).toBe(-5000)
  })
})

describe('summa — ko\'rsatish', () => {
  it('son → maydon matni', () => {
    expect(formatNumberValue(1250000, { group: true })).toBe('1 250 000')
    expect(formatNumberValue(null, { group: true })).toBe('')
    expect(formatNumberValue(undefined, { group: true })).toBe('')
    expect(formatNumberValue(0, { group: true })).toBe('0')
    expect(formatNumberValue(12.5, { decimals: 2 })).toBe('12,5')
    expect(formatNumberValue(12, { decimals: 2 })).toBe('12')
  })

  it('formatNumber — ro\'yxatlar uchun', () => {
    expect(formatNumber(4500000)).toBe('4 500 000')
    expect(formatNumber(-15000)).toBe('-15 000')
  })

  it('round-trip: format → parse o\'sha sonni beradi', () => {
    for (const n of [0, 5, 999, 1000, 1250000, 987654321]) {
      expect(parseNumberInput(formatNumberValue(n, { group: true }))).toBe(n)
    }
  })
})

describe('telefon', () => {
  it('yozish paytida +998 formatida (qisman ham)', () => {
    expect(formatPhoneInput('9')).toBe('+998 9')
    expect(formatPhoneInput('901234567')).toBe('+998 90 123 45 67')
    expect(formatPhoneInput('998901234567')).toBe('+998 90 123 45 67')
    expect(formatPhoneInput('')).toBe('')
  })

  it("ko'rsatishda: o'zbek raqami formatlanadi, xorijiy/noto'g'ri raqam TEGILMAYDI", () => {
    expect(formatPhone('998901234567')).toBe('+998 90 123 45 67')
    expect(formatPhone('+998 (90) 123-45-67')).toBe('+998 90 123 45 67')
    expect(formatPhone('901234567')).toBe('+998 90 123 45 67')
    expect(formatPhone('+79161234567')).toBe('+79161234567')
    expect(formatPhone('12345')).toBe('12345')
    expect(formatPhone(null)).toBe('')
  })
})

describe('typedPhoneInput — o\'z ko\'rinishini saqlaydigan telefon maydoni', () => {
  it('yozishda formatlaydi', () => {
    expect(typedPhoneInput('+998 9012', '+998 901')).toBe('+998 90 12')
  })
  it("o'chirishda faqat +998 qolsa maydon butunlay tozalanadi", () => {
    expect(typedPhoneInput('+998 ', '+998 9')).toBe('')
  })
  it("yozishda (o'chirmasdan) +998 qoldiriladi", () => {
    expect(typedPhoneInput('998', '')).toBe('+998')
  })
})
