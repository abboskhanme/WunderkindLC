import { api } from '../client'

/**
 * «KETISH SABABLARI» (Retention / Loss) — `GET /admin/retention-loss`.
 *
 * Butun hisob SERVERDA (`RetentionLossReport`) — sahifa faqat chizadi. "Ketdi" ta'rifi bosh
 * sahifadagi «Ketgan o'quvchilar» kartochkasi bilan AYNAN bir xil: kurs ichidagi guruh
 * almashtirish va sertifikat bilan tugatish ketish EMAS. Batafsil: `.claude/rules/retention-loss.md`.
 */

/** Qaysi ketishlar ko'rsatiladi: hammasi | aktivdan | sinovdan. */
export type RetentionKind = 'all' | 'active' | 'trial'

export interface RetentionLossTotals {
  /** Tanlangan tur bo'yicha ketishlar (har (o'quvchi, kurs) ketishi alohida). */
  left: number
  leftActive: number
  leftTrial: number
  /** Takrorsiz o'quvchilar (ikki kursdan ketgan — bitta). */
  students: number
  outOfControl: number
  noReason: number
  /** Davrda AKTIV bo'lgan (o'quvchi, kurs) juftliklari — foiz maxraji. */
  activeBase: number
  /** Aktivdan ketganlar / activeBase × 100; null — maxraj 0. */
  lossRate: number | null
  retention: number | null
}

export interface RetentionLossReason {
  label: string
  count: number
  outOfControl: boolean
}

export interface RetentionLossTeacher {
  /** '' — o'qituvchisi biriktirilmagan guruh(lar). */
  teacherId: string
  teacherName: string
  left: number
  leftActive: number
  leftTrial: number
  activeBase: number
  lossRate: number | null
}

export interface RetentionLossMonth {
  month: string
  leftActive: number
  leftTrial: number
}

export interface RetentionLossRow {
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  courseId: string
  courseName: string
  teacherId: string
  teacherName: string
  /** Ketgan sana ("yyyy-MM-dd"). */
  date: string
  /** false — sinovdan ketgan (hech qachon aktivlashmagan). */
  fromActive: boolean
  reasonId: string | null
  /** '' — sabab ko'rsatilmagan. */
  reason: string
  outOfControl: boolean
  /** log | audit | archive | '' — sabab qayerdan topilgani. */
  reasonSource: string
  /** Hozir boshqa guruhda o'qiyapti — markazdan emas, faqat shu kursdan ketgan. */
  stillStudying: boolean
}

export interface RetentionLossOption {
  id: string
  name: string
}

export interface RetentionLossReport {
  from: string
  to: string
  kind: RetentionKind
  teacherId: string | null
  courseId: string | null
  totals: RetentionLossTotals
  reasons: RetentionLossReason[]
  teachers: RetentionLossTeacher[]
  months: RetentionLossMonth[]
  rows: RetentionLossRow[]
  teacherOptions: RetentionLossOption[]
  courseOptions: RetentionLossOption[]
}

export interface RetentionLossQuery {
  from: string
  to: string
  teacherId?: string
  courseId?: string
  kind?: RetentionKind
}

export async function getRetentionLoss(q: RetentionLossQuery): Promise<RetentionLossReport> {
  const { data } = await api.get<RetentionLossReport>('/admin/retention-loss', {
    params: {
      from: q.from,
      to: q.to,
      teacherId: q.teacherId || undefined,
      courseId: q.courseId || undefined,
      kind: q.kind ?? 'all',
    },
  })
  return data
}
