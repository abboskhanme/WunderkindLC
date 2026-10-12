import { api } from '../client'

/**
 * O'QUVCHILAR REYTINGI — testlar + jurnal bali (docs/SPEC-crm-upgrade.md P4.3).
 *
 * Reyting bali = testlar o'rtachasi × ulush (standart 60%) + ball (guruhdagi eng yaxshi ballga nisbatan,
 * 0–100) × qolgani. Hamma raqam SERVERDA hisoblanadi — klient faqat chizadi (`.claude/rules/rating.md`).
 * Qarzdorlik reytingni YOPMAYDI (egasining qarori).
 */

export type RatingPeriodKind = 'month' | 'quarter' | 'year'

export interface RatingPeriod {
  kind: RatingPeriodKind
  /** month/quarter: "yyyy-MM" (3 oyda — OXIRGI oy); year: o'quv yili boshlanadigan yil "yyyy". */
  key: string
  label: string
  from: string
  to: string
}

/* ---------- Markaz jadvali ---------- */

export interface RatingCentreGroup {
  id: string
  name: string
  rank: number | null
  score: number | null
}

export interface RatingCentreItem {
  rank: number
  studentId: string
  fullName: string
  score: number
  testAvg: number | null
  testCount: number
  ball: number
  groups: RatingCentreGroup[]
}

export interface RatingCentre {
  period: RatingPeriod
  testWeight: number
  total: number
  page: number
  pageSize: number
  items: RatingCentreItem[]
}

/* ---------- Guruh jadvali ---------- */

export interface RatingGroupItem {
  /** null — shu davrda testi ham, bali ham yo'q (o'rinsiz). */
  rank: number | null
  studentId: string
  fullName: string
  score: number | null
  testAvg: number | null
  testCount: number
  ball: number
  ballNorm: number | null
}

export interface RatingGroupTable {
  period: RatingPeriod
  testWeight: number
  group: { id: string; name: string; courseName: string; teacherName: string }
  ranked: number
  items: RatingGroupItem[]
}

/* ---------- O'quvchi bloki ---------- */

export interface RatingBlockGroup {
  groupId: string
  groupName: string
  courseName: string
  rank: number | null
  total: number
  score: number | null
  testAvg: number | null
  testCount: number
  ball: number
  ballNorm: number | null
}

export interface RatingHistoryPoint {
  month: string
  centreRank: number | null
  centreTotal: number
  score: number | null
  testAvg: number | null
}

export interface RatingTestRow {
  testId: string
  name: string
  /** 'monthly' | 'interim' (U4a dan keyin); hozircha '' bo'lishi mumkin. */
  kind: string
  date: string
  groupName: string
  solved: number
  questions: number
  percent: number | null
  rankInTest: number
  takers: number
}

export interface StudentRatingBlock {
  studentId: string
  period: RatingPeriod
  centre: { rank: number | null; total: number; score: number | null; prevRank: number | null }
  groups: RatingBlockGroup[]
  components: { testAvg: number | null; testCount: number; ball: number; testWeight: number }
  history: RatingHistoryPoint[]
  tests: RatingTestRow[]
  attendance: { held: number; attended: number; percent: number | null }
}

export interface RatingTopRow {
  rank: number
  fullName: string
  score: number
  isMe: boolean
}

export interface StudentRatingSummary extends StudentRatingBlock {
  groupTop: { groupId: string; groupName: string; rows: RatingTopRow[] }[]
  centreTop: RatingTopRow[]
}

/* ---------- Sozlamalar ---------- */

export type RatingTopBottomScope = 'group' | 'centre'

export interface RatingSettings {
  testWeight: number
  topBottomCount: number
  topBottomScope: RatingTopBottomScope
}

export interface RatingQuery {
  period: RatingPeriodKind
  key: string
}

const q = (p: RatingQuery) => ({ period: p.period, key: p.key })

export async function getRatingCentre(p: RatingQuery & { q?: string; page?: number; pageSize?: number }) {
  const { data } = await api.get<RatingCentre>('/admin/rating/centre', {
    params: { ...q(p), q: p.q || undefined, page: p.page ?? 1, pageSize: p.pageSize ?? 50 },
  })
  return data
}

export async function getRatingGroup(groupId: string, p: RatingQuery) {
  const { data } = await api.get<RatingGroupTable>(`/admin/rating/groups/${groupId}`, { params: q(p) })
  return data
}

export async function getStudentRatingBlock(studentId: string, p: RatingQuery) {
  const { data } = await api.get<StudentRatingBlock>(`/admin/rating/students/${studentId}`, { params: q(p) })
  return data
}

export async function getRatingSettings() {
  const { data } = await api.get<RatingSettings>('/admin/rating/settings')
  return data
}

export async function saveRatingSettings(s: RatingSettings) {
  const { data } = await api.put<RatingSettings>('/admin/rating/settings', s)
  return data
}

/** O'quvchi/ota-ona kabineti: blok + guruh va markaz TOP ro'yxatlari. */
export async function getStudentRatingSummary(p: RatingQuery, studentId?: string) {
  const { data } = await api.get<StudentRatingSummary>('/student/rating/summary', {
    params: { ...q(p), ...(studentId ? { studentId } : {}) },
  })
  return data
}
