import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { adminPermissions } from '../constants'
import { navByRole, type NavChild, type NavItem } from '../navigation'

/**
 * Kanban «Topshiriqlar» (CRM Phase 5 da «Boshqaruv» ga qaytgan doska/ro'yxat/kalendar/nazorat) 2026-10-21 da
 * TAG-TUGI BILAN olib tashlandi — o'rniga HR topshiriqlari (`docs/SPEC-hr-tasks.md`). Menyuda ham, ruxsat
 * katalogida ham qolmasin; eski manzillar (xatcho'plar) HR topshiriqlariga redirect bo'ladi.
 */
const admin = navByRole.admin

function flatTos(items: (NavItem | NavChild)[] | undefined): string[] {
  return (items ?? []).flatMap((c) => [c.to, ...flatTos(c.children)])
}

describe('Kanban «Topshiriqlar» olib tashlangan', () => {
  it('menyuda eski /admin/topshiriqlar bandi YO\'Q', () => {
    expect(flatTos(admin).filter((to) => to.startsWith('/admin/topshiriqlar'))).toEqual([])
  })

  it('ruxsat katalogida `tasks` bo\'limi YO\'Q', () => {
    const keys = adminPermissions.flatMap((p) => [p.key, ...(p.pages ?? []).map((pg) => pg.key)])
    expect(keys.filter((k) => k === 'tasks' || k.startsWith('tasks.'))).toEqual([])
  })

  it('eski manzillar HR topshiriqlariga redirect bo\'ladi', () => {
    const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')
    expect(app).toContain('path="topshiriqlar/*" element={<Navigate to="/admin/hr/topshiriqlar" replace />}')
    expect(app).toContain('path="boshqaruv/staff-tasks" element={<Navigate to="/admin/hr/topshiriqlar" replace />}')
    expect(app).not.toContain('pages/admin/tasks/')
  })
})
