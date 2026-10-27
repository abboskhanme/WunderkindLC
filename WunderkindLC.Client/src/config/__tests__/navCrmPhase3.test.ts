import { describe, expect, it } from 'vitest'
import { navByRole } from '@/config/navigation'

/** «Bog'lanish kerak» navbati — 2026-10-28 dan asosiy menyuda «Service» bandi, «O'quv bo'limi» ostida (egasining talabi). */
const admin = navByRole.admin

interface Node {
  to: string
  children?: Node[]
}
const flatten = (items: readonly Node[]): Node[] => items.flatMap((i) => [i, ...flatten(i.children ?? [])])

describe("«Service» (Bog'lanish kerak) menyuda", () => {
  it("asosiy menyuda alohida band, «O'quv bo'limi» dan keyin", () => {
    const at = admin.findIndex((i) => i.to === '/admin/students/boglanish')
    expect(at).toBeGreaterThan(-1)
    expect(admin[at].label).toBe('Service')
    expect(admin[at].perm).toBe('contacts')
    expect(admin[at - 1].to).toBe('#oquv-bolimi')
  })

  it("«O'quvchilar» guruhida endi yo'q", () => {
    const group = admin.find((i) => i.to === '#oquvchilar')
    expect((group?.children ?? []).some((c) => c.to === '/admin/students/boglanish')).toBe(false)
  })

  it('Future ichida qolmagan (bir band ikki joyda turmasin)', () => {
    const future = admin.find((i) => i.to === '#future')
    expect(flatten(future?.children ?? []).some((c) => c.to === '/admin/students/boglanish')).toBe(false)
    expect(flatten(admin).filter((c) => c.to === '/admin/students/boglanish')).toHaveLength(1)
  })
})

/** CRM Phase 3, U3b (SPEC P3.3): «Davomat xabarlari» — Nazorat guruhida, o'z ruxsati bilan. */
describe('CRM Phase 3 — «Davomat xabarlari» menyuda', () => {
  it('Nazorat guruhida, `students.notices` bilan, bitta joyda', () => {
    const group = admin.find((i) => i.to === '#nazorat')
    const item = (group?.children ?? []).find((c) => c.label === 'Davomat xabarlari')
    expect(item?.to).toBe('/admin/nazorat/davomat-xabarlari')
    expect(item?.perm).toBe('students.notices')
    expect(flatten(admin).filter((c) => c.to === '/admin/nazorat/davomat-xabarlari')).toHaveLength(1)
  })
})
