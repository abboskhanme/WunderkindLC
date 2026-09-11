import { api } from '../client'
import type {
  SubstituteTeacherAssignment,
  SubstituteAssignmentsResult,
  SubstitutePreview,
  CreateSubstituteAssignmentPayload,
  GroupLessonDate,
} from '@/types'

export interface GetSubstituteAssignmentsParams {
  groupId?: string
  teacherId?: string
  date?: string
  isActive?: boolean
  /** Nechta qator so'ralsin. Berilmasa server standarti (`MaxRows = 500`) ishlaydi. */
  limit?: number
}

/**
 * Javob sarlavhasidan musbat butun sonni o'qish.
 *
 * Sarlavha nomi katta-kichik harfga sezgir EMAS (HTTP qoidasi), axios esa ularni kichik
 * harfda beradi — shuning uchun kalit ham kichik harfda qidiriladi. Sarlavha yo'q/buzuq
 * bo'lsa `null` qaytadi va chaqiruvchi zaxira qiymatga tushadi.
 */
function headerCount(headers: unknown, name: string): number | null {
  const raw = (headers as Record<string, unknown> | undefined)?.[name]
  if (typeof raw !== 'string' && typeof raw !== 'number') return null
  const n = Number(raw)
  return Number.isFinite(n) && n >= 0 ? Math.trunc(n) : null
}

/**
 * O'rinbosar o'qituvchilar tayinlovlari ro'yxati.
 *
 * ⚠️ Server javobi CHEGARALANGAN (`MaxRows = 500`), lekin javob TANASI ilgarigidek yalang
 * MASSIV bo'lib qoladi (mavjud klientlar buzilmasin) — jami son esa `X-Total-Count` va
 * `X-Returned-Count` SARLAVHALARIDA keladi. Shuning uchun `total` ni tanadan emas,
 * SARLAVHADAN o'qiymiz: aks holda "jami" har doim ko'rsatilgan qatorlar soniga teng bo'lib,
 * 500 talik cheklov foydalanuvchidan YASHIRIN qolardi (loyiha qoidasi: cheklov ochiq yoziladi).
 *
 * SPA va API BITTA originda (`baseURL = '/api'`, dev'da Vite proxy) — ya'ni bu sarlavhalarni
 * o'qish uchun CORS `Access-Control-Expose-Headers` KERAK EMAS.
 *
 * Sarlavha kelmasa (eski backend) yoki tana `{ items, total }` shaklida kelsa ham ishlaydi —
 * shakl o'zgarsa sahifa bo'shab qolmasin.
 */
export async function getSubstituteAssignments(
  params?: GetSubstituteAssignmentsParams
): Promise<SubstituteAssignmentsResult> {
  const res = await api.get<SubstituteTeacherAssignment[] | SubstituteAssignmentsResult>(
    '/admin/substitute-teachers',
    { params }
  )
  const data = res.data
  const items = Array.isArray(data) ? data : (data?.items ?? [])
  const bodyTotal = Array.isArray(data) ? null : (data?.total ?? null)
  const total = headerCount(res.headers, 'x-total-count') ?? bodyTotal ?? items.length
  return { items, total }
}

/** ID bo'yicha tayinlovni olish */
export async function getSubstituteAssignmentById(id: string): Promise<SubstituteTeacherAssignment> {
  const { data } = await api.get<SubstituteTeacherAssignment>(`/admin/substitute-teachers/${id}`)
  return data
}

/** Yangi o'rinbosar o'qituvchi biriktirish */
export async function createSubstituteAssignment(
  payload: CreateSubstituteAssignmentPayload
): Promise<SubstituteTeacherAssignment> {
  const { data } = await api.post<SubstituteTeacherAssignment>('/admin/substitute-teachers', payload)
  return data
}

/** Tayinlovni bekor qilish */
export async function cancelSubstituteAssignment(id: string): Promise<{ message: string }> {
  const { data } = await api.delete<{ message: string }>(`/admin/substitute-teachers/${id}`)
  return data
}

/** O'qituvchi ilovasi uchun — me'yoriy o'rinbosarliklar ro'yxatini olish */
export async function getMySubstitutions(): Promise<SubstituteTeacherAssignment[]> {
  const { data } = await api.get<SubstituteTeacherAssignment[]>('/teacher/substitutions')
  return data
}

/** Guruhning oydagi dars kunlarini olish (modal uchun) */
export async function getGroupLessonDates(
  groupId: string,
  month: string
): Promise<GroupLessonDate[]> {
  const { data } = await api.get<GroupLessonDate[]>(
    '/admin/substitute-teachers/group-lesson-dates',
    { params: { groupId, month } }
  )
  return data
}

export interface SubstitutePreviewParams {
  groupId: string
  substituteTeacherId: string
  dates: string[]
}

/**
 * JONLI HISOB-KITOB — biriktirish oynasidagi summalar.
 *
 * Formula SERVERDA (maosh hisobi bilan bitta joyda); klient faqat ko'rsatadi. `dates` bir nechta
 * `?dates=...` parametri bo'lib ketadi (ASP.NET Core massivni shunday o'qiydi).
 */
export async function getSubstitutePreview(
  params: SubstitutePreviewParams
): Promise<SubstitutePreview> {
  const { data } = await api.get<SubstitutePreview>('/admin/substitute-teachers/preview', {
    params: {
      groupId: params.groupId,
      substituteTeacherId: params.substituteTeacherId,
      dates: params.dates,
    },
    // Massiv `?dates=a&dates=b` ko'rinishida ketsin (axios standarti `dates[]=` bo'lardi).
    paramsSerializer: { indexes: null },
  })
  return data
}
