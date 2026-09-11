import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * KURSLAR ANALITIKASI — "O'quv bo'limi → Kurslar analitikasi" sahifasining ma'lumoti.
 * Butun hisob-kitob serverda (`CourseAnalytics`), klient faqat chizadi.
 */

export interface CourseMonthFlow {
  /** "yyyy-MM" */
  month: string
  /** Kursga KELGAN (sinovdagilar ham) */
  joined: number
  /** Shu oyda BIRINCHI marta aktivlashgan (to'lov boshlangan) */
  activated: number
  /** KETGAN — haqiqiy churn (kursni tugatgan emas) */
  left: number
  /** Kursni TUGATGAN (sertifikat bilan) */
  completed: number
  /** Oy oxirida faol bo'lgan o'quvchilar */
  activeEnd: number
}

export interface CourseAnalyticsRow {
  courseId: string
  courseName: string
  price: number
  groups: number
  teachers: number
  active: number
  trial: number
  frozen: number
  /** Hozir kursda bo'lgan takrorsiz o'quvchilar (faol + sinov + muzlatilgan) */
  students: number
  /** Kursda biror payt o'qigan takrorsiz o'quvchilar */
  totalEver: number
  monthlyRevenue: number
  monthly: CourseMonthFlow[]
}

export interface CourseOverlap {
  totalStudents: number
  oneCourse: number
  multiCourse: number
  buckets: { courses: number; students: number }[]
  pairs: { aId: string; aName: string; bId: string; bName: string; students: number }[]
}

export interface CourseAnalytics {
  months: string[]
  courses: CourseAnalyticsRow[]
  overlap: CourseOverlap
  activeStudents: number
  totalGroups: number
  monthlyRevenue: number
}

const empty: CourseAnalytics = {
  months: [],
  courses: [],
  overlap: { totalStudents: 0, oneCourse: 0, multiCourse: 0, buckets: [], pairs: [] },
  activeStudents: 0,
  totalGroups: 0,
  monthlyRevenue: 0,
}

/** Butun analitika bitta so'rovda. `months` — nechta oy ko'rsatilsin (1..36). */
export async function getCourseAnalytics(months = 12): Promise<CourseAnalytics> {
  if (USE_MOCK) {
    await delay()
    return empty
  }
  const { data } = await api.get<CourseAnalytics>('/admin/course-analytics', { params: { months } })
  return data
}
