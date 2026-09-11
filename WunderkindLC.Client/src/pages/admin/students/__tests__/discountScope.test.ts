import { describe, it, expect } from 'vitest'
import type { DiscountScopeOption } from '@/api/services/discounts'
import { SCOPE_NONE, appliedGroupLabel, defaultScopeKey, periodInvalid, scopeKey } from '../discountScope'

/** Qamrov varianti — testda faqat `groupId` va `hasActive` muhim. */
const scope = (groupId: string | null, hasActive = false): DiscountScopeOption => ({
  groupId,
  groupName: groupId ? `Guruh ${groupId}` : '',
  courseName: groupId ? `Fan ${groupId}` : '',
  teacherName: '',
  monthlyFee: 500_000,
  hasActive,
  currentCharge: null,
})

/** Server ro'yxatni HAR DOIM «Barcha guruhlar» dan boshlaydi (`DiscountRules` tartibi). */
const blanket = (hasActive = false) => scope(null, hasActive)

describe("defaultScopeKey — standart tanlov FAN, «barcha guruhlar» EMAS", () => {
  it('bitta bo\'sh FAN bo\'lsa — o\'sha fan oldindan tanlanadi', () => {
    expect(defaultScopeKey([blanket(), scope('g1')])).toBe('g1')
  })

  it('bir nechta bo\'sh FAN bo\'lsa — hech narsa tanlanmaydi', () => {
    expect(defaultScopeKey([blanket(), scope('g1'), scope('g2')])).toBe(SCOPE_NONE)
  })

  it('HAMMA fanda chegirma bor — «Barcha guruhlar» TANLANMAYDI (asosiy xato)', () => {
    // Aynan shu holatda ilgari blanket qamrov jimgina tanlab qo'yilardi.
    const scopes = [blanket(), scope('g1', true), scope('g2', true)]
    expect(defaultScopeKey(scopes)).toBe(SCOPE_NONE)
  })

  it('o\'quvchi GURUHSIZ bo\'lsa — «Barcha guruhlar» (yagona mumkin qamrov)', () => {
    expect(defaultScopeKey([blanket()])).toBe('')
  })

  it('guruhsiz o\'quvchida blanket ham band bo\'lsa — hech narsa tanlanmaydi', () => {
    expect(defaultScopeKey([blanket(true)])).toBe(SCOPE_NONE)
  })

  it('qamrov umuman kelmagan bo\'lsa — hech narsa tanlanmaydi', () => {
    expect(defaultScopeKey([])).toBe(SCOPE_NONE)
  })

  it('«tanlanmagan» qiymat «Barcha guruhlar» DAN farq qiladi', () => {
    expect(SCOPE_NONE).not.toBe('')
    expect(scopeKey(null)).toBe('')
    expect(scopeKey('g1')).toBe('g1')
  })
})

describe('periodInvalid — teskari davr', () => {
  it('boshlanish tugashdan KEYIN — noto\'g\'ri', () => {
    expect(periodInvalid('2026-09', '2026-06')).toBe(true)
  })

  it('teng yoki oldin — to\'g\'ri', () => {
    expect(periodInvalid('2026-06', '2026-06')).toBe(false)
    expect(periodInvalid('2026-06', '2026-09')).toBe(false)
  })

  it('bo\'sh (cheklovsiz) davr — to\'g\'ri', () => {
    expect(periodInvalid('', '')).toBe(false)
    expect(periodInvalid('2026-09', '')).toBe(false)
    expect(periodInvalid('', '2026-06')).toBe(false)
  })
})

describe('appliedGroupLabel — bo\'sh nomning IKKI sababi', () => {
  it('guruh nomi bor — o\'sha nom', () => {
    expect(appliedGroupLabel('g1', 'Matematika-1')).toBe('Matematika-1')
  })

  it('guruhsiz hisob (groupId null) — «Barcha guruhlar»', () => {
    expect(appliedGroupLabel(null, '')).toBe('Barcha guruhlar')
  })

  it('guruh O\'CHIRILGAN (id bor, nomi yo\'q) — blanket deb ko\'rsatilmaydi', () => {
    expect(appliedGroupLabel('g9', '')).toBe("Guruh o'chirilgan")
  })
})
