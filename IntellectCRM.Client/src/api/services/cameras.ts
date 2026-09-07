import { api } from '../client'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? '/api'

export interface Camera {
  id: string
  name: string
  location: string
  rtspUrl: string
  rtspSubUrl: string
  /** Yozuv necha kun saqlansin (0 = cheksiz) */
  retentionDays: number
  isActive: boolean
  note: string
  /**
   * Shu kamera 24/7 diskka yozib borilsinmi. Jonli kuzatuvdan ALOHIDA — `false` bo'lsa ham
   * kamera jonli ko'rinaveradi, faqat yozuv (playback / qirqib olish) bo'lmaydi.
   * ⚠️ Haqiqiy yozuv markazdagi BOSH kalit bilan birga hal qilinadi
   * (Sozlamalar -> Kamera integratsiya). Bosh kalit o'chiq bo'lsa bu bayroq ta'sir qilmaydi.
   */
  recordEnabled: boolean
  /** NVR (videoregistrator) dagi kanal raqami. 0 = kamera NVR'da yo'q. */
  nvrChannel: number
  /**
   * Arxiv (orqaga qaytarish / klip) QAYERDAN olinadi — serverda hisoblanadi:
   * `nvr` (NVR arxividan) | `local` (bizning yozuvimizdan) | `none` (arxiv yo'q).
   * ⚠️ Klientda qayta hisoblamang: qoida serverda (`CameraRules.ArchiveSource`).
   */
  archiveSource: 'nvr' | 'local' | 'none'
}

export interface SaveCameraPayload {
  name: string
  location?: string
  rtspUrl: string
  rtspSubUrl?: string
  retentionDays: number
  isActive: boolean
  note?: string
  recordEnabled: boolean
  nvrChannel: number
}

export async function getCameras(): Promise<Camera[]> {
  const { data } = await api.get<Camera[]>('/admin/cameras')
  return data
}

export async function createCamera(payload: SaveCameraPayload): Promise<Camera> {
  const { data } = await api.post<Camera>('/admin/cameras', payload)
  return data
}

export async function updateCamera(id: string, payload: SaveCameraPayload): Promise<Camera> {
  const { data } = await api.put<Camera>(`/admin/cameras/${id}`, payload)
  return data
}

export async function deleteCamera(id: string): Promise<void> {
  await api.delete(`/admin/cameras/${id}`)
}

/** Jonli HLS pleylist manzili (hls.js shu manzilni autentifikatsiya bilan oladi). */
export function cameraLiveUrl(id: string): string {
  return `${API_BASE}/admin/cameras/${id}/index.m3u8`
}

/** NVR arxividagi bitta uzluksiz bo'lak (markaz vaqtida). */
export interface NvrSegment {
  start: string
  end: string
}

export interface NvrSearchResult {
  ok: boolean
  /** Bo'sh bo'lmasa — SABAB (manzil? port? login? kanal?). Sozlash aynan shu matn bilan qilinadi. */
  error: string
  segments: NvrSegment[]
}

/**
 * NVR arxivida shu oraliqda yozuv bormi. `to` berilmasa `from` + 1 kun.
 * ⚠️ Xato holatida ham 200 qaytadi — sababni `error` dan o'qing.
 */
export async function nvrSearch(id: string, from: string, to?: string): Promise<NvrSearchResult> {
  const { data } = await api.get<NvrSearchResult>(`/admin/cameras/${id}/nvr-search`, {
    params: { from, ...(to ? { to } : {}) },
  })
  return data
}

/** Playback/clip — arxivdan MP4 (blob). start "YYYY-MM-DDTHH:mm:ss", duration soniyada. */
export async function getClipBlob(id: string, start: string, durationSec: number): Promise<Blob> {
  const { data } = await api.get(`/admin/cameras/${id}/clip`, {
    params: { start, duration: durationSec },
    responseType: 'blob',
  })
  return data as Blob
}
