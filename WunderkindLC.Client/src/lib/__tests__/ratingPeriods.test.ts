import { describe, expect, it } from 'vitest'
import {
  academicYearOf,
  defaultRatingKey,
  fmtScore,
  fmtSolved,
  monthLabel,
  quarterLabel,
  rankDelta,
  ratingKeyOptions,
  ratingPeriodLabel,
  scoreFormulaText,
  shiftMonth,
  shortMonth,
  testKindLabel,
  yearLabel,
} from '../ratingPeriods'

const oct12 = new Date(2026, 9, 12)

describe('reyting davrlari', () => {
  it("standart kalitlar: oy va 3 oy — joriy oy, o'quv yili — sentabrdan", () => {
    expect(defaultRatingKey('month', oct12)).toBe('2026-10')
    expect(defaultRatingKey('quarter', oct12)).toBe('2026-10')
    expect(defaultRatingKey('year', oct12)).toBe('2026')
    expect(academicYearOf(new Date(2026, 7, 31))).toBe(2025) // 31-avgust — o'tgan o'quv yili
    expect(academicYearOf(new Date(2026, 8, 1))).toBe(2026)  // 1-sentabr — yangisi
  })

  it("yorliqlar server bilan bir xil", () => {
    expect(monthLabel('2026-10')).toBe('Oktabr 2026')
    expect(quarterLabel('2026-10')).toBe('Avgust – Oktabr 2026')
    expect(quarterLabel('2026-01')).toBe('Noyabr 2025 – Yanvar 2026')
    expect(yearLabel(2026)).toBe("2026/27 o'quv yili")
    expect(yearLabel(2099)).toBe("2099/00 o'quv yili")
    expect(ratingPeriodLabel({ kind: 'quarter', key: '2026-10' })).toBe('Avgust – Oktabr 2026')
    expect(shortMonth('2026-09')).toBe('Sen')
  })

  it("oy siljitish yil chegarasidan o'tadi", () => {
    expect(shiftMonth('2026-01', -1)).toBe('2025-12')
    expect(shiftMonth('2026-11', 3)).toBe('2027-02')
    expect(shiftMonth('2026-10', -24)).toBe('2024-10')
  })

  it("tanlash ro'yxati eng yangisidan, kelajak yo'q", () => {
    const months = ratingKeyOptions('month', oct12)
    expect(months).toHaveLength(24)
    expect(months[0]).toEqual({ key: '2026-10', label: 'Oktabr 2026' })
    expect(months[23].key).toBe('2024-11')
    expect(ratingKeyOptions('quarter', oct12)[0].label).toBe('Avgust – Oktabr 2026')
    const years = ratingKeyOptions('year', oct12)
    expect(years[0]).toEqual({ key: '2026', label: "2026/27 o'quv yili" })
    expect(years).toHaveLength(6)
  })

  it("o'rin o'zgarishi: raqam kichraysa — ko'tarilgan", () => {
    expect(rankDelta(12, 15)).toEqual({ dir: 'up', n: 3 })
    expect(rankDelta(5, 3)).toEqual({ dir: 'down', n: 2 })
    expect(rankDelta(4, 4)).toEqual({ dir: 'same', n: 0 })
    expect(rankDelta(null, 4)).toBeNull()
    expect(rankDelta(4, null)).toBeNull()
  })

  it('raqamlar formati', () => {
    expect(fmtScore(84.6)).toBe('84.6')
    expect(fmtScore(90)).toBe('90')
    expect(fmtScore(null)).toBe('—')
    expect(fmtSolved(25, 30)).toBe('25/30')
    expect(fmtSolved(7.5, 10)).toBe('7.5/10')
  })

  it('reyting bali izohi — faqat bor komponentlar', () => {
    expect(scoreFormulaText(88, 79, 60)).toBe('Testlar 88% × 60% + Ball 79 × 40%')
    expect(scoreFormulaText(88, null, 60)).toBe('Faqat testlar: 88%')
    expect(scoreFormulaText(null, 79, 60)).toBe('Faqat ball: 79')
    expect(scoreFormulaText(null, null, 60)).toBe("Bu davrda test ham, ball ham yo'q")
  })

  it('test turi yorlig\'i', () => {
    expect(testKindLabel('monthly')).toBe('Oylik')
    expect(testKindLabel('interim')).toBe('Oraliq')
    expect(testKindLabel('')).toBe('')
  })
})

import { ratingWeights, scoreFormulaText as formula, weightsLabel } from '@/lib/ratingPeriods'

describe('uy vazifasi ulushi (SPEC-backlog A3)', () => {
  it('ulushlar server bilan bir xil: uy vazifasi testlar bilan birga 100 dan oshmaydi', () => {
    expect(ratingWeights(50, 20)).toEqual({ test: 50, homework: 20, ball: 30 })
    expect(ratingWeights(90, 30)).toEqual({ test: 90, homework: 10, ball: 0 })
    expect(weightsLabel(60)).toBe('testlar 60% + ball 40%')
    expect(weightsLabel(50, 20)).toBe('testlar 50% + ball 30% + uy vazifasi 20%')
  })

  it("ulush 0 bo'lsa izoh avvalgidek, aks holda uy vazifasi qo'shiladi", () => {
    expect(formula(88, 79, 60, 75, 0)).toBe('Testlar 88% × 60% + Ball 79 × 40%')
    expect(formula(88, 79, 50, 75, 20)).toBe('Testlar 88% × 50% + Ball 79 × 30% + Uy vazifasi 75% × 20%')
    expect(formula(null, null, 50, 75, 20)).toBe('Faqat uy vazifasi: 75%')
  })
})
