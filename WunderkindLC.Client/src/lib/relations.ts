/**
 * TELEGRAM QARINDOSHLIK — kim ulangan (otasi / onasi / ota-onasi / o'zi / boshqa). Server qoidasi:
 * `TelegramRelations` (CRM upgrade P2.3). Bu fayl faqat KO'RINISH (yorliq, ikonka, chiplar, sanoq
 * satri) — sof funksiyalar, `lib/__tests__/relations.test.ts`.
 *
 * ⚠️ Bo'sh (`""`) qarindoshlik — hali to'ldirilmagan eski ulanish: server uni OTA-ONA deb hisoblaydi
 * (`TelegramRelations.Effective`), shuning uchun bu yerda ham «Ota-onasi».
 */

export type Relation = 'father' | 'mother' | 'parent' | 'student' | 'other'

export const RELATIONS: Relation[] = ['father', 'mother', 'parent', 'student', 'other']

/** «Barcha ota-onalar» — o'quvchining o'z chatidan boshqa hammasi (server standarti ham shu). */
export const PARENT_RELATIONS: Relation[] = ['father', 'mother', 'parent', 'other']

/** «O'quvchilar» — faqat o'quvchining o'z chati. */
export const STUDENT_RELATIONS: Relation[] = ['student']

const LABEL: Record<Relation, string> = {
  father: 'Otasi',
  mother: 'Onasi',
  parent: 'Ota-onasi',
  student: "O'zi",
  other: 'Boshqa',
}

/** Kompozitor chipidagi yorliq (SPEC P2.8: «O'quvchining o'zi»). */
const CHIP_LABEL: Record<Relation, string> = { ...LABEL, student: "O'quvchining o'zi" }

/** Sanoq satridagi kichik harfli yorliq ("otasi 140, onasi 120, o'quvchi 27"). */
const COUNT_LABEL: Record<Relation, string> = {
  father: 'otasi',
  mother: 'onasi',
  parent: 'ota-onasi',
  student: "o'quvchi",
  other: 'boshqa',
}

const ICON: Record<Relation, string> = {
  father: '👨',
  mother: '👩',
  parent: '👪',
  student: '🧑',
  other: '❔',
}

/** Server qiymatini qarindoshlikka keltiradi: noma'lum yoki bo'sh — «ota-ona». */
export function normalizeRelation(r: string | null | undefined): Relation {
  return (RELATIONS as string[]).includes(r ?? '') ? (r as Relation) : 'parent'
}

export const relationLabel = (r: string | null | undefined): string => LABEL[normalizeRelation(r)]
export const relationChipLabel = (r: Relation): string => CHIP_LABEL[r]
export const relationIcon = (r: string | null | undefined): string => ICON[normalizeRelation(r)]

/** Takrorlarsiz va katalog tartibida (otasi → onasi → ota-onasi → o'zi → boshqa). */
export function sortRelations(list: readonly string[]): Relation[] {
  const set = new Set(list.map(normalizeRelation))
  return RELATIONS.filter((r) => set.has(r))
}

/** Chip bosilganda: bor bo'lsa olib tashlanadi, yo'q bo'lsa qo'shiladi (tartib saqlanadi). */
export function toggleRelation(selected: readonly Relation[], r: Relation): Relation[] {
  return selected.includes(r) ? selected.filter((x) => x !== r) : sortRelations([...selected, r])
}

/** Tanlov AYNAN shu presetmi (tartibsiz to'plam tengligi). */
export function isPreset(selected: readonly Relation[], preset: readonly Relation[]): boolean {
  const a = new Set(selected)
  return a.size === preset.length && preset.every((r) => a.has(r))
}

/** Audience count javobi (server `AudienceCountDto`). */
export interface RelationCount {
  relation: string
  chats: number
}

export interface CountLineInput {
  messages: number
  chats: number
  byRelation: RelationCount[]
  notConnected: number
  blocked: number
  /** O'qituvchilar auditoriyasi: "o'quvchi" o'rniga "o'qituvchi". */
  teachers?: boolean
}

/**
 * Yuborishdan OLDINGI sanoq satri (SPEC P2.8): «Yuboriladi: 312 ta xabar · 287 ta chat (otasi 140,
 * onasi 120, o'quvchi 27) · Ulanmagan: 58 o'quvchi · Bloklagan: 6». Nol bo'lgan qismlar tushadi.
 */
export function countLine(c: CountLineInput): string {
  const rel = c.byRelation
    .filter((r) => r.chats > 0 && r.relation)
    .sort((a, b) => RELATIONS.indexOf(normalizeRelation(a.relation)) - RELATIONS.indexOf(normalizeRelation(b.relation)))
    .map((r) => `${COUNT_LABEL[normalizeRelation(r.relation)]} ${r.chats}`)
  const parts = [`Yuboriladi: ${c.messages} ta xabar`, `${c.chats} ta chat${rel.length > 0 ? ` (${rel.join(', ')})` : ''}`]
  if (c.notConnected > 0) parts.push(`Ulanmagan: ${c.notConnected} ${c.teachers ? "o'qituvchi" : "o'quvchi"}`)
  if (c.blocked > 0) parts.push(`Bloklagan: ${c.blocked}`)
  return parts.join(' · ')
}

/** «Telegram» ustuni filtri (U6b, klientda). */
export type TelegramFilter = 'all' | 'linked' | 'none'

export const TELEGRAM_FILTERS: { key: TelegramFilter; label: string }[] = [
  { key: 'all', label: 'Hammasi' },
  { key: 'linked', label: 'Ulangan' },
  { key: 'none', label: 'Ulanmagan' },
]

/** Qator filtrga mos keladimi (ulangan = bloklamagan chat kamida bitta). */
export function matchesTelegramFilter(linked: number | null | undefined, filter: TelegramFilter): boolean {
  if (filter === 'all') return true
  const n = linked ?? 0
  return filter === 'linked' ? n > 0 : n === 0
}
