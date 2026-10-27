import { api } from '../client'

/**
 * BILIMLAR BAZASI (wiki) — shaxsiy eslatmalar. Ikki API:
 *   - «Eslatmalarim» — `api/me/notes` (egasi tokendan), ruxsatsiz (har bir xodim va o'qituvchi);
 *   - «Xodimlar» — `api/admin/wiki/team`, ruxsat `wiki.team`: o'qish — rahbariyat va menejer; yozish
 *     (sahifa qo'shish, tahrir, qulf, o'chirish) — faqat rahbariyat.
 * Chap panel BITTA so'rov bilan chiziladi (daraxt matnsiz), sahifa matni ochilganda alohida.
 * Qoidalar: `.claude/rules/wiki.md`.
 */

const NOTES = '/me/notes'
const TEAM = '/admin/wiki/team'

export interface WikiTreeNode {
  id: string
  /** "" — yuqori daraja */
  parentId: string
  title: string
  icon: string
  order: number
  /** Shu eslatmaning O'Z qulfi (meros — `effectiveLocked` bilan hisoblanadi) */
  locked: boolean
  /** BO'LIM (papka): chap panelda sarlavha, sahifa sifatida ochilmaydi; har doim yuqori darajada */
  isSection?: boolean
}

export interface WikiPage {
  id: string
  ownerUserId: string
  parentId: string
  title: string
  icon: string
  /** BlockNote bloklari (JSON massiv matni); "" — bo'sh sahifa */
  contentJson: string
  /** Saqlashda `baseVersion` sifatida qaytariladi — eski versiya ustidan yozilsa 409 */
  version: number
  createdAt: string
  createdBy: string
  updatedAt: string
  updatedBy: string
  /** O'z qulfi */
  locked: boolean
  /** O'zi yoki otasi qulflangan */
  effectiveLocked: boolean
}

export interface WikiSearchHit {
  pageId: string
  title: string
  icon: string
  snippet: string
}

// ============================================================================ SAHIFA MANBASI (adapter)

/**
 * Bitta sahifa bilan ishlash yo'li — o'z eslatmasi, xodim eslatmasi (o'qish) yoki uni tahrirlash (rahbariyat).
 * `WikiPageView` va avtosaqlash shu interfeys orqali ishlaydi — bitta komponent hamma joyda.
 */
export interface WikiPageSource {
  get: (id: string) => Promise<WikiPage>
  /** Yo'q bo'lsa — faqat o'qish (masalan xodim eslatmasi) */
  save?: (id: string, payload: { title: string; icon: string; contentJson: string; baseVersion: number }) => Promise<WikiPage>
}

// ============================================================================ MENING ESLATMALARIM (`api/me/notes`)

export interface WikiNotesTree {
  ownerUserId: string
  ownerName: string
  pages: WikiTreeNode[]
}

export async function getMyNotes(): Promise<WikiNotesTree> {
  const { data } = await api.get<WikiNotesTree>(NOTES)
  return data
}

/** KESHSIZ: boshqa odam (rahbar) yaqinda saqlagan bo'lsa eski versiya ochilib, saqlashda 409 chiqmasin. */
export const myNotePageSource: WikiPageSource = {
  get: async (id) => (await api.get<WikiPage>(`${NOTES}/pages/${id}`, { headers: { 'X-No-Cache': '1' } })).data,
  save: async (id, payload) => (await api.put<WikiPage>(`${NOTES}/pages/${id}`, payload)).data,
}

/** `isSection` — sahifa emas, yangi BO'LIM (faqat yuqori darajada, `parentId = ""`). */
export async function createMyNote(payload: { parentId: string; title: string; isSection?: boolean }): Promise<WikiPage> {
  const { data } = await api.post<WikiPage>(`${NOTES}/pages`, payload)
  return data
}

/**
 * Nomini o'zgartirish (bo'lim uchun): oxirgi versiyani olib, faqat sarlavhani saqlaydi — boshqa odam shu orada
 * o'zgartirgan bo'lsa ham versiya to'qnashuvi bo'lmaydi.
 */
export async function renameNote(source: WikiPageSource, id: string, title: string): Promise<WikiPage> {
  if (!source.save) throw new Error("Nomini o'zgartirib bo'lmaydi")
  const page = await source.get(id)
  return source.save(id, { title, icon: page.icon, contentJson: page.contentJson, baseVersion: page.version })
}

/** Ko'chirish: `index` — yangi ota ichidagi o'rni (0 — eng tepada). */
export async function moveMyNote(id: string, payload: { parentId: string; index: number }): Promise<WikiPage> {
  const { data } = await api.put<WikiPage>(`${NOTES}/pages/${id}/move`, payload)
  return data
}

/** Ichidagilari bilan birga o'chiradi; nechta o'chganini qaytaradi. */
export async function deleteMyNote(id: string): Promise<number> {
  const { data } = await api.delete<{ deleted: number }>(`${NOTES}/pages/${id}`)
  return data.deleted
}

export async function searchMyNotes(q: string): Promise<WikiSearchHit[]> {
  const { data } = await api.get<WikiSearchHit[]>(`${NOTES}/search`, { params: { q } })
  return data
}

// ============================================================================ XODIMLAR (`api/admin/wiki/team`)

export interface WikiTeamMember {
  userId: string
  fullName: string
  position: string
  /** HR roli: employee | manager | director */
  hrRole: string
  noteCount: number
  /** "" — hali yozmagan */
  lastUpdatedAt: string
}

export async function getTeamMembers(): Promise<{ isLeader: boolean; members: WikiTeamMember[] }> {
  const { data } = await api.get<{ isLeader: boolean; members: WikiTeamMember[] }>(`${TEAM}/members`)
  return data
}

export async function getTeamMemberNotes(userId: string): Promise<WikiNotesTree> {
  const { data } = await api.get<WikiNotesTree>(`${TEAM}/members/${userId}`, { headers: { 'X-No-Cache': '1' } })
  return data
}

/** Xodim eslatmasi — o'qish (`save` yo'q). KESHSIZ: rahbar xodim yozayotganini "jonli" kuzatadi (qayta so'rab turadi). */
export const teamNotePageSource: WikiPageSource = {
  get: async (id) => (await api.get<WikiPage>(`${TEAM}/pages/${id}`, { headers: { 'X-No-Cache': '1' } })).data,
}

export interface WikiSettings {
  managersSeeTeamNotes: boolean
  /** Bo'sh — barcha lavozimlar */
  managerVisiblePositionIds: string[]
  positions: { id: string; name: string }[]
}

export async function getWikiSettings(): Promise<WikiSettings> {
  const { data } = await api.get<WikiSettings>(`${TEAM}/settings`)
  return data
}

export async function saveWikiSettings(payload: { managersSeeTeamNotes: boolean; managerVisiblePositionIds: string[] }): Promise<WikiSettings> {
  const { data } = await api.put<WikiSettings>(`${TEAM}/settings`, payload)
  return data
}

// ---------------------------------------------------------------------------- RAHBARIYAT: xodim eslatmalarida yozish

/** Xodimga yangi sahifa qo'shish (u xodimning o'z daraxtida chiqadi). Faqat rahbariyat. */
export async function createMemberNote(userId: string, payload: { parentId: string; title: string; isSection?: boolean }): Promise<WikiPage> {
  const { data } = await api.post<WikiPage>(`${TEAM}/members/${userId}/pages`, payload)
  return data
}

/** Xodim eslatmasini TAHRIRLASH manbasi — faqat rahbariyat, "o'zgartirasizmi?" tasdig'idan keyin ulanadi. */
export const memberNoteEditSource: WikiPageSource = {
  get: teamNotePageSource.get,
  save: async (id, payload) => (await api.put<WikiPage>(`${TEAM}/pages/${id}`, payload)).data,
}

/** Qulf: eslatma egasining o'ziga ham faqat o'qish bo'ladi. Faqat rahbariyat. */
export async function lockMemberNote(id: string, locked: boolean): Promise<WikiPage> {
  const { data } = await api.put<WikiPage>(`${TEAM}/pages/${id}/lock`, { locked })
  return data
}

export async function deleteMemberNote(id: string): Promise<number> {
  const { data } = await api.delete<{ deleted: number }>(`${TEAM}/pages/${id}`)
  return data.deleted
}
