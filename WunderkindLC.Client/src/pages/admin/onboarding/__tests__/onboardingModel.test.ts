// `src/pages/admin/onboarding/onboardingModel.ts` — moslashuv sahifasining sof yordamchilari.
// Kechikish/foiz serverda (OnboardingServiceTests); bu yerda faqat ko'rinish matnlari va
// "tizimdagi akkaunt" tanlagichining kodlanishi.

import { describe, expect, it } from 'vitest'
import {
  daysBetween,
  decodeAccount,
  dueText,
  encodeAccount,
  filters,
  groupPeople,
  progressBarClass,
  statusLabels,
  statusTones,
  todayIso,
} from '@/pages/admin/onboarding/onboardingModel'

describe('todayIso', () => {
  it('MAHALLIY sana (UTC emas) — "yyyy-MM-dd"', () => {
    // Mahalliy 2026-10-06 01:30 — UTC bo'yicha hali 10-05 bo'lishi mumkin, lekin natija mahalliy.
    expect(todayIso(new Date(2026, 9, 6, 1, 30))).toBe('2026-10-06')
    expect(todayIso(new Date(2026, 0, 9))).toBe('2026-01-09')
  })
})

describe('daysBetween', () => {
  it('kunlar farqi', () => {
    expect(daysBetween('2026-10-01', '2026-10-06')).toBe(5)
    expect(daysBetween('2026-10-06', '2026-10-01')).toBe(-5)
    expect(daysBetween('2026-12-30', '2027-01-02')).toBe(3)
  })

  it('buzuq sana — null', () => {
    expect(daysBetween('', '2026-10-01')).toBeNull()
    expect(daysBetween('06.10.2026', '2026-10-01')).toBeNull()
  })
})

describe('dueText', () => {
  const today = '2026-10-06'
  it('bajarilgan qadam muddatidan qat\'i nazar "Bajarildi"', () => {
    expect(dueText({ done: true, dueDate: '2026-09-01' }, today)).toBe('Bajarildi')
  })
  it('kechikkan / bugun / ertaga / qoldi', () => {
    expect(dueText({ done: false, dueDate: '2026-10-03' }, today)).toBe('3 kun kechikdi')
    expect(dueText({ done: false, dueDate: '2026-10-06' }, today)).toBe('Bugun')
    expect(dueText({ done: false, dueDate: '2026-10-07' }, today)).toBe('Ertaga')
    expect(dueText({ done: false, dueDate: '2026-10-16' }, today)).toBe('10 kun qoldi')
  })
  it('muddat noma\'lum bo\'lsa — bo\'sh (yolg\'on "kechikdi" yo\'q)', () => {
    expect(dueText({ done: false, dueDate: '' }, today)).toBe('')
  })
})

describe('progressBarClass', () => {
  it('kechikish ustun, keyin tugaganlik', () => {
    expect(progressBarClass(100, 1)).toBe('bg-red-500')
    expect(progressBarClass(100, 0)).toBe('bg-emerald-500')
    expect(progressBarClass(40, 0)).toBe('bg-brand-500')
  })
})

describe('akkaunt tanlagichi', () => {
  it('kodlash/ochish — faqat bitta maydon to\'ladi', () => {
    expect(encodeAccount('t1', '')).toBe('teacher:t1')
    expect(encodeAccount('', 'u1')).toBe('staff:u1')
    expect(encodeAccount('', '')).toBe('')
    expect(decodeAccount('teacher:t1')).toEqual({ teacherId: 't1', staffUserId: '' })
    expect(decodeAccount('staff:u1')).toEqual({ teacherId: '', staffUserId: 'u1' })
    expect(decodeAccount('')).toEqual({ teacherId: '', staffUserId: '' })
    expect(decodeAccount('boshqa:x')).toEqual({ teacherId: '', staffUserId: '' })
  })

  it('odamlar turi bo\'yicha ajratiladi', () => {
    const g = groupPeople([
      { id: '1', kind: 'teacher', fullName: 'A' },
      { id: '2', kind: 'staff', fullName: 'B' },
      { id: '3', kind: 'teacher', fullName: 'C' },
    ])
    expect(g.teachers.map((p) => p.id)).toEqual(['1', '3'])
    expect(g.staff.map((p) => p.id)).toEqual(['2'])
  })
})

describe('kataloglar', () => {
  it('har holatda yorliq va rang bor', () => {
    for (const s of ['active', 'completed', 'cancelled'] as const) {
      expect(statusLabels[s]).toBeTruthy()
      expect(statusTones[s]).toBeTruthy()
    }
  })
  it('standart filtr — "Jarayonda", "Hammasi" oxirida', () => {
    expect(filters[0].key).toBe('active')
    expect(filters[filters.length - 1].key).toBe('')
    expect(filters.map((f) => f.key)).toContain('overdue')
  })
})
