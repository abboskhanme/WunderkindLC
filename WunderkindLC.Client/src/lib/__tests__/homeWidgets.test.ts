import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { permLabel } from '@/config/constants'
import { HOME_CARDS } from '@/lib/homeCards'
import {
  HOME_BLOCKS,
  HOME_CARD_KEYS,
  allowedBlocks,
  defaultHomeLayout,
  initialHomeLayout,
  normalizeHomeLayout,
  swapItems,
  toggleItem,
  visibleBlocks,
  visibleCards,
  type WidgetAccess,
} from '@/lib/homeWidgets'

const all: WidgetAccess = { can: () => true, isSuperAdmin: true }
const admin: WidgetAccess = { can: () => true, isSuperAdmin: false }
const staff = (keys: string[]): WidgetAccess => ({ can: (p) => keys.includes(p), isSuperAdmin: false })

describe('standart', () => {
  it("bugungi sahifa bilan bir xil: 12 kartochka + kassalar + jadval", () => {
    const d = defaultHomeLayout()
    expect(d.cards.map((c) => c.key)).toEqual(HOME_CARDS.map((c) => c.key))
    expect(d.cards.every((c) => c.visible)).toBe(true)
    expect(d.cardsVisible).toBe(true)
    expect(visibleBlocks(d, all).map((b) => b.key)).toEqual(['cashboxes', 'schedule'])
  })

  it("server katalogi (HomeLayoutRules.cs) bilan kalitlar AYNAN bir xil", () => {
    const src = readFileSync(
      fileURLToPath(new URL('../../../../WunderkindLC.Application/Services/HomeLayoutRules.cs', import.meta.url)),
      'utf8',
    )
    const cardsBlock = src.slice(src.indexOf('CardKeys ='), src.indexOf('];', src.indexOf('CardKeys =')))
    expect([...cardsBlock.matchAll(/"(\w+)"/g)].map((m) => m[1])).toEqual(HOME_CARD_KEYS)
    const blocksBlock = src.slice(src.indexOf('BlockKeys ='), src.indexOf('];', src.indexOf('BlockKeys =')))
    expect([...blocksBlock.matchAll(/\("(\w+)", (true|false)\)/g)].map((m) => [m[1], m[2] === 'true'])).toEqual(
      HOME_BLOCKS.map((b) => [b.key, b.defaultVisible]),
    )
  })

  it("qurilgan bloklarning ruxsat kaliti katalogda bor", () => {
    for (const b of HOME_BLOCKS.filter((x) => x.available && x.perm))
      expect(permLabel(b.perm!), `${b.key} → ${b.perm}`).not.toBe(b.perm)
  })
})

describe('normallashtirish (server bilan bir xil)', () => {
  it("noma'lum va takror kalit tashlanadi, yetishmagan oxiriga qo'shiladi", () => {
    const l = normalizeHomeLayout({
      version: 1,
      cardsVisible: false,
      cards: [{ key: 'debtors', visible: false }, { key: 'hack', visible: true }, { key: 'debtors', visible: true }],
      blocks: [{ key: 'myTasks', visible: true }, { key: 'zzz', visible: true }],
    })
    expect(l.cardsVisible).toBe(false)
    expect(l.cards[0]).toEqual({ key: 'debtors', visible: false })
    expect(l.cards).toHaveLength(12)
    expect(l.blocks.map((b) => b.key).slice(0, 2)).toEqual(['myTasks', 'cashboxes'])
    expect(l.blocks.find((b) => b.key === 'cashboxes')!.visible).toBe(true)
  })

  it("buzuq yoki boshqa versiya — standart", () => {
    expect(normalizeHomeLayout(null)).toEqual(defaultHomeLayout())
    expect(normalizeHomeLayout({ version: 2 })).toEqual(defaultHomeLayout())
  })

  it("server sozlamasi yo'q bo'lsa eski brauzerdagi «Statistika» bir marta o'tadi", () => {
    expect(initialHomeLayout(null, false).cardsVisible).toBe(false)
    expect(initialHomeLayout(null, null).cardsVisible).toBe(true)
    expect(initialHomeLayout({ ...defaultHomeLayout(), cardsVisible: true }, false).cardsVisible).toBe(true)
  })
})

describe('ruxsat filtri', () => {
  it("kassalar faqat superadmin; boshqa bloklar o'z ruxsati bilan; qurilmaganlari yo'q", () => {
    const layout = { ...defaultHomeLayout(), blocks: defaultHomeLayout().blocks.map((b) => ({ ...b, visible: true })) }
    expect(visibleBlocks(layout, admin).map((b) => b.key)).not.toContain('cashboxes')
    expect(visibleBlocks(layout, all).map((b) => b.key)).toEqual(
      ['cashboxes', 'schedule', 'lessonsMonitor', 'myTasks', 'contactsToday', 'aiAnalysis'],
    )
    expect(visibleBlocks(layout, staff(['tasks.board', 'contacts'])).map((b) => b.key)).toEqual(['myTasks', 'contactsToday'])
    // Keyingi bosqich bloklari (absentNotices, redList) — hali ko'rinmaydi.
    expect(allowedBlocks(layout, all).map((b) => b.key)).not.toContain('redList')
  })

  it("kartochkalar sozlama TARTIBIDA va ruxsat bo'yicha", () => {
    const layout = normalizeHomeLayout({
      version: 1, cardsVisible: true,
      cards: [{ key: 'groups', visible: true }, { key: 'orders', visible: false }, { key: 'debtors', visible: true }],
      blocks: [],
    })
    const keys = visibleCards(layout, staff(['classes.list', 'students.list'])).map((c) => c.key)
    expect(keys[0]).toBe('groups')
    expect(keys[1]).toBe('debtors')
    expect(keys).not.toContain('orders')
  })
})

describe('tartiblash', () => {
  it("▲/▼ — qo'shni bilan o'rin almashadi; yoqish/o'chirish", () => {
    const d = defaultHomeLayout()
    const swapped = swapItems(d.blocks, 'schedule', 'cashboxes')
    expect(swapped.map((b) => b.key).slice(0, 2)).toEqual(['schedule', 'cashboxes'])
    expect(swapItems(d.blocks, 'schedule', 'yoq')).toBe(d.blocks)
    expect(toggleItem(d.blocks, 'myTasks').find((b) => b.key === 'myTasks')!.visible).toBe(true)
  })
})
