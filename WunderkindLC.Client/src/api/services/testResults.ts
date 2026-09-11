import { api } from '../client'
import type {
  GroupTest,
  OnlineTest,
  StudentTestResult,
  TestGroupOverview,
  TestResultDetail,
} from '@/types'

/** Testlar natijalari bosh sahifasi — barcha guruhlar + testlar soni. */
export async function getTestGroups(): Promise<TestGroupOverview[]> {
  const { data } = await api.get<TestGroupOverview[]>('/admin/test-results/groups')
  return data
}

/** Bitta guruhning testlar ro'yxati. */
export async function getGroupTests(groupId: string): Promise<GroupTest[]> {
  const { data } = await api.get<GroupTest[]>('/admin/test-results', { params: { groupId } })
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
  maxScore: number
  /** Onlayn test sozlamalari; berilmasa (yoki mode="offline") — eski (oflayn) test. */
  online?: OnlineTest
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled?: boolean
  /** Sertifikat shabloni; bo'sh/null — standart shablon */
  certificateTemplateId?: string | null
}

export async function createTest(payload: TestPayload): Promise<GroupTest> {
  const { data } = await api.post<GroupTest>('/admin/test-results', payload)
  return data
}

export async function updateTest(
  id: string,
  payload: {
    name: string
    date: string
    maxScore: number
    online?: OnlineTest
    /** true — test natijasi bo'yicha sertifikat beriladi */
    certificateEnabled?: boolean
    /** Sertifikat shabloni; bo'sh/null — standart shablon */
    certificateTemplateId?: string | null
  },
): Promise<void> {
  await api.put(`/admin/test-results/${id}`, payload)
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

/** O'quvchi profilidagi test natijalari (barcha guruhlaridan). */
export async function getStudentTestResults(studentId: string): Promise<StudentTestResult[]> {
  const { data } = await api.get<StudentTestResult[]>(`/admin/test-results/student/${studentId}`)
  return data
}
