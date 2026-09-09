import { api } from '../client'

/* =====================================================================================
 *  KPI — xodimlar samaradorligi va oyligi ("Boshqaruv → KPI")
 * =====================================================================================
 *  Bo'lim OPERATSION ma'lumot YARATMAYDI: lid — Lidlarda, to'lov — Kassada, davomat —
 *  Jurnalda qoladi. KPI ularni O'QIYDI va o'ziga tegishli beshta narsani yozadi:
 *  cheklist belgisi · tiket · oy boshi snapshot · oylik natija · uzaytirish hodisasi.
 *
 *  ⚠️ Bu yerdagi tiplar `IntellectCRM.Application/Dtos/KpiDtos.cs` dagi recordlarning
 *  AYNAN nusxasi (ASP.NET default JSON siyosati — camelCase). Server tomonda maydon
 *  qo'shilsa/o'zgarsa shu fayl ham o'zgaradi: kontrakt IKKI joyda ayri ketmasin.
 */

// ---------- Qoidalar (KpiRuleSetJson) ----------

/** Bitta pog'ona: `[from, to]` oralig'i → `coef`. `note` — nima uchun shu koeffitsient. */
export interface KpiTier {
  from: number
  to: number
  coef: number
  note?: string | null
}

/**
 * Rol qoidalari — konstantalar va pog'ona jadvallari. «Qoidalar» sahifasi shuni TAHRIRLAYDI
 * va har saqlash YANGI versiya bo'lib yoziladi (`effectiveFrom` bilan).
 */
export interface KpiRuleSetJson {
  // --- umumiy (ikkala rol) ---
  ticketFine: number
  guarantee: number
  /** Chiquvchida 7 500 000; kiruvchida `null` (shift YO'Q). */
  monthlyCap: number | null
  efficiency: KpiTier[]

  // --- intake_admin / call_operator ---
  contractBase: number
  qualityNorm: number
  qualityPenalty: number
  qualitySevereBelow: number
  qualitySeverePenalty: number
  leadFloor: number
  planLeadToTrial: number
  planTrialToContract: number
  workDays: number
  touchesPerLead: number
  monthlyContractPlan: number
  ticketStepThreshold: number
  ticketStepPenalty: number
  conversion: KpiTier[]

  // --- retention_admin ---
  activeStudentBonus: number
  extensionBonus: number
  debtRate: number
  debtRateNorm: number
  unknownReasonFine: number
  retention: KpiTier[]
  extension: KpiTier[]

  // --- birlik iqtisodiyoti («Yopish» sahifasidagi 3 indikator) ---
  unitCoursePrice: number
  unitTeacherShare: number
  unitExtensionMonths: number
  unitMaxBonusAShare: number
  unitMaxBonusBShare: number
  unitMaxSalaryShare: number
}

// ---------- Umumiy bo'laklar ----------

/**
 * Bitta KIRISH raqami + "qayerdan olindi" izohi.
 *
 * `source` — matn ("Lidlar: yaratilgan sana oy ichida"), `link` — MAVJUD sahifaga manzil.
 * ⚠️ `estimated` — oy boshi snapshoti yo'q, qiymat JONLI hisobdan TIKLANGAN (taxminiy).
 */
export interface KpiInputDto {
  key: string
  label: string
  value: number
  source: string
  link: string | null
  auto: boolean
  estimated: boolean
}

/** Bitta koeffitsient: xom qiymat, tanlangan pog'ona koeffitsienti va NIMA UCHUN shunday. */
export interface KpiCoefDto {
  key: string
  label: string
  /** Koeffitsient TANLANGAN xom qiymat (masalan konversiya 0.3286). */
  value: number
  coef: number
  note: string
}

/** Oylik hisobning bitta PUL qatori (oklad, bonus A/B/C, jarima, jami). */
export interface KpiMoneyLineDto {
  key: string
  label: string
  amount: number
  note: string | null
}

/** 5-QADAM nazorati: reja bilan solishtirish (faqat `intake_admin`). */
export interface KpiPlanDto {
  planContracts: number
  planDone: number
  /** Lid oqimi kafolati ishladimi (lid < `leadFloor` → reja PASAYADI). */
  leadFloorApplied: boolean
  leadFloor: number
  dailyLeads: number
  dailyTouches: number
  dailyTrials: number
  dailyContracts: number
}

/** Bitta xodimning bitta oydagi TO'LIQ hisobi — «Oy» va «Yopish» sahifalari uchun. */
export interface KpiMonthDto {
  userId: string
  userName: string
  roleCode: string
  roleLabel: string
  month: string
  inputs: KpiInputDto[]
  /**
   * Excel 2-bo'limi: oqimning har bosqichidagi ULUSHLAR (kiruvchi — lid→sinov, sinov→shartnoma,
   * lid→shartnoma; chiquvchi — ketish, uzaytirish, qarz yig'ish).
   *
   * ⚠️ `conversions` va `coefs` ATAYIN ikki ALOHIDA ro'yxat: bu yerda pulga tegmaydigan
   * ko'rsatkich ham bor (masalan lid→shartnoma), u `coef: 1` va tushuntiruvchi `note` bilan
   * keladi. `coefs` esa FAQAT pulni ko'paytiradigan qatorlarni saqlaydi — ya'ni "nima bo'ldi"
   * va "bu qanday baholandi" bir-biriga aralashmaydi.
   */
  conversions: KpiCoefDto[]
  /** FAQAT pulni ko'paytiradigan koeffitsientlar (yuqoridagi izohga qarang). */
  coefs: KpiCoefDto[]
  lines: KpiMoneyLineDto[]
  baseSalary: number
  bonusTotal: number
  fineTotal: number
  /** Kafolat/shift QO'LLANMASDAN oldingi summa. */
  computed: number
  salary: number
  guaranteeApplied: boolean
  /** Shiftdan oshdi — summa KESILMAYDI, faqat bayroq (rahbar qarori). */
  capExceeded: boolean
  cap: number | null
  /** draft | confirmed */
  status: string
  confirmedBy: string | null
  confirmedAt: string | null
  ruleSetId: string | null
  rulesMissing: boolean
  snapshotMissing: boolean
  plan: KpiPlanDto | null
  warning: string | null
}

// ---------- «Bugun» ----------

export interface KpiChecklistItemDto {
  itemId: string
  no: number
  text: string
  norm: string
  kpiTag: string | null
  criterionNo: number | null
  /** Bo'sh bo'lmasa — band TIZIM tomonidan belgilanadi (qo'lda tegilmaydi). */
  autoCheckKey: string | null
  /** done | failed | na */
  state: string
  /** manual | auto */
  source: string
  note: string | null
}

export interface KpiChecklistBlockDto {
  timeBlock: string
  items: KpiChecklistItemDto[]
}

/** Signal chipi: kechikkan topshiriq, javobsiz DM, qaytarilmagan qo'ng'iroq. */
export interface KpiSignalDto {
  key: string
  label: string
  count: number
  /** Rang shkalasi. */
  tone: 'ok' | 'warn' | 'bad'
  link: string | null
}

export interface KpiAlertRowDto {
  id: string
  title: string
  sub: string
  link: string | null
}

/** Erta ogohlantirish ro'yxati (chiquvchi admin uchun). */
export interface KpiAlertListDto {
  key: string
  label: string
  link: string | null
  rows: KpiAlertRowDto[]
}

/** "Shu tezlikda davom etsa oy oxirida…" */
export interface KpiMonthForecastDto {
  salary: number
  progress: number
  note: string
}

/**
 * Bitta KUNLIK norma: reja va fakt YONMA-YON, ikkalasi ham SON.
 *
 * ⚠️ `inverse` — "KAMROQ yaxshiroq" ko'rsatkichi (masalan «ketma-ket 3 dars kelmaganlar»
 * yoki «kechikkan vazifa» — normasi 0). Busiz bunday katak fakt 0 bo'lganda "reja
 * bajarilmadi" bo'lib qizarib turardi.
 */
export interface KpiNormDto {
  key: string
  label: string
  fact: number
  plan: number
  /** O'lchov birligi ("lid", "shartnoma", "daqiqa") — raqam yonida chiqadi. */
  unit: string
  inverse: boolean
  link: string | null
  note: string | null
}

export interface KpiTodayDto {
  userId: string
  userName: string
  roleCode: string
  date: string
  /** Kunlik normalar — reja/fakt juftliklari. */
  norms: KpiNormDto[]
  blocks: KpiChecklistBlockDto[]
  signals: KpiSignalDto[]
  lists: KpiAlertListDto[]
  forecast: KpiMonthForecastDto | null
}

// ---------- Profil va oklad ----------

export interface KpiSalaryDto {
  id: string
  effectiveFrom: string
  baseSalary: number
  note: string | null
  createdBy: string | null
  createdAt: string
}

export interface KpiProfileDto {
  id: string
  userId: string
  userName: string
  position: string
  roleCode: string
  roleLabel: string
  startMonth: string
  guaranteeUntilMonth: string | null
  isActive: boolean
  note: string | null
  currentSalary: number
  salaries: KpiSalaryDto[]
}

export interface SaveKpiProfileRequest {
  id: string | null
  userId: string
  roleCode: string
  startMonth: string
  guaranteeUntilMonth: string | null
  isActive: boolean
  note: string | null
  /** Birinchi oklad — profil yaratilayotganda birga kiritiladi. */
  baseSalary: number | null
  salaryEffectiveFrom: string | null
}

export interface SaveKpiSalaryRequest {
  effectiveFrom: string
  baseSalary: number
  note: string | null
}

export interface KpiRuleSetDto {
  id: string
  roleCode: string
  roleLabel: string
  effectiveFrom: string
  note: string | null
  createdBy: string | null
  createdAt: string
  rules: KpiRuleSetJson
}

export interface SaveKpiRuleSetRequest {
  roleCode: string
  effectiveFrom: string
  note: string | null
  rules: KpiRuleSetJson
}

// ---------- Tiketlar ----------

export interface KpiTicketDto {
  id: string
  userId: string
  userName: string
  date: string
  reasonCode: string
  reasonLabel: string
  criterionNo: number | null
  criterionLabel: string | null
  callId: string | null
  note: string | null
  /** proposed | confirmed | disputed | cancelled */
  status: string
  issuedBy: string | null
  issuedAt: string
  disputeNote: string | null
  resolvedBy: string | null
  resolvedAt: string | null
}

export interface SaveKpiTicketRequest {
  id: string | null
  userId: string
  date: string
  reasonCode: string
  criterionNo: number | null
  callId: string | null
  note: string | null
  status: string
  disputeNote: string | null
}

export interface KpiTicketReasonDto {
  code: string
  label: string
  /** Sabab qaysi ROLGA tegishli (`''` — umumiy). */
  roleCode: string
  criterionNo: number | null
}

/** Yashirin mijoz auditining 13 mezonidan biri. */
export interface KpiCriterionDto {
  no: number
  label: string
}

export interface KpiStaffDto {
  userId: string
  userName: string
  roleCode: string
  roleLabel: string
}

export interface KpiTicketMetaDto {
  reasons: KpiTicketReasonDto[]
  criteria: KpiCriterionDto[]
  staff: KpiStaffDto[]
}

/** Haftalik sifat nazorati uchun tasodifiy qo'ng'iroq namunasi. */
export interface KpiCallSampleDto {
  callId: string
  phoneNumber: string
  /** `Call` entity'sidagi NATIV qiymatlar — qisqartirilmaydi. */
  direction: 'inbound' | 'outbound'
  startedAt: string
  durationSeconds: number
  hasRecording: boolean
  transcript: string | null
  aiAnalysis: string | null
}

// ---------- Yopish ----------

/** Birlik iqtisodiyoti — 3% / 5% / 10% sog'lomlik indikatorlari. */
export interface KpiUnitEconomicsDto {
  coursePrice: number
  teacherShare: number
  marginPerStudent: number
  activeStudents: number
  centerMargin: number

  bonusAUnit: number
  bonusAShare: number
  bonusAMax: number
  bonusAOk: boolean

  bonusBUnit: number
  extensionMargin: number
  bonusBShare: number
  bonusBMax: number
  bonusBOk: boolean

  totalSalaries: number
  salaryShare: number
  salaryMax: number
  salaryOk: boolean
}

export interface KpiCloseDto {
  month: string
  rows: KpiMonthDto[]
  unit: KpiUnitEconomicsDto | null
}

// ---------- Cheklist shabloni ----------

export interface KpiChecklistItemDefDto {
  id: string
  no: number
  timeBlock: string
  text: string
  norm: string
  kpiTag: string | null
  criterionNo: number | null
  autoCheckKey: string | null
  order: number
}

export interface KpiChecklistTemplateDto {
  id: string
  roleCode: string
  roleLabel: string
  name: string
  isActive: boolean
  items: KpiChecklistItemDefDto[]
}

export interface SaveChecklistCheckRequest {
  userId: string
  date: string
  itemId: string
  /** done | failed | na */
  state: string
  note: string | null
}

/** Oy BOSHIDAGI muzlatilgan raqamlar (orqaga tiklab bo'lmaydigan qiymatlar). */
export interface KpiSnapshotDto {
  id: string
  month: string
  roleCode: string
  userId: string | null
  takenAt: string
  values: Record<string, number>
}

/* =====================================================================================
 *  So'rovlar
 * =====================================================================================
 *  ⚠️ `userId` BO'SH bo'lsa server JORIY foydalanuvchini oladi — xodim o'z raqamini
 *  bo'lim ruxsatisiz ham ko'radi (spetsifikatsiya §7). Shuning uchun bo'sh qiymat
 *  so'rovdan OLIB TASHLANADI, `userId=''` bo'lib yuborilmaydi.
 */

/** Bo'sh (undefined/null/"") parametrlarni so'rovdan chiqarib tashlaydi. */
function clean(params: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {}
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue
    out[k] = v
  }
  return out
}

// ---------- Profillar va qoidalar ----------

export async function getKpiProfiles(): Promise<KpiProfileDto[]> {
  const { data } = await api.get<KpiProfileDto[]>('/admin/kpi/profiles')
  return data
}

/**
 * KPI profili BERILISHI mumkin bo'lgan xodimlar.
 *
 * ⚠️ `getKpiProfiles` dan FARQI: u allaqachon profili BORLARNI qaytaradi, bu esa
 * tanlash uchun nomzodlarni. Profilsiz xodimda `roleCode`/`roleLabel` BO'SH bo'ladi —
 * shu bo'yicha ro'yxatda "hali biriktirilmagan" deb belgilanadi.
 */
export async function getKpiProfileCandidates(): Promise<KpiStaffDto[]> {
  const { data } = await api.get<KpiStaffDto[]>('/admin/kpi/profiles/candidates')
  return data
}

export async function saveKpiProfile(req: SaveKpiProfileRequest): Promise<KpiProfileDto> {
  const { data } = await api.post<KpiProfileDto>('/admin/kpi/profiles', req)
  return data
}

export async function deleteKpiProfile(id: string): Promise<void> {
  await api.delete(`/admin/kpi/profiles/${id}`)
}

/** Yangi OKLAD versiyasi — `effectiveFrom` oyidan kuchga kiradi (eskisi tarixda qoladi). */
export async function saveKpiSalary(userId: string, req: SaveKpiSalaryRequest): Promise<KpiSalaryDto> {
  const { data } = await api.post<KpiSalaryDto>(`/admin/kpi/profiles/${userId}/salary`, req)
  return data
}

/** Shu OYDA amal qiladigan qoidalar (`effectiveFrom <= month` bo'lgan eng so'nggi versiya). */
export async function getKpiRules(role: string, month?: string): Promise<KpiRuleSetDto> {
  const { data } = await api.get<KpiRuleSetDto>('/admin/kpi/rules', {
    params: clean({ role, month }),
  })
  return data
}

export async function getKpiRuleHistory(role: string): Promise<KpiRuleSetDto[]> {
  const { data } = await api.get<KpiRuleSetDto[]>('/admin/kpi/rules/history', { params: { role } })
  return data
}

/** Qoidalarning YANGI versiyasi (eskisi TAHRIRLANMAYDI — tarix o'zgarmasin). */
export async function saveKpiRules(req: SaveKpiRuleSetRequest): Promise<KpiRuleSetDto> {
  const { data } = await api.post<KpiRuleSetDto>('/admin/kpi/rules', req)
  return data
}

/** Excel konstantalarini seed qilish — idempotent (yo'q bo'lgani qo'shiladi). */
export async function seedKpiRules(): Promise<{ rules: number; templates: number; items: number }> {
  const { data } = await api.post<{ rules: number; templates: number; items: number }>(
    '/admin/kpi/rules/seed',
  )
  return data
}

// ---------- Oy ----------

export async function getKpiMonth(userId: string, month: string): Promise<KpiMonthDto> {
  const { data } = await api.get<KpiMonthDto>('/admin/kpi/month', {
    params: clean({ userId, month }),
  })
  return data
}

export async function getKpiMonthAll(month: string): Promise<KpiMonthDto[]> {
  const { data } = await api.get<KpiMonthDto[]>('/admin/kpi/month/all', { params: { month } })
  return data
}

// ---------- Bugun ----------

export async function getKpiToday(userId: string, date: string): Promise<KpiTodayDto> {
  const { data } = await api.get<KpiTodayDto>('/admin/kpi/today', {
    params: clean({ userId, date }),
  })
  return data
}

export async function saveChecklistCheck(req: SaveChecklistCheckRequest): Promise<void> {
  await api.post('/admin/kpi/today/check', req)
}

// ---------- Tiketlar ----------

export interface KpiTicketQuery {
  from?: string
  to?: string
  userId?: string
  status?: string
}

export async function getKpiTickets(q: KpiTicketQuery = {}): Promise<KpiTicketDto[]> {
  const { data } = await api.get<KpiTicketDto[]>('/admin/kpi/tickets', { params: clean({ ...q }) })
  return data
}

export async function saveKpiTicket(req: SaveKpiTicketRequest): Promise<KpiTicketDto> {
  if (req.id) {
    const { data } = await api.put<KpiTicketDto>(`/admin/kpi/tickets/${req.id}`, req)
    return data
  }
  const { data } = await api.post<KpiTicketDto>('/admin/kpi/tickets', req)
  return data
}

export async function deleteKpiTicket(id: string): Promise<void> {
  await api.delete(`/admin/kpi/tickets/${id}`)
}

/** Sabablar katalogi + 13 audit mezoni + KPI profili bor xodimlar. */
export async function getKpiTicketMeta(): Promise<KpiTicketMetaDto> {
  const { data } = await api.get<KpiTicketMetaDto>('/admin/kpi/tickets/reasons')
  return data
}

/** Haftalik sifat nazorati: xodimning TASODIFIY qo'ng'iroqlari. */
export async function getKpiCallSamples(userId: string, count = 5): Promise<KpiCallSampleDto[]> {
  const { data } = await api.get<KpiCallSampleDto[]>('/admin/kpi/tickets/calls', {
    params: clean({ userId, count }),
  })
  return data
}

// ---------- Yopish ----------

export async function getKpiClose(month: string): Promise<KpiCloseDto> {
  const { data } = await api.get<KpiCloseDto>('/admin/kpi/close', { params: { month } })
  return data
}

/**
 * Natijani MUZLATISH — tasdiqlangandan keyin raqamlar o'zgarmaydi.
 *
 * ⚠️ `approveOverCap` — yuqori chegaradan OSHGAN summa uchun ONGLI tasdiq. Server bunday
 * qatorni bu bayroqsiz 400 bilan RAD ETADI: summa avtomatik kesilmagani uchun chegaradan
 * oshish rahbarning alohida qarori bo'lishi kerak. Tekshiruv ATAYIN serverda ham bor —
 * faqat klientda qolsa, boshqa har qanday chaqiruvchi uni chetlab o'tardi.
 */
export async function confirmKpiMonth(
  userId: string,
  month: string,
  approveOverCap = false,
): Promise<KpiMonthDto> {
  const { data } = await api.post<KpiMonthDto>(`/admin/kpi/close/${userId}`, null, {
    params: { month, approveOverCap },
  })
  return data
}

export async function reopenKpiMonth(userId: string, month: string): Promise<KpiMonthDto> {
  const { data } = await api.post<KpiMonthDto>(`/admin/kpi/close/${userId}/reopen`, null, {
    params: { month },
  })
  return data
}

export async function getKpiUnitEconomics(month: string): Promise<KpiUnitEconomicsDto> {
  const { data } = await api.get<KpiUnitEconomicsDto>('/admin/kpi/close/unit-economics', {
    params: { month },
  })
  return data
}

/**
 * Oy natijalarini Excel'ga yuklab olish.
 *
 * ⚠️ Fayl nomi javob sarlavhasidan olinadi (`Content-Disposition`) — server nomni o'zi
 * belgilaydi. Topilmasa zaxira nom ishlatiladi, ya'ni yuklash JIMGINA buzilmaydi.
 */
export async function exportKpiClose(month: string): Promise<void> {
  const res = await api.get('/admin/kpi/close/export', {
    params: { month },
    responseType: 'blob',
  })
  const cd = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = match?.[1] ?? `kpi_${month}.xlsx`
  a.click()
  URL.revokeObjectURL(href)
}

// ---------- Cheklist shablonlari va snapshotlar ----------

export async function getChecklistTemplates(role?: string): Promise<KpiChecklistTemplateDto[]> {
  const { data } = await api.get<KpiChecklistTemplateDto[]>('/admin/kpi/checklist/templates', {
    params: clean({ role }),
  })
  return data
}

export async function seedChecklistTemplates(): Promise<{ templates: number; items: number }> {
  const { data } = await api.post<{ templates: number; items: number }>(
    '/admin/kpi/checklist/templates/seed',
  )
  return data
}

export async function updateChecklistItem(
  id: string,
  item: Partial<KpiChecklistItemDefDto>,
): Promise<KpiChecklistItemDefDto> {
  const { data } = await api.put<KpiChecklistItemDefDto>(`/admin/kpi/checklist/items/${id}`, item)
  return data
}

export async function getKpiSnapshots(month: string): Promise<KpiSnapshotDto[]> {
  const { data } = await api.get<KpiSnapshotDto[]>('/admin/kpi/snapshots', { params: { month } })
  return data
}

export async function takeKpiSnapshot(month: string): Promise<KpiSnapshotDto[]> {
  const { data } = await api.post<KpiSnapshotDto[]>('/admin/kpi/snapshots', null, {
    params: { month },
  })
  return data
}
