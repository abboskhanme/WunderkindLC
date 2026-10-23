import { describe, expect, it } from 'vitest'
import { navByRole } from '@/config/navigation'

/** CRM Phase 3 (SPEC P3.5): «Bog'lanish kerak» — asosiy «O'quvchilar» guruhida, Future'da EMAS. */
const admin = navByRole.admin

interface Node {
  to: string
  children?: Node[]
}
const flatten = (items: readonly Node[]): Node[] => items.flatMap((i) => [i, ...flatten(i.children ?? [])])

describe("CRM Phase 3 — «Bog'lanish kerak» menyuda", () => {
  it("asosiy «O'quvchilar» guruhida, «Yangi o'quvchilar» dan keyin", () => {
    const group = admin.find((i) => i.to === '#oquvchilar')
    const labels = (group?.children ?? []).map((c) => c.label)
    const at = labels.indexOf("Bog'lanish kerak")
    expect(at).toBeGreaterThan(-1)
    expect(labels[at - 1]).toBe("Yangi o'quvchilar")
    expect(group?.children?.[at]?.to).toBe('/admin/students/boglanish')
    expect(group?.children?.[at]?.perm).toBe('contacts')
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
