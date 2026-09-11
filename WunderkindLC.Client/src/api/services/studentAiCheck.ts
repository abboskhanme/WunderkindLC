import { api } from '../client'
import type { AiCheck, AiCheckListItem, AiCheckStatus } from '@/types'

/** O'quvchi AI tekshiruv holati (limit/premium/blok + kalitlar tayyorligi). */
export async function getAiCheckStatus(): Promise<AiCheckStatus> {
  const { data } = await api.get<AiCheckStatus>('/student/ai-check/status')
  return data
}

/** AI tekshiruv tarixi (eng yangi birinchi). */
export async function getAiCheckHistory(): Promise<AiCheckListItem[]> {
  const { data } = await api.get<AiCheckListItem[]>('/student/ai-check/history')
  return data
}

/** Bitta yozuv (to'liq — matn/ovoz/tahlil). */
export async function getAiCheckItem(id: string): Promise<AiCheck> {
  const { data } = await api.get<AiCheck>(`/student/ai-check/history/${id}`)
  return data
}

/** Writing (yozma) — matn yuboriladi, Gemini tahlil qiladi. taskType: ielts_task1/ielts_task2 — IELTS band. */
export async function submitWriting(text: string, prompt?: string, taskType?: string): Promise<AiCheck> {
  const { data } = await api.post<AiCheck>('/student/ai-check/writing', { text, prompt, taskType })
  return data
}

/** Speaking (nutq) — WAV ovoz yuboriladi, Azure + Gemini tahlil qiladi. */
export async function submitSpeaking(
  audio: Blob,
  prompt?: string,
  referenceText?: string,
): Promise<AiCheck> {
  const fd = new FormData()
  fd.append('audio', audio, 'speaking.wav')
  if (prompt) fd.append('prompt', prompt)
  if (referenceText) fd.append('referenceText', referenceText)
  const { data } = await api.post<AiCheck>('/student/ai-check/speaking', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

/** Yuklangan media (ovoz) to'liq URL — boshqa origin (VITE_API_BASE_URL) bo'lsa prefiks qo'shiladi. */
export function mediaUrl(path: string): string {
  if (!path) return ''
  if (/^https?:\/\//i.test(path)) return path
  const base = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api'
  const origin = base.replace(/\/api\/?$/, '')
  return origin + path
}
