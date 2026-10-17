import { describe, expect, it } from 'vitest'
import { navByRole, type NavChild } from '../navigation'

/**
 * Egasining TZ'si (2026-10-17, SPEC-backlog F5 N1, "TZ'dagilar"): Kassa → Moliya, Kitoblar sotuvi → O'quv bo'limi,
 * O'qituvchilar kabineti → Boshqaruv (Xodimlar), Instagram marketing → o'z asosiy guruhi, Future hisobotlari → asosiy
 * «Hisobotlar». Ko'chgan band Future'da QOLMAYDI (bir band ikki joyda turmasin).
 */
const admin = navByRole.admin
const flatten = (items: NavChild[]): NavChild[] => items.flatMap((c) => (c.children ? flatten(c.children) : [c]))
const tosOf = (label: string) => flatten(admin.find((i) => i.label === label)!.children ?? []).map((c) => c.to)

describe('F5 N1 — Future modullari asosiy menyuga', () => {
  it.each([
    ['/admin/kassa', 'Moliya'],
    ['/admin/books', "O'quv bo'limi"],
    ['/admin/app/teachers', 'Boshqaruv'],
    ['/admin/marketing/inbox', 'Instagram marketing'],
    ['/admin/marketing/kontent', 'Instagram marketing'],
    ['/admin/hisobotlar', 'Hisobotlar'],
  ])('%s → «%s»', (to, group) => {
    expect(tosOf(group)).toContain(to)
  })

  it("ko'chgan bandlar Future'da qolmagan", () => {
    const future = tosOf('Future')
    for (const to of ['/admin/kassa', '/admin/books', '/admin/app/teachers', '/admin/marketing', '/admin/marketing/inbox', '/admin/hisobotlar'])
      expect(future, to).not.toContain(to)
  })

  it('qolganlar Future\'da turibdi (KPI, kameralar, AI check)', () => {
    const future = tosOf('Future')
    for (const to of ['/admin/boshqaruv/kpi', '/admin/boshqaruv/cameras', '/admin/ai-check']) expect(future).toContain(to)
  })
})
