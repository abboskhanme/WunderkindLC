import { api } from '../client'
import type {
  LevelTestListItem,
  LevelTestDetail,
  LevelTestSubmission,
  LevelTestPayload,
  DayCount,
  LeadStageCount,
} from '@/types'
import type { CrmOverview } from './leads'

/** Daraja testlari ro'yxati. */
export async function getLevelTests(): Promise<LevelTestListItem[]> {
  const { data } = await api.get<LevelTestListItem[]>('/admin/level-tests')
  return data
}

/** Bitta testning to'liq tafsiloti (savollar + diapazonlar). */
export async function getLevelTest(id: string): Promise<LevelTestDetail> {
  const { data } = await api.get<LevelTestDetail>(`/admin/level-tests/${id}`)
  return data
}

/** Yangi test yaratish. */
export async function createLevelTest(payload: LevelTestPayload): Promise<LevelTestDetail> {
  const { data } = await api.post<LevelTestDetail>('/admin/level-tests', payload)
  return data
}

/** Testni yangilash. */
export async function updateLevelTest(id: string, payload: LevelTestPayload): Promise<LevelTestDetail> {
  const { data } = await api.put<LevelTestDetail>(`/admin/level-tests/${id}`, payload)
  return data
}

/** Testni o'chirish. */
export async function deleteLevelTest(id: string): Promise<void> {
  await api.delete(`/admin/level-tests/${id}`)
}

/** Test natijalari (topshirganlar — har biri CRM'da lid). */
export async function getLevelTestSubmissions(id: string): Promise<LevelTestSubmission[]> {
  const { data } = await api.get<LevelTestSubmission[]>(`/admin/level-tests/${id}/submissions`)
  return data
}

/** Topshiruvchi: aktiv o'quvchi bo'ldimi + qaysi guruh(lar)ga qo'shilgan va o'qituvchisi (FISH).
 * isDeleted — lid o'chirilgan yoki o'quvchi o'chirilgan/arxivlangan. */
export interface LevelTestStatRow {
  submissionId: string
  fullName: string
  phone: string
  level: string
  percent: number
  createdAt: string
  leadId: string
  studentId: string | null
  active: boolean
  groupName: string
  teacherName: string
  isDeleted: boolean
  /** Lidning HOZIRGI kanban bosqichi (lid formalari bilan bir xil manba — `LeadOutcome`). */
  stageTitle: string
  stageColor: string
  /** SOTUV natijasi: odam pul to'ladimi (to'lov − vozvrat > 0). */
  paid: boolean
  paidTotal: number
  firstPaidAt: string
}
export interface LevelTestStats {
  /** Topshiriqlar soni (bir odam ikki marta topshirsa — 2). */
  total: number
  /** ⚠️ Quyidagi uchtasi TAKRORSIZ lidlar bo'yicha — `leads` ular uchun maxraj. */
  active: number
  paid: number
  revenue: number
  leads: number
  rows: LevelTestStatRow[]
}
export async function getLevelTestStats(id: string): Promise<LevelTestStats> {
  const { data } = await api.get<LevelTestStats>(`/admin/level-tests/${id}/stats`)
  return data
}

/** Bu testga yuborilgan bir martalik havolalar (lid + SMS holati + ishlangani). */
export interface LevelTestInvite {
  id: string
  testId: string
  leadId: string
  leadName: string
  phone: string
  smsStatus: string
  createdAt: string
  used: boolean
  usedAt: string
  percent: number
  level: string
}
export async function getLevelTestInvites(id: string): Promise<LevelTestInvite[]> {
  const { data } = await api.get<LevelTestInvite[]>(`/admin/level-tests/${id}/invites`)
  return data
}

/**
 * BARCHA daraja testlari bo'yicha umumiy statistika — "Formalar → Test statistikasi" sahifasi.
 * Voronka lid formalaridagi bilan bir xil o'qiladi: topshirdi → lid → o'quvchi → TO'LADI,
 * foizlar TAKRORSIZ lidlar bo'yicha.
 */
export interface LevelCount { level: string; count: number }
/** Bitta test bo'yicha voronka qatori. */
export interface TestStatRow {
  testId: string
  title: string
  isActive: boolean
  submissions: number
  invites: number
  invitesUsed: number
  avgPercent: number
  /** Takrorsiz lidlar (bir odam ikki marta topshirsa ham bitta). */
  leads: number
  converted: number
  activeStudents: number
  /** Pul to'lagan lidlar soni va ular keltirgan sof summa. */
  paid: number
  revenue: number
  convertRate: number
  /** SOTUV konversiyasi — lidlarning necha foizi haqiqatan to'ladi. */
  payRate: number
}
/** Umumiy statistikadagi bitta topshiruvchi — qaysi testga tegishli + natija + hozirgi holati. */
export interface LevelTestOverallRow {
  submissionId: string
  testId: string
  testTitle: string
  fullName: string
  phone: string
  level: string
  percent: number
  createdAt: string
  leadId: string
  studentId: string | null
  active: boolean
  groupName: string
  teacherName: string
  isDeleted: boolean
  /** Lidning HOZIRGI kanban bosqichi. */
  stageTitle: string
  stageColor: string
  paid: boolean
  paidTotal: number
  firstPaidAt: string
}
export interface LevelTestOverallStats {
  testCount: number
  activeTests: number
  submissions: number
  invites: number
  invitesUsed: number
  avgPercent: number
  leads: number
  converted: number
  active: number
  paid: number
  revenue: number
  byLevel: LevelCount[]
  byTest: TestStatRow[]
  byStage: LeadStageCount[]
  daily: DayCount[]
  /** JAMI topshiruvchilar soni — `rows` esa ko'pi bilan eng yangi 500 tasi (server chegarasi). */
  rowsTotal: number
  rows: LevelTestOverallRow[]
  /** BUTUN CRM manzarasi — lid formalari statistikasidagi bilan AYNAN bir xil blok. */
  overview?: CrmOverview
}
export async function getLevelTestOverallStats(): Promise<LevelTestOverallStats> {
  const { data } = await api.get<LevelTestOverallStats>('/admin/level-tests/overall-stats')
  return data
}

/** Lidga daraja testi havolasini SMS qilib yuborish (bir martalik). */
export async function sendLeadTest(leadId: string, testId: string): Promise<{ ok: boolean; status: string; link: string }> {
  const { data } = await api.post<{ ok: boolean; status: string; link: string }>(
    `/admin/leads/${leadId}/send-test`,
    { testId },
  )
  return data
}
