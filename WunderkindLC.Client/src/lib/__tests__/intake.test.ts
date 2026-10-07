import { describe, expect, it } from 'vitest'
import type { IntakeItem, IntakeItemKey, StudentIntake } from '@/api/services/studentIntake'
import {
  INTAKE_COLUMNS,
  intakeIndex,
  itemState,
  matchesIntake,
  pendingLabels,
  progressTone,
  type IntakeFilter,
} from '../intake'

const KEYS: IntakeItemKey[] = ['profile', 'levelTest', 'contract', 'telegram', 'payment']

function item(key: IntakeItemKey, done: boolean, auto = done): IntakeItem {
  return { key, label: `L-${key}`, done, auto, detail: '', missing: [], mark: null }
}

function row(id: string, doneKeys: IntakeItemKey[], extra: Partial<StudentIntake> = {}): StudentIntake {
  const items = KEYS.map((k) => item(k, doneKeys.includes(k)))
  const done = items.filter((i) => i.done).length
  return {
    studentId: id,
    fullName: `O'quvchi ${id}`,
    phone: '+998-90-123-45-67',
    groups: ['Ingliz A1'],
    memberState: 'trial',
    since: '2026-10-01',
    done,
    total: items.length,
    complete: done === items.length,
    items,
    leadId: null,
    ...extra,
  }
}

const ALL: IntakeFilter = { status: 'all', item: '', q: '' }

describe('qabul cheklisti', () => {
  it('ustunlar serverdagi bandlar tartibida', () => {
    expect(INTAKE_COLUMNS.map((c) => c.key)).toEqual(KEYS)
  })

  it('band holati: avtomatik > qo‘lda > kutilmoqda', () => {
    expect(itemState({ auto: true, done: true })).toBe('auto')
    expect(itemState({ auto: false, done: true })).toBe('manual')
    expect(itemState({ auto: false, done: false })).toBe('pending')
  })

  it('progress rangi', () => {
    expect(progressTone(5, 5)).toBe('complete')
    expect(progressTone(3, 5)).toBe('partial')
    expect(progressTone(2, 5)).toBe('low')
    expect(progressTone(0, 0)).toBe('partial')
  })

  it("holat filtri: to'liq emas / to'liq / hammasi", () => {
    const full = row('a', KEYS)
    const half = row('b', ['profile', 'payment'])
    expect(matchesIntake(full, { ...ALL, status: 'incomplete' })).toBe(false)
    expect(matchesIntake(half, { ...ALL, status: 'incomplete' })).toBe(true)
    expect(matchesIntake(full, { ...ALL, status: 'complete' })).toBe(true)
    expect(matchesIntake(half, { ...ALL, status: 'complete' })).toBe(false)
    expect(matchesIntake(full, ALL)).toBe(true)
  })

  it('band filtri — faqat shu band BAJARILMAGANLAR', () => {
    const noContract = row('a', ['profile', 'payment'])
    const withContract = row('b', ['contract'])
    expect(matchesIntake(noContract, { ...ALL, item: 'contract' })).toBe(true)
    expect(matchesIntake(withContract, { ...ALL, item: 'contract' })).toBe(false)
    // Qo'lda belgilangan band ham "bajarilgan" — filtrdan chiqadi.
    const manual = row('c', [])
    manual.items[2] = item('contract', true, false)
    expect(matchesIntake(manual, { ...ALL, item: 'contract' })).toBe(false)
  })

  it('qidiruv: ism, guruh va telefon raqamlari (kamida 3 raqam)', () => {
    const r = row('x', [], { fullName: 'Aliyev Vali', groups: ['Matematika 5'], phone: '+998 90 123 45 67' })
    expect(matchesIntake(r, { ...ALL, q: 'vali' })).toBe(true)
    expect(matchesIntake(r, { ...ALL, q: 'matem' })).toBe(true)
    expect(matchesIntake(r, { ...ALL, q: '901234567' })).toBe(true)
    expect(matchesIntake(r, { ...ALL, q: '12' })).toBe(false)
    expect(matchesIntake(r, { ...ALL, q: 'karim' })).toBe(false)
  })

  it('indeks va bajarilmagan yorliqlar', () => {
    const idx = intakeIndex([row('a', KEYS), row('b', ['profile'])])
    expect(idx.get('b')?.done).toBe(1)
    expect(intakeIndex(null).size).toBe(0)
    expect(pendingLabels(row('b', ['profile', 'telegram']))).toEqual(['L-levelTest', 'L-contract', 'L-payment'])
  })
})
