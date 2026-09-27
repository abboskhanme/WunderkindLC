import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  LEGACY_SETTINGS_REDIRECTS,
  SETTINGS_GROUPS,
  visibleSettingsGroups,
  type SettingsItem,
} from '../settingsGroups'
import { permLabel } from '../constants'
import { activeNavTo, navByRole, type NavChild } from '../navigation'

/**
 * SOZLAMALAR KATALOGI — edutizim tuzilishi (`config/settingsGroups.ts`). Katalog BIR VAQTDA
 * yon menyuni ham, guruh sahifasini ham quradi, shuning uchun undagi xato ikki joyni buzadi.
 */

const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')
const routeExists = (to: string) => app.includes(`path="${to.split('?')[0].replace(/^\/admin\//, '')}"`)

const allItems = SETTINGS_GROUPS.flatMap((g) => g.items)
const allRows = allItems.flatMap((i) => i.rows ?? [])

describe('sozlamalar katalogi — edutizim tuzilishi', () => {
  it("guruhlar edutizim tartibida (Gamifikatsiya bizda yo'q)", () => {
    expect(SETTINGS_GROUPS.map((g) => g.label)).toEqual([
      'Umumiy sozlamalar',
      'Moliya',
      "O'quv",
      'Sotuv va marketing',
      'Ilova sozlamalari',
    ])
  })

  it("Umumiy sozlamalar birinchi bandi — Funksionallik (edutizimdagi kulrang qatorlar)", () => {
    const first = SETTINGS_GROUPS[0].items[0]
    expect(first.label).toBe('Funksionallik')
    expect(first.kind).toBe('rows')
  })

  it('ruxsat kalitlari ruxsatlar katalogida mavjud', () => {
    for (const x of [...allItems, ...allRows]) {
      if (!x.perm) continue
      expect(permLabel(x.perm), `${'label' in x ? x.label : x.title} → ${x.perm}`).not.toBe(x.perm)
    }
  })

  it("har bir havolaning marshruti App.tsx da HAQIQATAN bor", () => {
    for (const i of allItems.filter((i) => i.kind === 'link')) expect(routeExists(i.to!), i.to).toBe(true)
    for (const r of allRows) if ('to' in r.open) expect(routeExists(r.open.to), r.open.to).toBe(true)
    expect(app).toContain('path="sozlamalar/:group"')
  })

  it('har band o\'z turiga mos to\'ldirilgan', () => {
    const ok = (i: SettingsItem) =>
      (i.kind === 'rows' && (i.rows?.length ?? 0) > 0) ||
      (i.kind === 'panel' && !!i.panel) ||
      (i.kind === 'link' && !!i.to)
    for (const i of allItems) expect(ok(i), i.key).toBe(true)
  })

  it("guruh ichida bo'lim kalitlari takrorlanmaydi (?bolim= bir ma'noli)", () => {
    for (const g of SETTINGS_GROUPS) {
      const keys = g.items.map((i) => i.key)
      expect(new Set(keys).size, g.key).toBe(keys.length)
    }
  })
})

describe('ruxsat bo\'yicha ko\'rinish', () => {
  it("ruxsati yo'q xodim faqat o'z akkaunt sozlamasini ko'radi", () => {
    const groups = visibleSettingsGroups(() => false)
    expect(groups.map((g) => g.key)).toEqual(['umumiy'])
    expect(groups[0].items.map((i) => i.key)).toEqual(['foydalanuvchi'])
  })

  it('admin hamma guruhni ko\'radi', () => {
    expect(visibleSettingsGroups(() => true)).toHaveLength(SETTINGS_GROUPS.length)
  })

  it("jurnal siyosati qatorlari faqat TAHRIRLASH huquqi bilan (oyna faqat saqlash uchun)", () => {
    const viewOnly = visibleSettingsGroups((_p, a) => a === 'view')
    const rows = viewOnly.flatMap((g) => g.items).flatMap((i) => i.rows ?? [])
    expect(rows.some((r) => 'modal' in r.open)).toBe(false)
  })
})

describe('menyu', () => {
  const admin = navByRole.admin
  const settings = admin.find((i) => i.label === 'Sozlamalar')!
  const future = admin.find((i) => i.label === 'Future')!
  const flatten = (xs: NavChild[]): NavChild[] => xs.flatMap((c) => [c, ...flatten(c.children ?? [])])

  it('menyudagi Sozlamalar guruhi katalog bilan BIR XIL', () => {
    expect(settings.children!.map((c) => [c.label, c.to])).toEqual(SETTINGS_GROUPS.map((g) => [g.label, g.to]))
  })

  it("Sozlamalarga ko'chgan bandlar Future'da QOLMAGAN", () => {
    const futureTos = flatten(future.children ?? []).map((c) => c.to)
    for (const moved of ['/admin/landing', '/admin/districts', '/admin/settings/channels', '/admin/settings/azure-speech', '/admin/settings/gemini', '/admin/reasons'])
      expect(futureTos, moved).not.toContain(moved)
  })

  it('guruh sahifasi menyuda "Sozlamalar" ni faol qiladi', () => {
    expect(activeNavTo(admin, '/admin/sozlamalar/moliya')).toBe(settings.to)
  })
})

describe('eski manzillar', () => {
  it("har bir eski /admin/settings/:section mavjud guruh va bo'limga yo'naltiriladi", () => {
    for (const [section, to] of Object.entries(LEGACY_SETTINGS_REDIRECTS)) {
      const url = new URL(to, 'http://x')
      const group = SETTINGS_GROUPS.find((g) => g.to === url.pathname)
      expect(group, section).toBeDefined()
      expect(group!.items.map((i) => i.key), section).toContain(url.searchParams.get('bolim'))
    }
  })
})
