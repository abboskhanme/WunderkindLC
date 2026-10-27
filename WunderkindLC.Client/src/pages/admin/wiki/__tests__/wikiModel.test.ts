import { describe, expect, it } from 'vitest'
import { defaultBlockSpecs } from '@blocknote/core'
import type { WikiTreeNode } from '@/api/services/wiki'
import { wikiSchema } from '../lib/wikiSchema'
import {
  DEFAULT_FONT_SIZE, WIKI_BLOCK_TYPES, ancestorIds, buildTree, descendantCount, effectiveLocked, formatStamp, isConflictError,
  moveTarget, moveToSectionTarget, parseContent, splitSections, stepFontSize, wikiTabOf,
} from '../lib/wikiModel'

const n = (id: string, parentId = '', order = 0, title = id, locked = false): WikiTreeNode => ({ id, parentId, title, icon: '', order, locked })

// a ─ b ─ c
//   └ d
// e
const nodes = [n('a', '', 0), n('b', 'a', 0), n('c', 'b', 0), n('d', 'a', 1), n('e', '', 1)]

describe('wikiSchema — FAQAT MATN', () => {
  it('sxemada aynan WIKI_BLOCK_TYPES bor (server WikiRules.AllowedBlockTypes bilan bir xil)', () => {
    expect(Object.keys(wikiSchema.blockSpecs).sort()).toEqual([...WIKI_BLOCK_TYPES].sort())
  })

  it('rasm, video, audio, fayl sxemada YO‘Q', () => {
    for (const t of ['image', 'video', 'audio', 'file']) expect(Object.keys(wikiSchema.blockSpecs)).not.toContain(t)
  })

  it('WIKI_BLOCK_TYPES dagi har tur BlockNote da haqiqatan bor (versiya yangilanganda nom o‘zgarsa qizaradi)', () => {
    for (const t of WIKI_BLOCK_TYPES) expect(Object.keys(defaultBlockSpecs)).toContain(t)
  })
})

describe('buildTree', () => {
  it('ota-bola va tartib bo‘yicha', () => {
    const tree = buildTree([...nodes].reverse())
    expect(tree.map((t) => t.node.id)).toEqual(['a', 'e'])
    expect(tree[0].children.map((t) => t.node.id)).toEqual(['b', 'd'])
    expect(tree[0].children[0].children.map((t) => t.node.id)).toEqual(['c'])
  })

  it('otasi topilmagan sahifa yuqoriga chiqadi — yo‘qolmaydi', () => {
    expect(buildTree([n('x', 'yoq')]).map((t) => t.node.id)).toEqual(['x'])
  })

  it('halqa (x→y→x) ham ko‘rsatiladi, cheksiz aylanmaydi', () => {
    const tree = buildTree([n('x', 'y'), n('y', 'x')])
    const ids: string[] = []
    const walk = (items: typeof tree) => items.forEach((i) => { ids.push(i.node.id); walk(i.children) })
    walk(tree)
    expect(ids.sort()).toEqual(['x', 'y'])
  })
})

describe('descendantCount / ancestorIds', () => {
  it('ichki sahifalar soni', () => {
    expect(descendantCount(nodes, 'a')).toBe(3)
    expect(descendantCount(nodes, 'c')).toBe(0)
  })

  it('otalar zanjiri yuqoridan pastga', () => {
    expect(ancestorIds(nodes, 'c')).toEqual(['a', 'b'])
    expect(ancestorIds(nodes, 'a')).toEqual([])
  })
})

describe('moveTarget', () => {
  it('yuqoriga / pastga — chegarada null', () => {
    expect(moveTarget(nodes, 'd', 'up')).toEqual({ parentId: 'a', index: 0 })
    expect(moveTarget(nodes, 'b', 'up')).toBeNull()
    expect(moveTarget(nodes, 'b', 'down')).toEqual({ parentId: 'a', index: 1 })
    expect(moveTarget(nodes, 'd', 'down')).toBeNull()
  })

  it('ichkariga — yuqoridagi qo‘shnining oxiriga', () => {
    expect(moveTarget(nodes, 'd', 'indent')).toEqual({ parentId: 'b', index: 1 })
    expect(moveTarget(nodes, 'a', 'indent')).toBeNull()
  })

  it('tashqariga — otasining keyiniga', () => {
    expect(moveTarget(nodes, 'c', 'outdent')).toEqual({ parentId: 'a', index: 1 })
    expect(moveTarget(nodes, 'b', 'outdent')).toEqual({ parentId: '', index: 1 })
    expect(moveTarget(nodes, 'a', 'outdent')).toBeNull()
  })
})

describe('yordamchilar', () => {
  it('parseContent: bo‘sh/buzuq — undefined', () => {
    expect(parseContent('')).toBeUndefined()
    expect(parseContent('[]')).toBeUndefined()
    expect(parseContent('{buzuq')).toBeUndefined()
    expect(parseContent('[{"type":"paragraph"}]')).toHaveLength(1)
  })

  it('isConflictError faqat 409 + conflict', () => {
    expect(isConflictError({ response: { status: 409, data: { conflict: true } } })).toBe(true)
    expect(isConflictError({ response: { status: 409, data: {} } })).toBe(false)
    expect(isConflictError({ response: { status: 400, data: { conflict: true } } })).toBe(false)
    expect(isConflictError(null)).toBe(false)
  })

  it('formatStamp', () => {
    expect(formatStamp('2026-10-25T16:06:08')).toBe('25.10.2026 16:06')
  })
})

describe('effectiveLocked — serverdagi WikiRules.IsLocked nusxasi', () => {
  const tree = [n('sop', '', 0, 'sop', true), n('ichi', 'sop'), n('nevara', 'ichi'), n('ochiq', '', 1)]
  it('o‘zi, otasi yoki bo‘limi', () => {
    expect(effectiveLocked(tree, 'sop')).toBe(true)
    expect(effectiveLocked(tree, 'nevara')).toBe(true)
    expect(effectiveLocked(tree, 'ochiq')).toBe(false)
    expect(effectiveLocked(tree, '')).toBe(false)
  })
  it('halqaga chidamli', () => {
    expect(effectiveLocked([n('x', 'y'), n('y', 'x')], 'x')).toBe(false)
  })
})

describe('BO‘LIMLAR (papkalar)', () => {
  const sec = (id: string, order = 0, locked = false): WikiTreeNode => ({ ...n(id, '', order, id, locked), isSection: true })
  // a, b — «Mening eslatmalarim»; s1 (ichida x, y), s2 — bo'limlar
  const tree = [n('a', '', 0), n('b', '', 1), sec('s1', 0), sec('s2', 1), n('x', 's1', 0), n('y', 's1', 1)]

  it('splitSections — sahifalar va bo‘limlar alohida, ichidagilari bilan', () => {
    const { pages, sections } = splitSections(buildTree(tree))
    expect(pages.map((t) => t.node.id)).toEqual(['a', 'b'])
    expect(sections.map((t) => t.node.id)).toEqual(['s1', 's2'])
    expect(sections[0].children.map((t) => t.node.id)).toEqual(['x', 'y'])
  })

  it('yuqori darajada bo‘lim va sahifa tartibi aralashmaydi', () => {
    expect(moveTarget(tree, 'b', 'up')).toEqual({ parentId: '', index: 0 })
    expect(moveTarget(tree, 'b', 'down')).toBeNull() // pastida faqat bo'limlar
    expect(moveTarget(tree, 's2', 'up')).toEqual({ parentId: '', index: 0 })
    expect(moveTarget(tree, 'a', 'indent')).toBeNull() // tepasida bo'lim bo'lsa ham uning ichiga EMAS
  })

  it('bo‘lim ichiga/tashqariga surilmaydi', () => {
    expect(moveTarget(tree, 's2', 'indent')).toBeNull()
    expect(moveTarget(tree, 's1', 'outdent')).toBeNull()
  })

  it('bo‘limdan chiqqan sahifa «Mening eslatmalarim» oxiriga', () => {
    expect(moveTarget(tree, 'x', 'outdent')).toEqual({ parentId: '', index: 2 })
  })

  it('moveToSectionTarget — boshqa bo‘lim oxiriga; o‘sha joy yoki bo‘lim — null', () => {
    expect(moveToSectionTarget(tree, 'a', 's1')).toEqual({ parentId: 's1', index: 2 })
    expect(moveToSectionTarget(tree, 'x', '')).toEqual({ parentId: '', index: 2 })
    expect(moveToSectionTarget(tree, 'x', 's1')).toBeNull()
    expect(moveToSectionTarget(tree, 's1', 's2')).toBeNull()
  })

  it('qulflangan bo‘lim ichidagi sahifa ham qulflangan', () => {
    const locked = [sec('s', 0, true), n('p', 's')]
    expect(effectiveLocked(locked, 'p')).toBe(true)
  })
})

describe('shrift o‘lchami', () => {
  it('qadam va chegaralar', () => {
    expect(stepFontSize(DEFAULT_FONT_SIZE, 1)).toBe(17)
    expect(stepFontSize(14, -1)).toBe(14)
    expect(stepFontSize(22, 1)).toBe(22)
    expect(stepFontSize(999, 1)).toBe(17) // noma'lum — standartdan
  })
})

describe('wikiTabOf', () => {
  it('«Xodimlar» — faqat /xodimlar manzillari, qolgani «Eslatmalarim»', () => {
    expect(wikiTabOf('/admin/bilimlar/eslatmalar')).toBe('notes')
    expect(wikiTabOf('/admin/bilimlar/eslatmalar/x')).toBe('notes')
    expect(wikiTabOf('/admin/bilimlar/xodimlar')).toBe('team')
    expect(wikiTabOf('/admin/bilimlar/xodimlar/u1/p1')).toBe('team')
  })
})
