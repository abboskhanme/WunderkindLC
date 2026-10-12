/**
 * BOSH SAHIFA VIDJETLARI (CRM Phase 5) — katalog va sof qoidalar (`__tests__/homeWidgets.test.ts`).
 * ⚠️ Bu faylda KOMPONENT YO'Q (react-refresh qoidasi) — bloklarning o'zi `AdminDashboard` da kalit bo'yicha.
 *
 * Sozlama foydalanuvchiga bog'langan va SERVERDA saqlanadi (`GET/PUT/DELETE /admin/home/layout`,
 * `UserSettings.HomeLayout`) — boshqa brauzerda ham o'sha ko'rinish. Server qoidasi — `HomeLayoutRules`
 * (AYNAN shu katalog; `homeWidgets.test.ts` ikkalasining kalitlari bir xilligini qulflaydi).
 *
 * YANGI BLOK QO'SHISH: (1) server `HomeLayoutRules.BlockKeys` ga kalit, (2) shu yerda `HOME_BLOCKS` ga band
 * (`available: true`), (3) `AdminDashboard.renderBlock` ga komponent. «Davomat xabarlari» (`absentNotices`) va
 * «Qizil ro'yxat» (`redList`) Phase 3 (U3b) da qurildi — `available: true`.
 */
import { HOME_CARDS } from '@/lib/homeCards'

export const HOME_LAYOUT_VERSION = 1

export type HomeBlockKey =
  | 'cashboxes'
  | 'schedule'
  | 'lessonsMonitor'
  | 'myTasks'
  | 'absentNotices'
  | 'redList'
  | 'contactsToday'
  | 'aiAnalysis'

export interface HomeBlockDef {
  key: HomeBlockKey
  label: string
  description: string
  /** Standart holatda ko'rinadimi (bugungi sahifa: kassalar + jadval). */
  defaultVisible: boolean
  /** Ruxsat kaliti (`can(perm,'view')`); yo'q bo'lsa — hammaga. */
  perm?: string
  /** Faqat superadmin (ruxsat kaliti bilan ifodalab bo'lmaydi — `can()` admin'ga ham true). */
  superadminOnly?: boolean
  /** Blok komponenti hali QURILMAGAN (keyingi bosqich) — ko'rsatilmaydi. */
  available: boolean
}

export const HOME_BLOCKS: HomeBlockDef[] = [
  { key: 'cashboxes', label: 'Kassalar balansi', description: "Har kassaning qoldig'i va jami", defaultVisible: true, superadminOnly: true, available: true },
  { key: 'schedule', label: 'Dars jadvali', description: '«Kun» / «Hafta» — haqiqiy sanadagi darslar', defaultVisible: true, perm: 'schedule.timetable', available: true },
  { key: 'lessonsMonitor', label: 'Darslar monitoringi', description: "Bugungi darslar: davomat va baho qo'yildimi", defaultVisible: false, perm: 'classes.notAttended', available: true },
  { key: 'myTasks', label: 'Mening topshiriqlarim', description: 'Ochiq topshiriqlarim va tasdig\'imni kutayotganlar', defaultVisible: false, perm: 'tasks.board', available: true },
  { key: 'absentNotices', label: 'Davomat xabarlari', description: 'Bugungi «kelmadi» xabarlari: rejalashtirilgan, tasdiq kutayotgan, to\'xtatilgan', defaultVisible: false, perm: 'students.notices', available: true },
  { key: 'redList', label: "Qizil ro'yxat", description: 'Ketma-ket sababsiz dars qoldirganlar (top 10)', defaultVisible: false, perm: 'students.attendance', available: true },
  { key: 'contactsToday', label: "Bog'lanish kerak — bugun", description: "Bugun qo'ng'iroq qilinishi kerak bo'lganlar", defaultVisible: false, perm: 'contacts', available: true },
  { key: 'aiAnalysis', label: 'AI tahlil', description: 'Markaz bo\'yicha kunlik AI tahlil', defaultVisible: false, perm: 'ai', available: true },
]

export const HOME_CARD_KEYS: string[] = HOME_CARDS.map((c) => c.key)

export interface HomeLayoutItem {
  key: string
  visible: boolean
}

export interface HomeLayout {
  version: number
  cardsVisible: boolean
  cards: HomeLayoutItem[]
  blocks: HomeLayoutItem[]
}

/** Standart — vidjetlardan oldingi bosh sahifa bilan bir xil (12 kartochka + kassalar + jadval). */
export function defaultHomeLayout(): HomeLayout {
  return {
    version: HOME_LAYOUT_VERSION,
    cardsVisible: true,
    cards: HOME_CARD_KEYS.map((key) => ({ key, visible: true })),
    blocks: HOME_BLOCKS.map((b) => ({ key: b.key, visible: b.defaultVisible })),
  }
}

function merge(items: unknown, catalog: { key: string; visible: boolean }[]): HomeLayoutItem[] {
  const known = new Map(catalog.map((c) => [c.key, c.visible]))
  const seen = new Set<string>()
  const out: HomeLayoutItem[] = []
  for (const it of Array.isArray(items) ? items : []) {
    const key = (it as { key?: unknown })?.key
    if (typeof key !== 'string' || !known.has(key) || seen.has(key)) continue
    seen.add(key)
    out.push({ key, visible: (it as { visible?: unknown }).visible !== false })
  }
  for (const c of catalog) if (!seen.has(c.key)) out.push({ key: c.key, visible: c.visible })
  return out
}

/**
 * Server qoidasi bilan bir xil (`HomeLayoutRules.Normalize`): noma'lum va takror kalitlar tashlanadi,
 * yetishmaganlari standart ko'rinishi bilan OXIRIGA qo'shiladi. Buzuq qiymat — standart.
 */
export function normalizeHomeLayout(raw: unknown): HomeLayout {
  const r = raw as Partial<HomeLayout> | null | undefined
  if (!r || r.version !== HOME_LAYOUT_VERSION) return defaultHomeLayout()
  return {
    version: HOME_LAYOUT_VERSION,
    cardsVisible: r.cardsVisible !== false,
    cards: merge(r.cards, HOME_CARD_KEYS.map((key) => ({ key, visible: true }))),
    blocks: merge(r.blocks, HOME_BLOCKS.map((b) => ({ key: b.key, visible: b.defaultVisible }))),
  }
}

/** Eski brauzer sozlamasi («Statistika» tugmasi, `localStorage['home.showStats']`). */
export const LEGACY_SHOW_STATS_KEY = 'home.showStats'

/**
 * Sahifa ochilgandagi holat: serverda sozlama bo'lsa — u; bo'lmasa standart, lekin eski brauzerdagi
 * «Statistika» tanlovi BIR MARTA boshlang'ich qiymat bo'lib o'tadi (`legacyShowStats`).
 */
export function initialHomeLayout(server: HomeLayout | null, legacyShowStats: boolean | null): HomeLayout {
  if (server) return normalizeHomeLayout(server)
  const d = defaultHomeLayout()
  return legacyShowStats === false ? { ...d, cardsVisible: false } : d
}

/**
 * ▲/▼: ikki elementning o'rnini almashtiradi (kalit bo'yicha). Oynada faqat RUXSAT etilgan bandlar
 * ko'rinadi, shuning uchun "qo'shni" — ko'rinadigan qo'shni; to'liq ro'yxatdagi ruxsatsiz bandlar o'z
 * joyida qoladi. Kalit topilmasa — o'zgarishsiz.
 */
export function swapItems(list: HomeLayoutItem[], keyA: string, keyB: string): HomeLayoutItem[] {
  const a = list.findIndex((x) => x.key === keyA)
  const b = list.findIndex((x) => x.key === keyB)
  if (a < 0 || b < 0 || a === b) return list
  const next = [...list]
  ;[next[a], next[b]] = [next[b], next[a]]
  return next
}

/** Ko'rinishni almashtirish. */
export function toggleItem(list: HomeLayoutItem[], key: string): HomeLayoutItem[] {
  return list.map((i) => (i.key === key ? { ...i, visible: !i.visible } : i))
}

export interface WidgetAccess {
  can: (perm: string, action: 'view') => boolean
  isSuperAdmin: boolean
}

/** Foydalanuvchi bu blokni ko'ra oladimi (ruxsat + qurilganmi). */
export function blockAllowed(def: HomeBlockDef, a: WidgetAccess): boolean {
  if (!def.available) return false
  if (def.superadminOnly) return a.isSuperAdmin
  return !def.perm || a.can(def.perm, 'view')
}

/** Vidjetlar oynasidagi bloklar — tartibda, faqat foydalanuvchiga RUXSAT etilganlari. */
export function allowedBlocks(layout: HomeLayout, a: WidgetAccess): (HomeLayoutItem & { def: HomeBlockDef })[] {
  const defs = new Map(HOME_BLOCKS.map((b) => [b.key as string, b]))
  return layout.blocks
    .map((i) => ({ ...i, def: defs.get(i.key)! }))
    .filter((i) => i.def && blockAllowed(i.def, a))
}

/** Sahifada chiziladigan bloklar — tartibda, ko'rinadigan VA ruxsat etilgan. */
export function visibleBlocks(layout: HomeLayout, a: WidgetAccess): HomeBlockDef[] {
  return allowedBlocks(layout, a).filter((i) => i.visible).map((i) => i.def)
}

/** Sahifada chiziladigan kartochkalar — tartibda, ko'rinadigan VA ruxsat etilgan (`HOME_CARDS` dan). */
export function visibleCards(layout: HomeLayout, a: WidgetAccess): typeof HOME_CARDS {
  const byKey = new Map(HOME_CARDS.map((c) => [c.key as string, c]))
  return layout.cards
    .filter((i) => i.visible)
    .map((i) => byKey.get(i.key))
    .filter((c): c is (typeof HOME_CARDS)[number] => !!c && a.can(c.perm, 'view'))
}
