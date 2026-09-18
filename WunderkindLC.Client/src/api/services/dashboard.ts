import type { AdminDashboard, TodayLessons } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { adminDashboardMock } from '../mock/dashboard'

/** Admin bosh sahifasi uchun barcha ma'lumotlar */
export async function getAdminDashboard(): Promise<AdminDashboard> {
  if (USE_MOCK) {
    await delay()
    return adminDashboardMock
  }
  const { data } = await api.get<AdminDashboard>('/admin/dashboard')
  return data
}

/** Darslar monitoringi — tanlangan sanada (default bugun) har guruh bo'yicha davomat/baho holati */
export async function getTodayLessons(date?: string): Promise<TodayLessons> {
  const { data } = await api.get<TodayLessons>('/admin/dashboard/today-lessons', {
    params: date ? { date } : undefined,
  })
  return data
}

/**
 * Bosh sahifaning 12 ta kartochkasi (server: `DashboardSummaryDto`, hisob — `DashboardSummary`).
 * "Ketganlar" va "birinchi to'lov" — JORIY oy (`month`, "yyyy-MM") bo'yicha.
 */
export interface DashboardSummary {
  month: string
  /** Ochiq (aylantirilmagan) lidlar. */
  orders: number
  /** Bugun yoki keyinroqqa belgilangan, natijasi hali yozilmagan sinov darsi bor lidlar. */
  firstLesson: number
  /** Sinovdagi (trial) a'zoligi bor o'quvchilar. */
  newStudents: number
  /** Faol (active) a'zoligi bor o'quvchilar. */
  activeStudents: number
  /** Shu oyda o'chirilgan (arxivga tushgan) lidlar. */
  ordersLeft: number
  /** Shu oyda kursdan ketgan, aktivlashmagan o'quvchilar. */
  newLeft: number
  /** Shu oyda kursdan ketgan, aktivlashgan o'quvchilar. */
  activeLeft: number
  debtors: number
  groups: number
  /** Birinchi o'quv to'lovi shu oyda bo'lganlar. */
  firstPayments: number
  /** Faqat muzlatilgan a'zoliklari qolgan o'quvchilar. */
  frozen: number
  /** Arxivlangan o'quvchilar. */
  archived: number
}

/** Bosh sahifa kartochkalari. */
export async function getDashboardSummary(): Promise<DashboardSummary> {
  const { data } = await api.get<DashboardSummary>('/admin/dashboard/summary')
  return data
}
