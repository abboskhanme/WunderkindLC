import type { CenterAiRecord, CenterAiHistoryItem, CenterAiResponse } from '@/types'
import { api } from '../client'

/** Bosh sahifa "AI Tahlil" — bugungi (yoki eng so'nggi) markaz AI tahlili. Yo'q bo'lsa null. */
export async function getCenterAiAnalysis(): Promise<CenterAiRecord | null> {
  const { data } = await api.get<CenterAiRecord | null>('/admin/ai-analysis/center')
  return data ?? null
}

/** Markaz AI tahlillari tarixi (eng yangisi birinchi). */
export async function getCenterAiHistory(): Promise<CenterAiHistoryItem[]> {
  const { data } = await api.get<CenterAiHistoryItem[]>('/admin/ai-analysis/center/history')
  return data
}

/** Qo'lda AI tahlil yaratish. force=true (superadmin) — bugungi yozuvni qayta yaratadi. */
export async function runCenterAiAnalysis(force = false): Promise<CenterAiResponse> {
  const { data } = await api.post<CenterAiResponse>(
    `/admin/ai-analysis/center/run${force ? '?force=true' : ''}`,
  )
  return data
}
