import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { activeNavTo, navByRole, type NavChild, type NavItem } from '../navigation'

/**
 * CRM Phase 5 menyu o'zgarishi: «Topshiriqlar» Future → Boshqaruv dan ASOSIY «Boshqaruv» guruhiga
 * qaytdi («Rollar» dan keyin). Eski manzillar (`boshqaruv/staff-tasks`, `topshiriqlar/kunlik`) redirect
 * bo'lib ishlayveradi.
 */
const admin = navByRole.admin

function group(items: NavItem[], label: string): NavItem {
  const g = items.find((i) => i.label === label)
  expect(g, label).toBeTruthy()
  return g!
}

function flatTos(children: NavChild[] | undefined): string[] {
  return (children ?? []).flatMap((c) => [c.to, ...flatTos(c.children)])
}

describe('CRM Phase 5 — Topshiriqlar menyuda', () => {
  it("asosiy «Boshqaruv» guruhida, «Rollar» dan keyin", () => {
    const boshqaruv = group(admin, 'Boshqaruv')
    const labels = (boshqaruv.children ?? []).map((c) => c.label)
    const i = labels.indexOf('Topshiriqlar')
    expect(i).toBeGreaterThan(-1)
    expect(labels[i - 1]).toBe('Rollar')
    expect(boshqaruv.children![i]).toMatchObject({ to: '/admin/topshiriqlar', perm: 'tasks' })
  })

  it('Future guruhida endi YO\'Q', () => {
    expect(flatTos(group(admin, 'Future').children)).not.toContain('/admin/topshiriqlar')
  })

  it("topshiriqlar sahifalarida «Boshqaruv» bandi faol", () => {
    for (const p of ['/admin/topshiriqlar', '/admin/topshiriqlar/royxat', '/admin/topshiriqlar/nazorat'])
      expect(activeNavTo(admin, p)).toBe('#boshqaruv')   // guruh ochiladi, Future emas
  })

  it('eski manzillar redirect bo\'lib qolgan', () => {
    const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')
    expect(app).toContain('path="boshqaruv/staff-tasks" element={<Navigate to="/admin/topshiriqlar" replace />}')
    expect(app).toContain('path="topshiriqlar/kunlik" element={<Navigate to="/admin/topshiriqlar" replace />}')
  })
})
