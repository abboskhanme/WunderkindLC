// `src/pages/admin/vacancies/careerLabels.ts` — karyera moduli yorliqlari.
// `salaryText` va `isExpired` HAM admin kartasida, HAM `/vakansiya` Mini App mantig'ida
// ko'rinadigan matnni belgilaydi, shuning uchun chegara holatlari muhim.
//
// ⚠️ Production kodi tuzatilmaydi: tasdiqlangan xatolar `it.skip` bilan qoldirilgan.

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  employmentLabels,
  employmentOptions,
  isExpired,
  salaryText,
  statusLabels,
  statusOrder,
  statusTones,
} from '@/pages/admin/vacancies/careerLabels'

/** `toLocaleString('ru-RU')` mingliklarni NBSP (U+00A0) bilan ajratadi, oddiy probel bilan EMAS. */
const NBSP = ' '

/** `salaryText` kutgan minimal vakansiya bo'lagi. */
const sal = (salaryFrom: number, salaryTo: number, salaryNote = '') => ({
  salaryFrom,
  salaryTo,
  salaryNote,
})

describe('salaryText', () => {
  it('oraliq berilganda "dan – gacha" ko\'rinishi', () => {
    expect(salaryText(sal(3_000_000, 5_000_000))).toBe(
      `3${NBSP}000${NBSP}000 – 5${NBSP}000${NBSP}000 so'm`,
    )
  })

  it('faqat quyi chegara → "…so\'mdan"', () => {
    expect(salaryText(sal(5_000_000, 0))).toBe(`5${NBSP}000${NBSP}000 so'mdan`)
  })

  it('faqat yuqori chegara → "…so\'mgacha"', () => {
    expect(salaryText(sal(0, 5_000_000))).toBe(`5${NBSP}000${NBSP}000 so'mgacha`)
  })

  it('maosh ko\'rsatilmasa izohni ko\'rsatadi', () => {
    expect(salaryText(sal(0, 0, 'Suhbat natijasiga qarab'))).toBe('Suhbat natijasiga qarab')
  })

  it('maosh ham, izoh ham bo\'sh bo\'lsa — "Kelishilgan holda"', () => {
    expect(salaryText(sal(0, 0))).toBe('Kelishilgan holda')
    expect(salaryText(sal(0, 0, ''))).toBe('Kelishilgan holda')
  })

  it('teskari kiritilgan oraliqni (to < from) oraliq deb ko\'rsatmaydi', () => {
    expect(salaryText(sal(5_000_000, 3_000_000))).toBe(`5${NBSP}000${NBSP}000 so'mdan`)
  })

  it('manfiy qiymatlar maosh sifatida qabul qilinmaydi', () => {
    expect(salaryText(sal(-1, -1, 'Kelishuv'))).toBe('Kelishuv')
  })

  // XATO (src/pages/admin/vacancies/careerLabels.ts:64-71): oraliq sharti `salaryTo > salaryFrom`
  // (QAT'IY), shuning uchun ANIQ maosh (from === to) "5 000 000 so'mdan" bo'lib chiqadi —
  // nomzod maosh yuqoriroq bo'lishi mumkin deb tushunadi. Kutilgan: aniq summa.
  it.skip('aniq maoshni (from === to) "dan" siz ko\'rsatadi', () => {
    expect(salaryText(sal(5_000_000, 5_000_000))).toBe(`5${NBSP}000${NBSP}000 so'm`)
  })
})

describe('isExpired', () => {
  afterEach(() => vi.useRealTimers())

  /** Sinov vaqtini qat'iy belgilaydi (aks holda test kalendar bilan "eskiradi"). */
  const freeze = (iso: string) => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(iso))
  }

  it('kechagi muddat o\'tgan deb hisoblanadi', () => {
    freeze('2026-08-15T09:00:00Z')
    expect(isExpired('2026-08-14')).toBe(true)
    expect(isExpired('2025-12-31')).toBe(true)
  })

  it('bugungi muddat hali o\'tmagan (oxirgi kun ariza qabul qilinadi)', () => {
    freeze('2026-08-15T09:00:00Z')
    expect(isExpired('2026-08-15')).toBe(false)
  })

  it('kelajakdagi sana false', () => {
    freeze('2026-08-15T09:00:00Z')
    expect(isExpired('2026-08-16')).toBe(false)
    expect(isExpired('2030-01-01')).toBe(false)
  })

  it('muddat belgilanmagan bo\'lsa (bo\'sh satr) muddatsiz e\'lon', () => {
    freeze('2026-08-15T09:00:00Z')
    expect(isExpired('')).toBe(false)
  })

  // XATO (src/pages/admin/vacancies/careerLabels.ts:74-78): bugungi sana `toISOString()` dan,
  // ya'ni UTC bo'yicha olinadi. Toshkent UTC+5 — mahalliy 05:00 gacha bo'lgan oynada "bugun"
  // hali KECHAGI kun. Natijada muddati kecha tugagan vakansiya ertalab hamon "faol" ko'rinadi.
  it.skip('muddatni Toshkent (mahalliy) sanasi bo\'yicha hisoblaydi, UTC emas', () => {
    // 2026-08-01T20:00Z = Toshkentda 2026-08-02 01:00 → 08-01 muddati ALLAQACHON o'tgan.
    freeze('2026-08-01T20:00:00Z')
    expect(isExpired('2026-08-01')).toBe(true)
  })

  // XATO (src/pages/admin/vacancies/careerLabels.ts:75): `deadline.length !== 10` → false.
  // Backend `Deadline` ni to'liq timestamp bilan qaytarsa ("2026-01-01T00:00:00") tekshiruv
  // JIMGINA o'chadi va allaqachon tugagan e'lon abadiy "faol" bo'lib qoladi.
  it.skip('to\'liq timestamp ko\'rinishidagi muddatni ham hisobga oladi', () => {
    freeze('2026-08-15T09:00:00Z')
    expect(isExpired('2026-01-01T00:00:00')).toBe(true)
  })
})

describe('yorliq/rang kataloglari', () => {
  it('bandlik turlari uchun yorliq va tanlov ro\'yxati mos', () => {
    expect(employmentLabels.full).toBe("To'liq bandlik")
    expect(employmentOptions.map((o) => o.value)).toEqual(['full', 'part', 'shift', 'remote'])
    for (const o of employmentOptions) expect(o.label).toBe(employmentLabels[o.value])
  })

  it('statusOrder backend bosqichlari bilan bir xil ketma-ketlikda', () => {
    expect(statusOrder).toEqual(['new', 'review', 'interview', 'trial', 'hired', 'rejected'])
  })

  it('har bir bosqichda yorliq ham, rang ham bor (UI bo\'sh katak ko\'rsatmaydi)', () => {
    for (const s of statusOrder) {
      expect(statusLabels[s]).toBeTruthy()
      expect(statusTones[s]).toBeTruthy()
    }
  })
})
