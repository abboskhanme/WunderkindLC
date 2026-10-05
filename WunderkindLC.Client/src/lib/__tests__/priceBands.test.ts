import { describe, expect, it } from 'vitest'
import { bandLabel, nextBandStart, sameBands, toPayloadBands, validateBands, type BandDraft } from '../priceBands'

const d = (gradeFrom: number, gradeTo: number, price: number | null = 100_000, lessonPrice: number | null = null): BandDraft => ({
  gradeFrom, gradeTo, price, lessonPrice,
})

describe('bandLabel', () => {
  it('serverdagi TuitionPricing.Label bilan bir xil', () => {
    expect(bandLabel({ gradeFrom: 3, gradeTo: 7 })).toBe('3–7-sinf')
    expect(bandLabel({ gradeFrom: 5, gradeTo: 5 })).toBe('5-sinf')
    expect(bandLabel({ gradeFrom: 12, gradeTo: 12 })).toBe('Maktabni bitirgan')
    expect(bandLabel({ gradeFrom: 10, gradeTo: 12 })).toBe('10–bitirgan-sinf')
  })
})

describe('validateBands', () => {
  it("to'g'ri va bo'sh ro'yxat qabul qilinadi", () => {
    expect(validateBands([])).toBeNull()
    expect(validateBands([d(3, 7), d(8, 9)])).toBeNull()
  })

  it('ustma-ust oraliqlar rad etiladi (tartibdan qat\'i nazar)', () => {
    expect(validateBands([d(8, 9), d(3, 8)])).toMatch(/ustma-ust/)
  })

  it('narxsiz oraliq rad etiladi', () => {
    expect(validateBands([d(3, 7, null)])).toMatch(/oylik narx/)
    expect(validateBands([d(3, 7, 0)])).toMatch(/oylik narx/)
  })

  it('teskari oraliq rad etiladi', () => {
    expect(validateBands([d(9, 3)])).toMatch(/noto'g'ri/)
  })
})

describe('toPayloadBands', () => {
  it("tartiblaydi va bo'sh bir dars narxini 0 qiladi", () => {
    expect(toPayloadBands([d(8, 9, 275_000), d(3, 7, 225_000, 20_000)])).toEqual([
      { gradeFrom: 3, gradeTo: 7, price: 225_000, lessonPrice: 20_000 },
      { gradeFrom: 8, gradeTo: 9, price: 275_000, lessonPrice: 0 },
    ])
  })
})

describe('sameBands', () => {
  it('tartibdan qat\'i nazar solishtiradi, undefined = bo\'sh', () => {
    const a = [{ gradeFrom: 3, gradeTo: 7, price: 1, lessonPrice: 0 }, { gradeFrom: 8, gradeTo: 9, price: 2, lessonPrice: 0 }]
    expect(sameBands(a, [...a].reverse())).toBe(true)
    expect(sameBands(undefined, [])).toBe(true)
    expect(sameBands(a, [{ ...a[0], price: 5 }, a[1]])).toBe(false)
  })
})

describe('nextBandStart', () => {
  it('oxirgi oraliqdan keyingi sinf, chegaradan oshmaydi', () => {
    expect(nextBandStart([])).toBe(1)
    expect(nextBandStart([d(3, 7)])).toBe(8)
    expect(nextBandStart([d(10, 12)])).toBe(12)
  })
})
