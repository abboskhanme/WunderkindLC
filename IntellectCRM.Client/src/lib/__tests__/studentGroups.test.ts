import { describe, it, expect } from 'vitest'
import { displayGroupNames, groupsText, primaryGroupName, statesToGroups } from '../studentGroups'

/**
 * MUAMMO: o'quvchi A guruhida MUZLATILGAN, B guruhida o'qiyapti. Ekranlarda `groups[0]` yoki
 * eski `className` ishlatilgani uchun u ESKI (A) guruhi bilan ko'rinardi.
 */
describe('studentGroups — bir nechta a\'zolik', () => {
  const frozenAactiveB = [
    { name: 'A guruh', status: 'frozen' },
    { name: 'B guruh', status: 'active' },
  ]

  it('muzlatilgan guruh KO\'RSATILMAYDI (aktivi bo\'lsa)', () => {
    expect(displayGroupNames(frozenAactiveB)).toEqual(['B guruh'])
  })

  it('hamma a\'zolik muzlatilgan bo\'lsa — o\'shalar ko\'rinadi ("guruhsiz" bo\'lib qolmasin)', () => {
    expect(
      displayGroupNames([
        { name: 'B guruh', status: 'frozen' },
        { name: 'A guruh', status: 'frozen' },
      ]),
    ).toEqual(['A guruh', 'B guruh'])
  })

  it('bir nechta aktiv guruh — HAMMASI, alifbo tartibida va takrorsiz', () => {
    expect(
      displayGroupNames([
        { name: 'B guruh', status: 'active' },
        { name: 'A guruh', status: 'trial' },
        { name: 'B guruh', status: 'active' },
      ]),
    ).toEqual(['A guruh', 'B guruh'])
  })

  it('a\'zolik yo\'q bo\'lsa — eski className zaxirasi', () => {
    expect(groupsText([], 'Eski guruh')).toBe('Eski guruh')
    expect(groupsText(undefined, '', '—')).toBe('—')
    // ⚠️ A'zolik BOR bo'lsa className ISHLATILMAYDI (aynan shu xato edi).
    expect(groupsText(frozenAactiveB, 'A guruh')).toBe('B guruh')
  })

  it('asosiy guruh — MUZLATILGAN emas, AKTIV', () => {
    expect(primaryGroupName(frozenAactiveB, 'A guruh')).toBe('B guruh')
    expect(primaryGroupName([], 'A guruh')).toBe('A guruh')
    // sinov muzlatilgandan ustun
    expect(
      primaryGroupName([
        { name: 'A guruh', status: 'frozen' },
        { name: 'C guruh', status: 'trial' },
      ]),
    ).toBe('C guruh')
  })

  it('groupStates → GroupLike', () => {
    expect(statesToGroups([{ name: 'A guruh', status: 'frozen' }])).toEqual([
      { name: 'A guruh', status: 'frozen' },
    ])
  })
})
