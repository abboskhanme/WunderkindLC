import { api } from '../client'

/**
 * CHEGIRMALAR — o'quvchiga berilgan chegirmalar REGISTRI va markaz bo'yicha hisoboti.
 *
 * ⚠️ Chegirma MANTIG'I bu yerda EMAS. Pul hisobi avvalgidek `Student.discount*` maydonlariga
 * tayanadi (`TuitionService.DiscountForMonth`); bu modul FAQAT registr (kim, qachon, qancha,
 * nega berdi/bekor qildi) va uning ustidagi hisobot. Batafsil: `.claude/rules/discounts.md`.
 */

/** `active` — hozir amaldagi yagona yozuv; `replaced` — yangisi bilan almashtirilgan; `cancelled` — bekor qilingan. */
export type StudentDiscountStatus = 'active' | 'replaced' | 'cancelled'

/** Bitta chegirma yozuvi (registr qatori). */
export interface StudentDiscountItem {
  id: string
  studentId: string
  /** SNAPSHOT — o'quvchi arxivlansa ham tarix o'qiladi. */
  studentName: string
  /** null/'' — BARCHA guruh hisoblariga; to'ldirilgan bo'lsa faqat o'sha guruhga. */
  groupId: string | null
  /** SNAPSHOT — guruh o'chirilsa ham qoladi ('' — barcha guruhlar). */
  groupName: string
  teacherId: string | null
  /** SNAPSHOT — chegirma BERILGAN paytdagi guruh o'qituvchisi. */
  teacherName: string
  /** Foiz (0..100). Avval foiz olib tashlanadi, keyin `amount` ayriladi. */
  pct: number
  /** Aniq summa (so'm). */
  amount: number
  /** "yyyy-MM" yoki '' (cheklovsiz). */
  startMonth: string
  endMonth: string
  /** Sabab/izoh (`Student.discountNote` bilan bir xil matn). */
  reason: string
  status: StudentDiscountStatus
  statusLabel: string
  /** Hozir HAQIQATAN amalda: `status === 'active'` VA joriy oy davr ichida. */
  inForce: boolean
  createdAt: string
  createdBy: string
  /** Yopilgan/bekor qilingan vaqt ('' — hali amalda). */
  endedAt: string
  endedBy: string
  cancelReason: string
}

/** Oyda HAQIQATAN qo'llangan chegirma (MonthlyCharge — pul haqiqati). */
export interface StudentDiscountMonth {
  month: string
  groupId: string | null
  groupName: string
  teacherName: string
  /** Oy uchun hisoblangan to'liq summa. */
  charged: number
  /** Shu oyda berilgan chegirma summasi. */
  discount: number
}

export interface StudentDiscountsResponse {
  /** Registr qatorlari — eng yangisi birinchi. */
  items: StudentDiscountItem[]
  /** Oylar bo'yicha HAQIQATAN qo'llangan chegirma (eng yangi oy birinchi). */
  applied: StudentDiscountMonth[]
  /** `applied` yig'indisi — "shu o'quvchiga jami qancha chegirma berilgan". */
  totalDiscount: number
  /** Hozir amaldagi yozuv (yo'q bo'lsa null). */
  current: StudentDiscountItem | null
  /** Joriy oy uchun beriladigan chegirma summasi (so'm). */
  currentMonthDiscount: number
}

/** Yaratish/tahrirlash tanasi. */
export interface StudentDiscountPayload {
  pct: number
  amount: number
  startMonth?: string
  endMonth?: string
  reason?: string
  /** '' yoki null — barcha guruhlarga. */
  groupId?: string | null
}

/**
 * O'quvchining chegirma registri.
 *
 * `applyCurrentMonth` — joriy oy hisobiga DARHOL qo'llansinmi (aks holda keyingi oydan).
 * Bu `PUT /students/{id}?applyDiscount=true` bilan AYNAN bir xil mantiq.
 */
export async function getStudentDiscounts(studentId: string): Promise<StudentDiscountsResponse> {
  const { data } = await api.get<StudentDiscountsResponse>(`/admin/students/${studentId}/discounts`)
  return data
}

export async function createStudentDiscount(
  studentId: string,
  payload: StudentDiscountPayload,
  applyCurrentMonth = true,
): Promise<StudentDiscountsResponse> {
  const { data } = await api.post<StudentDiscountsResponse>(
    `/admin/students/${studentId}/discounts`,
    payload,
    { params: { applyCurrentMonth } },
  )
  return data
}

export async function updateStudentDiscount(
  studentId: string,
  id: string,
  payload: StudentDiscountPayload,
  applyCurrentMonth = true,
): Promise<StudentDiscountsResponse> {
  const { data } = await api.put<StudentDiscountsResponse>(
    `/admin/students/${studentId}/discounts/${id}`,
    payload,
    { params: { applyCurrentMonth } },
  )
  return data
}

/** Chegirmani BEKOR qilish — yozuv o'chmaydi, `cancelled` bo'ladi va o'quvchidan chegirma olinadi. */
export async function cancelStudentDiscount(
  studentId: string,
  id: string,
  reason: string,
  applyCurrentMonth = true,
): Promise<StudentDiscountsResponse> {
  const { data } = await api.post<StudentDiscountsResponse>(
    `/admin/students/${studentId}/discounts/${id}/cancel`,
    { reason },
    { params: { applyCurrentMonth } },
  )
  return data
}

/* ══════════════════════ HISOBOT ══════════════════════ */

export interface DiscountReportSummary {
  /** Hozir amaldagi chegirma yozuvlari soni. */
  activeCount: number
  /** Chegirmasi bor (arxivlanmagan) o'quvchilar soni. */
  studentCount: number
  /** Markazdagi arxivlanmagan o'quvchilar soni — ulush uchun. */
  totalStudents: number
  /** Chegirmali o'quvchilar ulushi (%). */
  studentSharePct: number
  /** Davr bo'yicha jami hisoblangan summa. */
  periodCharged: number
  /** Davr bo'yicha jami chegirma. */
  periodDiscount: number
  /** Chegirma ulushi (%) = periodDiscount / periodCharged. */
  sharePct: number
  /** Joriy oyda beriladigan chegirma summasi. */
  currentMonthDiscount: number
}

export interface DiscountReportMonth {
  month: string
  charged: number
  discount: number
  /** Shu oyda chegirma olgan o'quvchilar soni. */
  students: number
}

export interface DiscountReportTeacher {
  teacherId: string
  teacherName: string
  charged: number
  discount: number
  students: number
  groups: number
}

export interface DiscountReportGroup {
  groupId: string
  groupName: string
  teacherName: string
  courseName: string
  charged: number
  discount: number
  students: number
}

export interface DiscountReportStudent {
  studentId: string
  studentName: string
  charged: number
  discount: number
  /** Nechta oyda chegirma olgan. */
  months: number
  groupNames: string[]
  teacherNames: string[]
  /** Hozirgi amaldagi chegirma (yo'q bo'lsa 0/''). */
  pct: number
  amount: number
  reason: string
  hasActive: boolean
}

export interface DiscountReportReason {
  /** '' — sababsiz. */
  reason: string
  count: number
  discount: number
}

export interface DiscountReport {
  from: string
  to: string
  summary: DiscountReportSummary
  months: DiscountReportMonth[]
  byTeacher: DiscountReportTeacher[]
  byGroup: DiscountReportGroup[]
  byStudent: DiscountReportStudent[]
  byReason: DiscountReportReason[]
  /** Hozir amaldagi chegirmalar ro'yxati (registrdan). */
  active: StudentDiscountItem[]
}

/** Markaz bo'yicha chegirmalar hisoboti. `from`/`to` — "yyyy-MM" (inklyuziv). */
export async function getDiscountReport(from: string, to: string): Promise<DiscountReport> {
  const { data } = await api.get<DiscountReport>('/admin/reports/discounts', { params: { from, to } })
  return data
}
