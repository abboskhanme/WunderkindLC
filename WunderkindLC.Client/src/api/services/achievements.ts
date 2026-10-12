import { api, USE_MOCK } from '../client'
import type { MessageAttachment } from './messaging'

/* ============================================================
   O'QUVCHI YUTUQLARI (CRM upgrade Phase 2, U2c) — IELTS / CEFR / sertifikat / olimpiada.
   Admin: `api/admin/achievements` (ruxsat `students.list`, o'qish ham).
   Kabinet: `api/student/achievements` (o'quvchi — o'zi, ota-ona — farzandi).
   Rasm statik `/uploads` dan EMAS — avtorizatsiyalangan endpointdan blob qilib olinadi.
   ============================================================ */

export type AchievementKind = 'ielts' | 'cefr' | 'certificate' | 'olympiad' | 'other'

export const ACHIEVEMENT_KINDS: { key: AchievementKind; label: string }[] = [
  { key: 'ielts', label: 'IELTS' },
  { key: 'cefr', label: 'CEFR' },
  { key: 'certificate', label: 'Sertifikat' },
  { key: 'olympiad', label: 'Olimpiada' },
  { key: 'other', label: 'Boshqa' },
]

export interface Achievement {
  id: string
  studentId: string
  studentName: string
  kind: AchievementKind
  kindLabel: string
  title: string
  result: string
  /** "yyyy-MM-dd" */
  date: string
  note: string
  hasPhoto: boolean
  /**
   * Rasm biriktirmasi id'si — tahrirda rasmni SAQLAB qolish uchun qayta yuboriladi (PUT `attachmentId`
   * bo'sh kelsa server rasmni olib tashlaydi). Eski backend bermasa — `undefined`.
   */
  attachmentId?: string | null
  congratulatedAt: string | null
  createdAt: string
  createdByName: string
  /** Yaratish/tabrikda navbatga qo'yilgan Telegram xabarlar soni. */
  messages?: number
  /** Yutuq saqlandi, lekin tabrik yuborilmadi — sababi (ko'rsatiladi). */
  warning?: string | null
}

export interface SaveAchievementRequest {
  studentId: string
  kind: AchievementKind
  title: string
  result: string
  date: string
  note: string
  attachmentId?: string | null
  congratulate?: boolean
}

export async function getAchievements(studentId: string): Promise<Achievement[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Achievement[]>('/admin/achievements', { params: { studentId } })
  return data
}

export async function createAchievement(req: SaveAchievementRequest): Promise<Achievement> {
  const { data } = await api.post<Achievement>('/admin/achievements', req)
  return data
}

export async function updateAchievement(id: string, req: SaveAchievementRequest): Promise<Achievement> {
  const { data } = await api.put<Achievement>(`/admin/achievements/${id}`, req)
  return data
}

export async function deleteAchievement(id: string): Promise<void> {
  await api.delete(`/admin/achievements/${id}`)
}

/** Tabrikni (qayta) yuborish — har safar yangi xabar. */
export async function congratulateAchievement(id: string): Promise<{ messages: number }> {
  const { data } = await api.post<{ messages: number }>(`/admin/achievements/${id}/congratulate`)
  return data
}

/** Admin: yutuq rasmini yuklash — O'Z endpointi (`students.list`), xabarlar ruxsati shart emas.
 *  Faqat jpg/png ≤ 10 MB (server tur, hajm va mazmunni tekshiradi). */
export async function uploadAchievementPhoto(
  file: File, onProgress?: (pct: number) => void,
): Promise<MessageAttachment> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<MessageAttachment>('/admin/achievements/photo', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
    onUploadProgress: (e) => {
      if (onProgress && e.total) onProgress(Math.round((e.loaded / e.total) * 100))
    },
  })
  return data
}

/** Admin: yutuq rasmi blob URL (chaqiruvchi `URL.revokeObjectURL` qiladi). */
export async function fetchAchievementPhotoUrl(id: string): Promise<string> {
  const res = await api.get(`/admin/achievements/${id}/photo`, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}

/* ---------- Kabinet («Yutuqlarim») ---------- */

export async function getMyAchievements(studentId?: string): Promise<Achievement[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<Achievement[]>('/student/achievements', {
    params: studentId ? { studentId } : undefined,
  })
  return data
}

export async function fetchMyAchievementPhotoUrl(id: string, studentId?: string): Promise<string> {
  const res = await api.get(`/student/achievements/${id}/photo`, {
    responseType: 'blob',
    params: studentId ? { studentId } : undefined,
  })
  return URL.createObjectURL(res.data as Blob)
}
