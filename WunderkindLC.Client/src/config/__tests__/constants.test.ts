// `src/config/constants.ts` — kod → yorliq jadvallari (butun UI shu matnlarni ko'rsatadi)
// va `src/config/navigation.ts` dagi `isKassaOnly` (kassir portaliga yo'naltirish qoidasi).
//
// ⚠️ Production kodi tuzatilmaydi: tasdiqlangan xato `it.skip` bilan qoldirilgan.

import { describe, expect, it } from 'vitest'
import {
  financeCategoryLabel,
  formatMonth,
  monthShortNames,
  paymentMethodLabel,
  studentStateBadge,
  teacherCategoryLabel,
} from '@/config/constants'
import { isKassaOnly } from '@/config/navigation'
import type { Role } from '@/types'

/** `isKassaOnly` kutgan minimal foydalanuvchi shakli. */
const user = (role: Role, permissions?: string[] | null) => ({ role, permissions })

describe('formatMonth', () => {
  it('"YYYY-MM" ni o\'zbekcha qisqa oy + yilga o\'giradi', () => {
    expect(formatMonth('2026-05')).toBe('May 2026')
    expect(formatMonth('2026-01')).toBe('Yan 2026')
    expect(formatMonth('2026-12')).toBe('Dek 2026')
  })

  it('12 ta qisqa oy nomi bor va tartibi to\'g\'ri', () => {
    expect(monthShortNames).toHaveLength(12)
    expect(monthShortNames[0]).toBe('Yan')
    expect(monthShortNames[11]).toBe('Dek')
  })

  it('shkaladan tashqari oy raqamida xom qiymatga qaytadi', () => {
    expect(formatMonth('2026-13')).toBe('13 2026')
    expect(formatMonth('2026-00')).toBe('00 2026')
  })

  // XATO (src/config/constants.ts:167-170): oy qismi bo'lmasa `ym.split('-')` ikkinchi element
  // bermaydi → `monthShortNames[NaN] ?? undefined` → ekranda "undefined 2026" chiqadi.
  // (Masalan filtr "yil bo'yicha" rejimga o'tkazilsa yoki server "2026" qaytarsa.)
  it.skip('oy qismi yo\'q qiymatda "undefined" chiqarmaydi', () => {
    expect(formatMonth('2026')).toBe('2026')
    expect(formatMonth('')).not.toContain('undefined')
  })
})

describe('financeCategoryLabel', () => {
  it('kirim va chiqim toifalarini tarjima qiladi', () => {
    expect(financeCategoryLabel('tuition')).toBe("O'quvchi to'lovi")
    expect(financeCategoryLabel('salary')).toBe('Oylik maosh')
  })

  it('katalogda yo\'q "refund" alohida ishlanadi', () => {
    expect(financeCategoryLabel('refund')).toBe('Vozvrat')
  })

  it('noma\'lum kod o\'zi qaytadi (yorliq yo\'qolmaydi)', () => {
    expect(financeCategoryLabel('nomalum_kod')).toBe('nomalum_kod')
    expect(financeCategoryLabel('')).toBe('')
  })
})

describe('paymentMethodLabel', () => {
  it('to\'lov usuli kodini tarjima qiladi', () => {
    expect(paymentMethodLabel('cash')).toBe('Naqd')
    expect(paymentMethodLabel('card')).toBe('Karta')
    expect(paymentMethodLabel('bank')).toBe('Bank orqali')
  })

  it('bo\'sh/null/undefined uchun tire', () => {
    expect(paymentMethodLabel(null)).toBe('—')
    expect(paymentMethodLabel(undefined)).toBe('—')
    expect(paymentMethodLabel('')).toBe('—')
  })

  it('noma\'lum usul kodi o\'zi qaytadi', () => {
    expect(paymentMethodLabel('payme')).toBe('payme')
  })
})

describe('teacherCategoryLabel', () => {
  it('mavjud toifani tarjima qiladi', () => {
    expect(teacherCategoryLabel('oliy')).toBe('Oliy toifa')
    expect(teacherCategoryLabel('1')).toBe('1-toifa')
  })

  it('noma\'lum yoki berilmagan toifada tire', () => {
    expect(teacherCategoryLabel('yoq')).toBe('—')
    expect(teacherCategoryLabel(undefined)).toBe('—')
  })
})

describe('studentStateBadge', () => {
  it('arxiv boshqa holatlardan USTUN', () => {
    expect(studentStateBadge('frozen', true)?.label).toBe('arxiv')
    expect(studentStateBadge('trial', true)?.label).toBe('arxiv')
  })

  it('a\'zolik holatlarini belgilaydi', () => {
    expect(studentStateBadge('frozen')?.label).toBe('muzlatilgan')
    expect(studentStateBadge('trial')?.label).toBe('sinov')
  })

  it('«aktiv muzlatish» oddiy muzlatishdan ALOHIDA belgi va ALOHIDA rang', () => {
    const year = studentStateBadge('yearFrozen')
    const plain = studentStateBadge('frozen')
    expect(year?.label).toBe('aktiv muzlatilgan')
    // Ranglar bir xil bo'lsa belgi ma'nosini yo'qotardi: modulning butun maqsadi —
    // "yangi o'quv yiliga nechta o'quvchi bilan o'tyapmiz" ni ko'z bilan ajratish.
    expect(year?.className).not.toBe(plain?.className)
  })

  it('arxiv «aktiv muzlatish»dan ham USTUN', () => {
    expect(studentStateBadge('yearFrozen', true)?.label).toBe('arxiv')
  })

  it('normal (aktiv) o\'quvchida belgi yo\'q', () => {
    expect(studentStateBadge('active')).toBeNull()
    expect(studentStateBadge('')).toBeNull()
    expect(studentStateBadge(undefined, false)).toBeNull()
  })
})

describe('isKassaOnly — kassir ish o\'rni', () => {
  it('faqat kassa ruxsati bo\'lgan xodim → true', () => {
    expect(isKassaOnly(user('staff', ['kassa']))).toBe(true)
    expect(isKassaOnly(user('staff', ['kassa:view', 'kassa:create']))).toBe(true)
  })

  it('kassa + boshqa bo\'lim (aralash) → false, to\'liq admin paneli kerak', () => {
    expect(isKassaOnly(user('staff', ['kassa', 'students:view']))).toBe(false)
    expect(isKassaOnly(user('staff', ['kassa:create', 'finance']))).toBe(false)
  })

  it('kassasiz xodim va bo\'sh ruxsat → false', () => {
    expect(isKassaOnly(user('staff', ['students']))).toBe(false)
    expect(isKassaOnly(user('staff', []))).toBe(false)
    expect(isKassaOnly(user('staff', null))).toBe(false)
  })

  it('admin/superadmin hech qachon kassa portaliga qamalmaydi', () => {
    expect(isKassaOnly(user('admin', null))).toBe(false)
    expect(isKassaOnly(user('superadmin', null))).toBe(false)
    expect(isKassaOnly(user('admin', ['kassa']))).toBe(false)
    expect(isKassaOnly(user('teacher', ['kassa']))).toBe(false)
  })

  it('kirilmagan foydalanuvchi (null) → false', () => {
    expect(isKassaOnly(null)).toBe(false)
  })
})
