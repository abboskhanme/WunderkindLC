import { api } from '../client'
import type {
  GroupTest,
  SendTestResultsResult,
  StudentTestResult,
  TestGroupOverview,
  TestOverviewRow,
  TestResultDetail,
  TestScoreBatchResult,
  TestScoreBatchRow,
} from '@/types'

/** CRM U4a: ro'yxat filtrlari — oy ("yyyy-MM"; bo'sh — hammasi) va tur («monthly»/«interim»). */
export interface TestListFilter {
  month?: string
  kind?: string
}

/** Bo'sh qiymatlarni so'rovga qo'shmaymiz (server bo'sh = filtrsiz deb oladi). */
const clean = (p: Record<string, string | undefined>) =>
  Object.fromEntries(Object.entries(p).filter(([, v]) => v != null && v !== ''))

/** Testlar natijalari bosh sahifasi — barcha guruhlar + testlar soni (`month` — shu oy kesimi). */
export async function getTestGroups(month?: string): Promise<TestGroupOverview[]> {
  const { data } = await api.get<TestGroupOverview[]>('/admin/test-results/groups', {
    params: clean({ month }),
  })
  return data
}

/** Bitta guruhning testlar ro'yxati (oy/tur filtri bilan). */
export async function getGroupTests(groupId: string, filter: TestListFilter = {}): Promise<GroupTest[]> {
  const { data } = await api.get<GroupTest[]>('/admin/test-results', {
    params: clean({ groupId, month: filter.month, kind: filter.kind }),
  })
  return data
}

/** Markaz kesimi — tanlangan oydagi barcha guruhlar testlari. */
export async function getTestsOverview(
  filter: TestListFilter & { courseId?: string; teacherId?: string } = {},
): Promise<TestOverviewRow[]> {
  const { data } = await api.get<TestOverviewRow[]>('/admin/test-results/overview', { params: clean({ ...filter }) })
  return data
}

/** Test tafsiloti — o'quvchilar + ballari (ball desc). */
export async function getTestDetail(id: string): Promise<TestResultDetail> {
  const { data } = await api.get<TestResultDetail>(`/admin/test-results/${id}`)
  return data
}

export interface TestPayload {
  groupId: string
  name: string
  date: string
  /** CRM U4a: «monthly» | «interim» — yangi testda SHART. */
  kind?: string
  /** CRM U4a: «Savollar soni» 1..200 — yangi testda SHART (maks ball = savollar soni). */
  questionCount?: number | null
  /** Faqat ESKI (savollar sonisiz) testni tahrirlashda — maksimal ball. */
  maxScore?: number
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled?: boolean
  /** Sertifikat shabloni; bo'sh/null — standart shablon */
  certificateTemplateId?: string | null
}

export async function createTest(payload: TestPayload): Promise<GroupTest> {
  const { data } = await api.post<GroupTest>('/admin/test-results', { maxScore: 0, ...payload })
  return data
}

export async function updateTest(id: string, payload: Omit<TestPayload, 'groupId'>): Promise<void> {
  await api.put(`/admin/test-results/${id}`, { maxScore: 0, ...payload })
}

export async function deleteTest(id: string): Promise<void> {
  await api.delete(`/admin/test-results/${id}`)
}

/** Bitta o'quvchiga ball qo'yish/tozalash (score=null). Qaytadi: qayta saralangan tafsilot. */
export async function setTestScore(
  id: string,
  studentId: string,
  score: number | null,
): Promise<TestResultDetail> {
  const { data } = await api.put<TestResultDetail>(`/admin/test-results/${id}/scores`, {
    studentId,
    score,
  })
  return data
}

/** CRM U4a: jadvaldan bir nechta katakni bittada saqlash — noto'g'ri qatorlar `errors` da (saqlanmaydi). */
export async function saveTestScoresBatch(id: string, rows: TestScoreBatchRow[]): Promise<TestScoreBatchResult> {
  const { data } = await api.put<TestScoreBatchResult>(`/admin/test-results/${id}/scores/batch`, { rows })
  return data
}

/** CRM U4a: «Ota-onalarga yuborish» — faqat o'zgargan natijalar (`resend` — hammasini qayta). */
export async function sendTestResults(id: string, resend = false): Promise<SendTestResultsResult> {
  const { data } = await api.post<SendTestResultsResult>(`/admin/test-results/${id}/send-results`, { resend })
  return data
}

/** O'quvchi profilidagi test natijalari (barcha guruhlaridan). */
export async function getStudentTestResults(studentId: string): Promise<StudentTestResult[]> {
  const { data } = await api.get<StudentTestResult[]>(`/admin/test-results/student/${studentId}`)
  return data
}
