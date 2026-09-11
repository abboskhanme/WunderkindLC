import { api } from '../client'
import type { PublicTest, TestResult } from '@/types'

/**
 * Ommaviy (autentifikatsiyasiz) daraja testi servisi. `api` instance tokensiz ham ishlaydi —
 * endpointlar [AllowAnonymous]. Test topshirilganda CRM'da yangi lid yaratiladi.
 */

/** Slug bo'yicha faol testni oladi (to'g'ri javobsiz). 404 — topilmadi/faol emas. */
export async function getPublicTest(slug: string): Promise<PublicTest> {
  const { data } = await api.get<PublicTest>(`/public/test/${slug}`)
  return data
}

/** Testni topshiradi → ball/daraja + lid yaratiladi. */
export async function submitPublicTest(
  slug: string,
  body: {
    fullName: string
    phone: string
    age: number
    answers: Record<string, number>
    surveyAnswers?: Record<string, number[]>
  },
): Promise<TestResult> {
  const { data } = await api.post<TestResult>(`/public/test/${slug}/submit`, body)
  return data
}

/** Bir martalik havola (invite) bo'yicha test + lid ma'lumoti (oldindan to'ldirilgan). */
export interface PublicInvite {
  test: PublicTest | null
  fullName: string
  phone: string
  used: boolean
}
export async function getInviteTest(token: string): Promise<PublicInvite> {
  const { data } = await api.get<PublicInvite>(`/public/test/invite/${token}`)
  return data
}

/** Bir martalik havola orqali topshirish → natija lidga bog'lanadi, havola yopiladi. */
export async function submitInviteTest(
  token: string,
  body: { answers: Record<string, number>; surveyAnswers?: Record<string, number[]> },
): Promise<TestResult> {
  const { data } = await api.post<TestResult>(`/public/test/invite/${token}/submit`, {
    fullName: '',
    phone: '',
    age: 0,
    ...body,
  })
  return data
}
