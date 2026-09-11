import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * YUZ BILAN KIRISH — admin tomoni (`/api/admin/face/*`).
 *
 * Modul mohiyati: o'quvchi mobil ilovasiga YANGI QURILMADAN kirganda selfi so'raladi.
 * Model TELEFONDA ishlaydi, serverga faqat vektor keladi va kosinus bilan solishtiriladi.
 * Etalon yo'q bo'lsa selfi o'quvchining PROFIL RASMI vektori bilan solishtiriladi; profil rasmi
 * ham bo'lmasa urinish `pending` bo'lib shu bo'limga — ADMINGA tushadi.
 *
 * ⚠️ Bo'lim `students` ruxsati bilan darvozalangan (`AdminPerm("students", ReadRequiresPerm = true)`)
 * — javobda selfi manzillari qaytadi, ya'ni O'QISH ham ruxsat talab qiladi
 * (`.claude/rules/uploads-security.md`).
 *
 * ⚠️ SELFI RASMI `<img src>` bilan TO'G'RIDAN-TO'G'RI OLINMAYDI. Biometrik surat `uploads/face/`
 * da va u STATIK yo'l bilan umuman berilmaydi (sertifikatlar bilan bir xil siyosat): `imageUrl`
 * `/api/admin/face/...` endpointiga ishora qiladi, u esa JWT sarlavhasini talab qiladi — brauzer
 * esa `<img>` so'roviga `Authorization` sarlavhasini QO'SHMAYDI. Shuning uchun rasm
 * [fetchFaceImage] orqali blob sifatida olinadi.
 */

/** Urinish holati: avtomatik tasdiqlangan | rad etilgan | admin qaroriga qolgan. */
export type FaceCheckStatus = 'approved' | 'rejected' | 'pending'

/** Bitta kirish urinishi (selfi tekshiruvi). */
export interface FaceCheck {
  id: string
  studentId: string
  studentName: string
  /** "yyyy-MM-ddTHH:mm:ss" (server vaqti) */
  createdAt: string
  status: FaceCheckStatus
  /** O'zbekcha sabab — backend `FaceMatch` da yozilgan tayyor matn (tarjima qilinmaydi). */
  reason: string
  /** Kosinus o'xshashligi 0..1. Solishtirish bo'lmagan holatda (masalan `pending`) — null. */
  score: number | null
  /** Selfi manzili ("/uploads/..."). */
  imageUrl: string
  deviceId: string
  deviceName: string
  platform: string
  appVersion: string
  ip: string
  modelVersion: string
  /** Kadr sifati — JSON MATN. Buzuq/bo'sh bo'lishi mumkin, `parseQuality` bilan o'qiladi. */
  quality: string
  /** Tasdiqlash mumkinmi (faqat `pending` VA vektori saqlangan urinishda). */
  canApprove: boolean
}

/** Ishonchli qurilma — bir marta tasdiqlangan telefon (qayta selfi so'ralmaydi). */
export interface FaceDevice {
  id: string
  userId: string
  studentId: string
  studentName: string
  deviceId: string
  deviceName: string
  platform: string
  /** Birinchi kirish */
  createdAt: string
  /** Oxirgi faollik */
  lastSeenAt: string
  /** Bekor qilingan bo'lsa vaqti; aks holda bo'sh/null. */
  revokedAt: string | null
}

/** Etalon holati (o'quvchi bo'yicha). Etalon bo'lmasa server 404 beradi → `null`. */
export interface FaceProfile {
  studentId: string
  studentName: string
  modelVersion: string
  /** Etalon qayerdan olingan: `photo` — profil rasmidan, `admin` — admin tasdiqlagan selfidan. */
  source: string
  sampleUrl: string
  /** Vektor o'lchami (512 — ArcFace, 128 — FaceNet). */
  dim: number
  createdAt: string
  updatedAt: string
}

export interface FaceSettings {
  /** O'chirilsa BUTUN modul ishlamaydi — yangi qurilmadan selfisiz kiriladi. */
  enabled: boolean
  /** Kosinus chegarasi 0.05..0.99 (server shu oraliqqa qisadi). Past = ko'proq o'tkazadi. */
  threshold: number
  /** Ilovadagi model versiyasi bilan AYNAN mos bo'lishi shart. */
  modelVersion: string
  /** O'quvchi boshiga saqlanadigan oxirgi selfilar soni (1..100) — maxfiylik uchun. */
  keepChecks: number
}

export interface FaceCheckFilters {
  status?: FaceCheckStatus | ''
  studentId?: string
  /** "yyyy-MM-dd" */
  from?: string
  /** "yyyy-MM-dd" — server kunning oxirigacha cho'zadi */
  to?: string
  /** Server chegarasi 500 (`AdminFaceController.MaxLimit`). */
  limit?: number
}

/** Server bir so'rovda qaytaradigan eng ko'p yozuv (`AdminFaceController.MaxLimit`). */
export const FACE_MAX_LIMIT = 500

/** Sozlamalar yuklanmaguncha ishlatiladigan xavfsiz qiymatlar (modul O'CHIQ deb hisoblanadi). */
export const DEFAULT_FACE_SETTINGS: FaceSettings = {
  enabled: false,
  threshold: 0.6,
  modelVersion: '',
  keepChecks: 5,
}

/** Bo'sh qiymatlarni tashlab, faqat to'ldirilgan filtrlarni yuboradi. */
function clean(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}

// ---------- Urinishlar ----------

export async function getFaceChecks(filters: FaceCheckFilters = {}): Promise<FaceCheck[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<FaceCheck[]>('/admin/face/checks', { params: clean(filters) })
  return data
}

/**
 * Tab yorlig'idagi son — KUTILAYOTGAN urinishlar.
 *
 * ⚠️ Backendda kitoblar bo'limidagi `pending-count` kabi ALOHIDA endpoint YO'Q, shuning uchun
 * son ro'yxatning uzunligidan olinadi. Server chegarasi 500 — undan ko'p bo'lsa `atLimit`
 * bayrog'i ko'tariladi va UI "500+" deb ko'rsatadi (jimgina noto'g'ri son chiqmasin).
 */
export async function getFacePendingCount(): Promise<{ count: number; atLimit: boolean }> {
  const rows = await getFaceChecks({ status: 'pending', limit: FACE_MAX_LIMIT })
  return { count: rows.length, atLimit: rows.length >= FACE_MAX_LIMIT }
}

/**
 * Kutilayotgan urinishni TASDIQLAYDI — o'sha selfi ETALON bo'lib saqlanadi va keyingi
 * kirishlar shunga solishtiriladi.
 */
export async function approveFaceCheck(id: string, note?: string): Promise<void> {
  if (USE_MOCK) {
    await delay()
    return
  }
  await api.post(`/admin/face/checks/${id}/approve`, { note })
}

/** Rad etish. Izoh bo'sh bo'lsa server standart sabab yozadi ("Administrator rad etdi"). */
export async function rejectFaceCheck(id: string, note?: string): Promise<void> {
  if (USE_MOCK) {
    await delay()
    return
  }
  await api.post(`/admin/face/checks/${id}/reject`, { note })
}

// ---------- Ishonchli qurilmalar ----------

export async function getFaceDevices(studentId?: string): Promise<FaceDevice[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<FaceDevice[]>('/admin/face/devices', {
    params: clean({ studentId }),
  })
  return data
}

/** Qurilmani bekor qiladi (telefon yo'qolganda) — o'sha qurilmada yana selfi so'raladi. */
export async function revokeFaceDevice(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay()
    return
  }
  await api.post(`/admin/face/devices/${id}/revoke`)
}

// ---------- Etalon ----------

/** Etalon holati. Etalon bo'lmasa server 404 qaytaradi — bu XATO EMAS, `null` bo'ladi. */
export async function getFaceProfile(studentId: string): Promise<FaceProfile | null> {
  if (USE_MOCK) {
    await delay()
    return null
  }
  try {
    const { data } = await api.get<FaceProfile>(`/admin/face/profile/${studentId}`)
    return data
  } catch (err) {
    if ((err as { response?: { status?: number } }).response?.status === 404) return null
    throw err
  }
}

/** Etalonni tozalaydi — o'quvchi keyingi kirishda qaytadan ro'yxatdan o'tadi. */
export async function deleteFaceProfile(studentId: string): Promise<void> {
  if (USE_MOCK) {
    await delay()
    return
  }
  await api.delete(`/admin/face/profile/${studentId}`)
}

// ---------- Sozlamalar ----------

export async function getFaceSettings(): Promise<FaceSettings> {
  if (USE_MOCK) {
    await delay()
    return { ...DEFAULT_FACE_SETTINGS }
  }
  const { data } = await api.get<FaceSettings>('/admin/face/settings')
  return data
}

export async function saveFaceSettings(payload: FaceSettings): Promise<FaceSettings> {
  if (USE_MOCK) {
    await delay()
    return payload
  }
  const { data } = await api.put<FaceSettings>('/admin/face/settings', payload)
  return data
}

// ---------- Selfi rasmi ----------

/**
 * Selfini AVTORIZATSIYA bilan olib, brauzer ko'rsata oladigan vaqtinchalik manzil qaytaradi.
 *
 * `<img src={imageUrl}>` ISHLAMAYDI: rasm `/api/admin/face/...` dan beriladi va JWT talab qiladi,
 * brauzer esa `<img>` so'roviga `Authorization` sarlavhasini qo'shmaydi. Shu sabab rasm blob
 * sifatida olinadi (loyihadagi qo'ng'iroq yozuvi/shartnoma bilan bir xil naqsh).
 *
 * ⚠️ Chaqiruvchi ishi tugagach `URL.revokeObjectURL` qilishi SHART — aks holda ro'yxatni
 * varaqlagan sari brauzer xotirasida rasmlar to'planib qoladi.
 */
export async function fetchFaceImage(url: string): Promise<string> {
  if (USE_MOCK) {
    await delay(100)
    return ''
  }
  // Backend to'liq yo'l qaytaradi (`/api/admin/...`), axios `baseURL` esa allaqachon `/api` —
  // aks holda `/api/api/...` bo'lib ketardi.
  const path = url.startsWith('/api/') ? url.slice(4) : url
  const res = await api.get(path, { responseType: 'blob' })
  return URL.createObjectURL(res.data as Blob)
}
