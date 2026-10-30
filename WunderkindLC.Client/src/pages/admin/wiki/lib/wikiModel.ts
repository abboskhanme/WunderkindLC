import type { WikiTreeNode } from '@/api/services/wiki'

/**
 * BILIMLAR BAZASI — sof yordamchilar (komponentsiz, testlangan: `__tests__/wikiModel.test.ts`).
 * Qoidalar: `.claude/rules/wiki.md`.
 */

/**
 * Ruxsat etilgan blok turlari — FAQAT MATN. Serverdagi `WikiRules.AllowedBlockTypes` bilan AYNAN
 * bir xil (server boshqa turni 400 bilan rad etadi). `wikiSchema.ts` shu ro'yxatdan quriladi va
 * test ikkalasining mosligini qulflaydi.
 */
export const WIKI_BLOCK_TYPES = [
  'paragraph', 'heading', 'quote', 'bulletListItem', 'numberedListItem', 'checkListItem',
  'toggleListItem', 'codeBlock', 'table', 'divider',
] as const

/** Avtosaqlash kechikishi: yozish to'xtagandan shuncha ms keyin saqlanadi. */
export const AUTOSAVE_MS = 1200

export interface TreeItem {
  node: WikiTreeNode
  children: TreeItem[]
}

const byOrder = (a: WikiTreeNode, b: WikiTreeNode) => a.order - b.order || a.title.localeCompare(b.title)

/**
 * Tekis ro'yxatdan daraxt. Otasi topilmagan sahifa (buzuq yozuv) YUQORI darajaga chiqadi —
 * jimgina yo'qolmasin. Halqa (A→B→A) ham yuqoriga chiqariladi.
 */
export function buildTree(nodes: WikiTreeNode[]): TreeItem[] {
  const ids = new Set(nodes.map((n) => n.id))
  const kids = new Map<string, WikiTreeNode[]>()
  for (const n of nodes) {
    const parent = n.parentId && ids.has(n.parentId) && n.parentId !== n.id ? n.parentId : ''
    const list = kids.get(parent) ?? []
    list.push(n)
    kids.set(parent, list)
  }
  const seen = new Set<string>()
  const build = (parentId: string): TreeItem[] =>
    (kids.get(parentId) ?? [])
      .slice()
      .sort(byOrder)
      .filter((n) => !seen.has(n.id) && seen.add(n.id))
      .map((n) => ({ node: n, children: build(n.id) }))
  const roots = build('')
  // Halqadagi sahifalar ildizdan yetib bo'lmaydi — ularni ham ko'rsatamiz.
  for (const n of nodes.slice().sort(byOrder))
    if (!seen.has(n.id)) {
      seen.add(n.id)
      roots.push({ node: n, children: build(n.id) })
    }
  return roots
}

/**
 * Chap panel uchun: yuqori darajadagi daraxtni «Mening eslatmalarim» sahifalari va BO'LIMLARGA ajratadi
 * (bo'limlar o'z tartibida, har birining ichidagi sahifalar bilan).
 */
export function splitSections(tree: TreeItem[]): { pages: TreeItem[]; sections: TreeItem[] } {
  return {
    pages: tree.filter((t) => !t.node.isSection),
    sections: tree.filter((t) => t.node.isSection),
  }
}

/**
 * Sahifani boshqa bo'limga (yoki `""` — «Mening eslatmalarim» ga) ko'chirish nishoni — o'sha joyning OXIRIGA.
 * Hozir o'sha joyda turgan bo'lsa yoki sahifa bo'limning o'zi bo'lsa — `null`.
 */
export function moveToSectionTarget(nodes: WikiTreeNode[], id: string, sectionId: string): { parentId: string; index: number } | null {
  const node = nodes.find((n) => n.id === id)
  if (!node || node.isSection || node.parentId === sectionId) return null
  return { parentId: sectionId, index: nodes.filter((n) => n.parentId === sectionId && !n.isSection).length }
}

/** Sahifaning ichki sahifalari soni (o'zisiz) — o'chirishdan oldin ogohlantirish uchun. */
export function descendantCount(nodes: WikiTreeNode[], id: string): number {
  const result = new Set<string>([id])
  let grew = true
  while (grew) {
    grew = false
    for (const n of nodes)
      if (!result.has(n.id) && result.has(n.parentId)) {
        result.add(n.id)
        grew = true
      }
  }
  return result.size - 1
}

/** Sahifaga olib boruvchi otalar zanjiri (yuqoridan pastga), sahifaning o'zisiz. */
export function ancestorIds(nodes: WikiTreeNode[], id: string): string[] {
  const byId = new Map(nodes.map((n) => [n.id, n]))
  const chain: string[] = []
  let cur = byId.get(id)?.parentId ?? ''
  while (cur && byId.has(cur) && !chain.includes(cur)) {
    chain.unshift(cur)
    cur = byId.get(cur)?.parentId ?? ''
  }
  return chain
}

export type MoveDir = 'up' | 'down' | 'indent' | 'outdent'

/**
 * Chap paneldagi ⋯ menyusi amallari uchun YANGI joy (`PUT pages/{id}/move` ga beriladi):
 * - `up` / `down` — shu ota ichida bitta yuqoriga/pastga;
 * - `indent` — yuqoridagi qo'shnisining ichiga (oxiriga);
 * - `outdent` — otasidan chiqib, otasining keyiniga.
 * Amal mumkin bo'lmasa `null` (tugma o'chiq ko'rinadi).
 */
export function moveTarget(nodes: WikiTreeNode[], id: string, dir: MoveDir): { parentId: string; index: number } | null {
  const node = nodes.find((n) => n.id === id)
  if (!node) return null
  // Yuqori darajada BO'LIMLAR va «Mening eslatmalarim» sahifalari ikki ALOHIDA ro'yxat (server ham shunday
  // tartiblaydi — `WikiPageOps.ApplyMove`). Bo'lim faqat yuqoriga/pastga siljiydi.
  const sameKind = (n: WikiTreeNode) => !!n.isSection === !!node.isSection
  const siblings = nodes.filter((n) => n.parentId === node.parentId && sameKind(n)).sort(byOrder)
  const i = siblings.findIndex((n) => n.id === id)
  if (node.isSection && (dir === 'indent' || dir === 'outdent')) return null

  switch (dir) {
    case 'up':
      return i > 0 ? { parentId: node.parentId, index: i - 1 } : null
    case 'down':
      return i < siblings.length - 1 ? { parentId: node.parentId, index: i + 1 } : null
    case 'indent': {
      if (i <= 0) return null
      const newParent = siblings[i - 1]
      return { parentId: newParent.id, index: nodes.filter((n) => n.parentId === newParent.id).length }
    }
    case 'outdent': {
      if (!node.parentId) return null
      const parent = nodes.find((n) => n.id === node.parentId)
      if (!parent) return { parentId: '', index: 0 }
      // Bo'limdan chiqqan sahifa «Mening eslatmalarim» oxiriga tushadi.
      if (parent.isSection) return { parentId: '', index: nodes.filter((n) => !n.parentId && !n.isSection).length }
      const parentSiblings = nodes.filter((n) => n.parentId === parent.parentId && !n.isSection).sort(byOrder)
      return { parentId: parent.parentId, index: parentSiblings.findIndex((n) => n.id === parent.id) + 1 }
    }
  }
}

/** Saqlangan JSON'dan bloklar (BlockNote `initialContent`). Bo'sh/buzuq — `undefined` (bo'sh sahifa). */
export function parseContent(json: string): unknown[] | undefined {
  if (!json) return undefined
  try {
    const v: unknown = JSON.parse(json)
    return Array.isArray(v) && v.length > 0 ? v : undefined
  } catch {
    return undefined
  }
}

/** Server 409 (`conflict: true`) qaytardimi — boshqa odam shu orada saqlagan. */
export function isConflictError(err: unknown): boolean {
  const e = err as { response?: { status?: number; data?: { conflict?: boolean } } }
  return e?.response?.status === 409 && e.response.data?.conflict === true
}

/** "2026-10-25T16:06:08" → "25.10.2026 16:06". */
export function formatStamp(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/.exec(iso)
  return m ? `${m[3]}.${m[2]}.${m[1]} ${m[4]}:${m[5]}` : iso
}

/**
 * Sahifa amalda QULFLANGANMI: o'zi yoki birorta otasi (bo'limi ham) qulflangan. Serverdagi
 * `WikiRules.IsLocked` nusxasi — faqat KO'RINISH uchun (tugmalarni yashirish, faqat o'qish rejimi);
 * hal qiluvchi tekshiruv baribir serverda (403). `id = ""` — yuqori daraja (hech qachon qulflanmaydi).
 */
export function effectiveLocked(nodes: WikiTreeNode[], id: string): boolean {
  const byId = new Map(nodes.map((n) => [n.id, n]))
  const seen = new Set<string>()
  let cur = id
  while (cur && !seen.has(cur)) {
    seen.add(cur)
    const n = byId.get(cur)
    if (!n) return false
    if (n.locked) return true
    cur = n.parentId
  }
  return false
}

/** O'qish/yozish shrifti o'lchamlari (px). Standart — 16 (Apple bilimlar bazasidagi asosiy matn). */
export const FONT_SIZES = [14, 15, 16, 17, 18, 20, 22] as const
export const DEFAULT_FONT_SIZE = 16
const FONT_KEY = 'wiki:fontSize'

/** Saqlangan o'lcham (har foydalanuvchining brauzerida). Buzuq/yo'q — standart. */
export function readFontSize(): number {
  try {
    const v = Number(localStorage.getItem(FONT_KEY))
    return (FONT_SIZES as readonly number[]).includes(v) ? v : DEFAULT_FONT_SIZE
  } catch {
    return DEFAULT_FONT_SIZE
  }
}

export function writeFontSize(size: number) {
  try {
    localStorage.setItem(FONT_KEY, String(size))
  } catch {
    // brauzer xotirasi yopiq — faqat shu sessiyada qoladi
  }
}

/** Keyingi o'lcham (+1 / −1 qadam), chegarada o'zgarmaydi. */
export function stepFontSize(current: number, dir: 1 | -1): number {
  const list = FONT_SIZES as readonly number[]
  const i = list.indexOf(current)
  const at = i < 0 ? list.indexOf(DEFAULT_FONT_SIZE) : i
  return list[Math.min(list.length - 1, Math.max(0, at + dir))]
}

/** Manzildan faol tab: `/admin/bilimlar/xodimlar/...` — «Xodimlar», qolgani — «Eslatmalarim». */
export function wikiTabOf(pathname: string): 'notes' | 'team' {
  return pathname.startsWith('/admin/bilimlar/xodimlar') ? 'team' : 'notes'
}

/** HR rollari («Topshiriqlar → Xodimlar») — ro'yxatda lavozim yo'q bo'lsa ko'rsatiladi. */
export const HR_ROLE_LABELS: Record<string, string> = {
  director: 'Direktor',
  manager: 'Menejer',
  employee: 'Xodim',
}
