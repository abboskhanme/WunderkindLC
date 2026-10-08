import type { Feedback } from '@/types'
import { api, USE_MOCK } from '../client'

export async function getFeedback(type?: string, status?: string): Promise<Feedback[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Feedback[]>('/admin/feedback', {
    params: { type: type || undefined, status: status || undefined },
  })
  return data
}

export async function resolveFeedback(id: string): Promise<void> {
  await api.post(`/admin/feedback/${id}/resolve`)
}

/** Hal qilingan murojaatni qayta ochish ("Yangi" ga qaytarish) — xato bosilgan "Hal qilindi" uchun. */
export async function reopenFeedback(id: string): Promise<void> {
  await api.post(`/admin/feedback/${id}/reopen`)
}
