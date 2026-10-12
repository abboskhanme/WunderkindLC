import { describe, expect, it } from 'vitest'
import { activeNavTo, navByRole } from '@/config/navigation'

/** CRM Phase 4 (U4a, SPEC P4.2): «Testlar natijalari» — asosiy «O'quv bo'limi» guruhida, Future'da EMAS. */
const admin = navByRole.admin

interface Node {
  to: string
  label?: string
  perm?: string
  children?: Node[]
}
const flatten = (items: readonly Node[]): Node[] => items.flatMap((i) => [i, ...flatten(i.children ?? [])])

describe("CRM Phase 4 — «Testlar natijalari» menyuda", () => {
  it("asosiy «O'quv bo'limi» guruhida, sahifa kaliti bilan", () => {
    const group = admin.find((i) => i.to === '#oquv-bolimi')
    const item = group?.children?.find((c) => c.to === '/admin/test-results')
    expect(item?.label).toBe('Testlar natijalari')
    expect(item?.perm).toBe('classes.testResults')
  })

  it('Future ichida qolmagan (bir band ikki joyda turmasin)', () => {
    const future = admin.find((i) => i.to === '#future')
    expect(flatten(future?.children ?? []).some((c) => c.to === '/admin/test-results')).toBe(false)
    expect(flatten(admin).filter((c) => c.to === '/admin/test-results')).toHaveLength(1)
  })

  it("test sahifalari ochilganda «O'quv bo'limi» faol", () => {
    expect(activeNavTo(admin, '/admin/test-results')).toBe('#oquv-bolimi')
    expect(activeNavTo(admin, '/admin/test-results/g1/tests/t1')).toBe('#oquv-bolimi')
  })
})
