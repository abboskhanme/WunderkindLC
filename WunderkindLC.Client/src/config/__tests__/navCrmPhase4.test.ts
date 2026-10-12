import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { activeNavTo, mainReportRoutes, navByRole, type NavChild } from '../navigation'
import { allReports, navReports, visibleReportGroups } from '../reports'
import { SETTINGS_GROUPS, visibleSettingsGroups } from '../settingsGroups'
import { permLabel } from '../constants'

/**
 * CRM 4-bosqich (U4c) — «Reyting» menyuda, hisobotlar katalogida va sozlamalarda
 * (docs/SPEC-crm-upgrade.md P4.4, §8.3: mavjud menyu testlari o'zgartirilmaydi — yangi fayl).
 */
const flatten = (xs: NavChild[]): NavChild[] => xs.flatMap((c) => [c, ...flatten(c.children ?? [])])
const admin = navByRole.admin
const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')

describe('Reyting — menyu va hisobotlar', () => {
  it("asosiy «Hisobotlar» guruhida, Future'da EMAS", () => {
    const reports = admin.find((i) => i.label === 'Hisobotlar')!
    const item = reports.children!.find((c) => c.to === '/admin/hisobotlar/reyting')
    expect(item?.label).toBe('Reyting')
    expect(item?.perm).toBe('classes.rating')
    expect(mainReportRoutes.has('/admin/hisobotlar/reyting')).toBe(true)

    const future = admin.find((i) => i.label === 'Future')!
    expect(flatten(future.children ?? []).map((c) => c.to)).not.toContain('/admin/hisobotlar/reyting')
  })

  it("katalogda (hub) — sof hisobot, menyuga tushadi, sahifa kaliti bilan", () => {
    const r = allReports.find((x) => x.to === '/admin/hisobotlar/reyting')!
    expect(r.perm).toBe('classes.rating')
    expect(r.inNav).toBe(true)
    expect(navReports).toContain(r)
    expect(permLabel('classes.rating')).toBe('Guruhlar → Reyting')
    // Kalitsiz xodim hub'da ko'rmaydi, kalit bilan ko'radi.
    const visible = (perm: string) =>
      visibleReportGroups((p) => p === perm, false).flatMap((g) => g.items).map((i) => i.to)
    expect(visible('classes.rating')).toContain('/admin/hisobotlar/reyting')
    expect(visible('students.list')).not.toContain('/admin/hisobotlar/reyting')
  })

  it("marshrut App.tsx da sahifa kaliti bilan darvozalangan", () => {
    expect(app).toContain('path="hisobotlar/reyting" element={<RequirePerm perm="classes.rating">')
  })

  it("sahifa ochilganda menyuda «Hisobotlar» faol", () => {
    const reports = admin.find((i) => i.label === 'Hisobotlar')!
    expect(activeNavTo(admin, '/admin/hisobotlar/reyting')).toBe(reports.to)
  })
})

describe('Reyting — sozlamalar', () => {
  it("O'quv guruhining BIRINCHI bandi, oyna faqat tahrir huquqi bilan", () => {
    const oquv = SETTINGS_GROUPS.find((g) => g.key === 'oquv')!
    const first = oquv.items[0]
    expect(first.label).toBe('Reyting')
    const row = first.rows![0]
    expect(row.open).toEqual({ modal: 'rating' })
    expect(row.action).toBe('edit')
    expect(row.perm).toBe('classes.rating')

    const keys = (can: (p: string, a: 'view' | 'edit') => boolean) =>
      visibleSettingsGroups(can).flatMap((g) => g.items).map((i) => i.key)
    expect(keys((p, a) => p === 'classes.rating' && a === 'view')).not.toContain('reyting')
    expect(keys((p) => p === 'classes.rating')).toContain('reyting')
  })
})
