// Tizimdagi barcha asosiy tiplar

// Sertifikat tiplari API qatlamida yozilgan (yagona manba) — bu yerda faqat qayta ishlatiladi.
import type { TestCertificate } from '@/api/services/testCertificates'

/**
 * Tizim rollari.
 * - `superadmin` — tizim egasi: admin'ning hamma huquqlari + qulflangan amallarni (masalan,
 *   o'quv yili boshlangach guruhlashni) istalgan vaqtda o'zgartira oladi.
 * - `admin` — oddiy administrator. Qulflangan ma'lumotlarni o'zgartira olmaydi.
 */
export type Role = 'superadmin' | 'admin' | 'teacher' | 'student' | 'parent' | 'staff'

export type Gender = 'male' | 'female'

export interface User {
  id: string
  fullName: string
  role: Role
  email?: string
  avatarUrl?: string
  /** Telefon — admin/xodim botda yangi lid xabarnomasini olishi uchun */
  phone?: string
  /** O'qituvchi uchun ochiq bo'limlar (nav filtri); boshqa rollarda bo'lmaydi */
  permissions?: string[]
}

/** O'quvchi/o'qituvchiga biriktirilgan tizim akkaunti (login/parol) */
export interface Credentials {
  /** Tizimga kirish logini (email) */
  login: string
  /** Ochiq parol (admin topshirishi uchun) */
  password: string
  role: Role
}

/* ---------- Admin dashboard ---------- */

export interface AdminStats {
  studentsCount: number
  teachersCount: number
  /** Markaz o'rtacha bahosi (5 ballik tizim) */
  averageGrade: number
  /** Umumiy davomat foizi (0-100); o'tilgan dars bo'lmasa null */
  attendanceRate: number | null
}

export interface ClassPerformance {
  classId: string
  /** Masalan: "9-A" */
  className: string
  /** Guruh o'qituvchisi (grafik x o'qida ko'rsatiladi; guruh nomi hoverda) */
  teacherName?: string
  /** O'rtacha baho (5 ballik) */
  averageGrade: number
  /** Davomat foizi (0-100); o'tilgan dars bo'lmasa null */
  attendanceRate: number | null
}

export interface TopClass {
  id: string
  name: string
  studentsCount: number
  /** Aktiv (Status=="active") a'zolar soni — sinov/muzlatilgan emas */
  activeCount: number
  averageGrade: number
}

export interface StudentBreakdown {
  /** Faol talabalar (status=="active" faol a'zolik) */
  active: number
  /** Aktiv bo'lmagan talabalar */
  inactive: number
  /** Qarzdorlar (Balance < 0) */
  debtors: number
  /** Qarzi yo'q talabalar */
  paid: number
  /** Guruhi bor talabalar */
  withGroup: number
  /** Guruhsiz talabalar */
  withoutGroup: number
}

/** O'qituvchining talaba saqlab qolish statistikasi (lifetime, per-group) */
export interface TeacherPerformance {
  teacherId: string
  teacherName: string
  phone: string
  totalStudents: number
  activeStudents: number
  frozenStudents: number
  leftStudents: number
  retentionPercent: number
  lossPercent: number
  effectivenessScore: number
  groupCount: number
}

/** Bosh sahifa tepasidagi 5 ta asosiy ko'rsatkich */
export interface DashboardHeaderStats {
  /** Jami lidlar soni */
  leads: number
  /** Sinov holatidagi (StudentGroup.Status=="trial") o'quvchilar soni */
  trialStudents: number
  /** Markazdagi barcha aktiv (StudentGroup.Status=="active") o'quvchilar soni */
  active: number
  /** Muzlatilgan (StudentGroup.Status=="frozen") o'quvchilar soni */
  frozen: number
  /** Qarzdorlar (Balance < 0) */
  debtors: number
}

export interface AdminDashboard {
  stats: AdminStats
  classPerformance: ClassPerformance[]
  /** O'rtacha baho bo'yicha eng yuqori guruhlar */
  topClasses: TopClass[]
  /** O'quvchilar bo'yicha taqsimot */
  studentBreakdown: StudentBreakdown
  /** Shu oyda nechta ba'ho kiritilgan */
  totalGradesCount?: number
  /** Bosh sahifa tepasidagi 5 ta asosiy ko'rsatkich */
  header: DashboardHeaderStats
}

/* ---------- Bugungi darslar monitoringi (bosh sahifa) ---------- */
export interface TodayLessonMonitor {
  groupId: string
  groupName: string
  courseName: string
  teacherId: string
  teacherName: string
  room: string
  startTime: string
  endTime: string
  studentsCount: number
  /** Bugun davomat qilinganmi (Conducted dars belgilangan) */
  attendanceDone: boolean
  /** Bugun baho qo'yilganmi (jurnal bahosi yoki mezon belgisi) */
  gradesDone: boolean
}
export interface TodayLessons {
  date: string
  dayIndex: number
  lessons: TodayLessonMonitor[]
}

/* ---------- Markaz (butun o'quv markazi) kunlik AI tahlili ---------- */
export interface CenterPoint {
  label: string
  value: number
}
export interface CenterRevenue {
  expectedThisMonth: number
  collectedThisMonth: number
  outstandingDebt: number
  yesterdayIncome: number
  predictedMonthEnd: number
}
export interface CenterMetrics {
  activeStudents: number
  newLeadsThisMonth: number
  newLeadsYesterday: number
  convertedThisMonth: number
  departedThisMonth: number
  avgGradeThisMonth: number
  avgGradePrevMonth: number
  leadsBySource: CenterPoint[]
  departureReasons: CenterPoint[]
  incomeLast14Days: CenterPoint[]
}
export interface CenterAiNarrative {
  umumiy: string
  tushumTahlili: string
  baholarTahlili: string
  lidlar: string
  ketganlar: string
  xavflar: string[]
  tavsiyalar: string[]
  salomatlik: number
  trend: string
}
export interface CenterAiRecord {
  id: string
  date: string
  createdAt: string
  model: string
  health: number
  ai: CenterAiNarrative
  revenue: CenterRevenue
  metrics: CenterMetrics
}
export interface CenterAiHistoryItem {
  id: string
  date: string
  createdAt: string
  health: number
  summary: string
}
export interface CenterAiResponse {
  ok: boolean
  alreadyToday: boolean
  record: CenterAiRecord | null
  error: string | null
}

/* ---------- O'qituvchi AI tahlili (profil → "AI tahlil" tabi) ---------- */

/** Bir oydagi o'quvchi oqimi: kelgan / aktivlashgan / muzlatilgan / ketgan. */
export interface TeacherFlowPoint {
  month: string
  came: number
  activated: number
  frozen: number
  left: number
}
/** Bir oydagi jurnal intizomi (reja/o'tilgan/belgilanmagan + mavzu/uy vazifa/davomat foizi). */
export interface TeacherJournalMonth {
  month: string
  planned: number
  conducted: number
  missed: number
  topicPct: number
  homeworkPct: number
  attendanceTakenPct: number
  grades: number
  avgGrade: number
}
/** Guruh kesimidagi qisqa ko'rsatkichlar. */
export interface TeacherGroupStat {
  groupId: string
  name: string
  courseName: string
  isArchived: boolean
  active: number
  trial: number
  frozen: number
  left: number
  planned: number
  conducted: number
  missed: number
  avgGrade: number
}
/** Deterministik hisoblangan o'qituvchi ko'rsatkichlari (AI emas — diagramma/jadval uchun). */
export interface TeacherAiMetrics {
  groupCount: number
  activeGroupCount: number
  cameTotal: number
  activeStudents: number
  trialStudents: number
  frozenStudents: number
  leftStudents: number
  retentionPct: number
  lossPct: number
  plannedLessons: number
  conductedLessons: number
  /** Muhlati o'tgan, lekin jurnalda belgilanmagan darslar — "o'z vaqtida to'ldirish" ko'rsatkichi */
  missedLessons: number
  journalDonePct: number
  topicPct: number
  homeworkPct: number
  attendanceTakenPct: number
  gradesCount: number
  avgGradeThisMonth: number
  avgGradePrevMonth: number
  studentAttendancePct: number
  avgBall: number
  testCount: number
  testAvgPct: number
  teacherPresentDays: number
  teacherLateDays: number
  teacherAbsentDays: number
  complaintCount: number
  suggestionCount: number
  flowByMonth: TeacherFlowPoint[]
  journalByMonth: TeacherJournalMonth[]
  departureReasons: CenterPoint[]
  groups: TeacherGroupStat[]
  recentMissedDates: string[]
  /**
   * O'quvchilarning shu o'qituvchi haqida yozilgan fikrlari SONI (oxirgi 12 oy).
   * Matnlarning O'ZI bu yerda YO'Q — ular faqat AI promptiga boradi (maxfiylik).
   */
  studentReviewCount?: number
}
/** AI sohaviy baholari (0-100) — radar diagramma uchun. */
export interface TeacherAiScores {
  jurnal: number
  saqlash: number
  baholash: number
  rivojlanish: number
  faollik: number
  umumiy: number
}
/** AI yozgan narrativ (o'zbekcha) — o'qituvchi tahlilining matn qismlari. */
/* ---------- O'quvchining O'QITUVCHI haqidagi fikri (admin yozadi) ---------- */

/** Bitta yozib qo'yilgan fikr (eng yangisi tepada). */
export interface TeacherReview {
  id: string
  teacherId: string
  groupId: string
  text: string
  /** ISO — yozilgan vaqt */
  createdAt: string
  /** Kim yozgani (admin F.I.Sh) */
  createdBy: string
}

/**
 * O'quvchi profilidagi bitta BLOK: guruh + uning o'qituvchisi + shu o'qituvchi haqida yozilgan
 * fikrlar. O'quvchi 2+ guruhda o'qisa — shunday blok 2+ ta bo'ladi.
 */
export interface StudentTeacherReviewGroup {
  groupId: string
  groupName: string
  courseName: string
  teacherId: string
  teacherName: string
  isActive: boolean
  /** active | trial | frozen | completed */
  membershipStatus: string
  reviews: TeacherReview[]
}

/**
 * O'qituvchi profilidagi «Fikrlar» bo'limi uchun bitta qator — shu o'qituvchi haqida yozilgan
 * fikr, kim (o'quvchi) va qaysi guruh bo'yicha ekani bilan.
 *
 * DIQQAT: bu ADMIN ko'rinishi (o'quvchi ismi bor). O'qituvchining O'ZIGA berilmaydi.
 */
export interface TeacherReviewFeedItem {
  id: string
  studentId: string
  studentName: string
  groupId: string
  groupName: string
  text: string
  createdAt: string
  createdBy: string
}

/** O'qituvchi «Fikrlar» bo'limi: jami soni + qatorlar (eng yangisi tepada). */
export interface TeacherReviewFeed {
  total: number
  items: TeacherReviewFeedItem[]
}

export interface TeacherAiNarrative {
  umumiy: string
  oquvchiOqimi: string
  ketishSabablari: string
  jurnal: string
  rivojlanish: string
  ozgarishlar: string
  kuchli: string[]
  zaif: string[]
  xavflar: string[]
  tavsiyalar: string[]
  baholar: TeacherAiScores
  trend: string
  /**
   * O'QUVCHILAR FIKRI bo'yicha xulosa — admin yozib borgan matnli mulohazalardan AI ajratgan
   * takrorlanuvchi naqshlar. Xom matn va o'quvchi ismi hech qachon ko'rsatilmaydi.
   */
  oquvchilarFikri?: string
}
/** Saqlangan bitta o'qituvchi AI tahlili. */
export interface TeacherAiRecord {
  id: string
  date: string
  createdAt: string
  model: string
  overallScore: number
  ai: TeacherAiNarrative
  metrics: TeacherAiMetrics
}
export interface TeacherAiResponse {
  ok: boolean
  alreadyToday: boolean
  record: TeacherAiRecord | null
  error: string | null
}

/* ---------- Guruh AI tahlili (guruh sahifasi → "AI tahlil" tabi) ---------- */

/** Bir oydagi a'zolik oqimi: kelgan / aktivlashgan / muzlatilgan / ketgan. */
export interface GroupFlowPoint {
  month: string
  came: number
  activated: number
  frozen: number
  left: number
}
/** Bir oydagi guruh ko'rsatkichlari (jurnal + davomat + baho + moliya). */
export interface GroupMonthStat {
  month: string
  planned: number
  conducted: number
  missed: number
  attendancePct: number
  grades: number
  avgGrade: number
  billed: number
  collected: number
}
/** Guruhda o'tkazilgan bitta test/imtihon natijasi. */
export interface GroupTestStat {
  id: string
  name: string
  date: string
  mode: string
  maxScore: number
  scored: number
  studentCount: number
  avgPct: number
}
/** Guruhdagi bitta o'quvchi kesimi. */
export interface GroupStudentStat {
  studentId: string
  fullName: string
  status: string
  ball: number
  avgGrade: number
  attendancePct: number | null
  absent: number
  debt: number
}
/** Deterministik hisoblangan guruh ko'rsatkichlari (AI emas). */
export interface GroupAiMetrics {
  groupName: string
  courseName: string
  teacherName: string
  days: string
  time: string
  startDate: string
  endDate: string
  isArchived: boolean
  capacity: number
  monthlyFee: number
  cameTotal: number
  activeStudents: number
  trialStudents: number
  frozenStudents: number
  leftStudents: number
  retentionPct: number
  lossPct: number
  fillPct: number
  plannedLessons: number
  conductedLessons: number
  /** Muhlati o'tgan, lekin jurnalda belgilanmagan darslar */
  missedLessons: number
  journalDonePct: number
  topicPct: number
  homeworkPct: number
  attendanceTakenPct: number
  attendancePct: number
  absenceCount: number
  lateCount: number
  gradesCount: number
  avgGradeThisMonth: number
  avgGradePrevMonth: number
  avgBall: number
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
  testCount: number
  testAvgPct: number
  /** Moliya ruxsati bo'lmasa false — to'lov raqamlari yig'ilmagan (0) */
  financeIncluded: boolean
  billed: number
  collected: number
  collectionPct: number
  debt: number
  paidCount: number
  unpaidCount: number
  curriculumTotal: number
  curriculumCovered: number
  curriculumRemaining: number
  curriculumFinishDate: string
  flowByMonth: GroupFlowPoint[]
  monthStats: GroupMonthStat[]
  departureReasons: CenterPoint[]
  absenceReasons: CenterPoint[]
  tests: GroupTestStat[]
  students: GroupStudentStat[]
  recentMissedDates: string[]
}
/** Guruh AI baholari (0-100). */
export interface GroupAiScores {
  davomat: number
  barqarorlik: number
  ozlashtirish: number
  tolov: number
  jurnal: number
  umumiy: number
}
/** AI yozgan narrativ (o'zbekcha, tanqidiy). */
export interface GroupAiNarrative {
  umumiy: string
  davomat: string
  oqim: string
  ozlashtirish: string
  imtihonlar: string
  tolovlar: string
  jurnal: string
  ozgarishlar: string
  kuchli: string[]
  zaif: string[]
  xavflar: string[]
  tavsiyalar: string[]
  baholar: GroupAiScores
  trend: string
}
/** Saqlangan bitta guruh AI tahlili. */
export interface GroupAiRecord {
  id: string
  date: string
  createdAt: string
  model: string
  overallScore: number
  ai: GroupAiNarrative
  metrics: GroupAiMetrics
}
export interface GroupAiResponse {
  ok: boolean
  alreadyToday: boolean
  record: GroupAiRecord | null
  error: string | null
}

/* ---------- Lidlar (markazga qiziqqanlar) ---------- */

export type StageColor =
  | 'slate'
  | 'blue'
  | 'emerald'
  | 'amber'
  | 'violet'
  | 'rose'
  | 'cyan'
  | 'orange'

/** Kanban ustuni (lid bosqichi) */
export interface Stage {
  id: string
  title: string
  color: StageColor
}

export interface Lead {
  id: string
  /** Familiya Ism Sharif */
  fullName: string
  gender: Gender
  birthDate: string
  /** O'quvchining o'z telefon raqami */
  phone: string
  /** Otasining FISH */
  fatherFullName: string
  /** Otasining telefon raqami */
  fatherPhone: string
  /** Onasining FISH */
  motherFullName: string
  /** Onasining telefon raqami */
  motherPhone: string
  note?: string
  /** Tegishli ustun (Stage) id'si */
  stage: string
  /** Lid manbasi (Instagram, Referral, Sayt, Telegram, Tashrif, Boshqa) */
  source?: string
  /** Qiziqqan fani / yo'nalishi */
  interestSubject?: string
  /** O'qiydigan TASHQI maktab tumani (District id) */
  districtId?: string
  /** O'qiydigan TASHQI maktab (School id) */
  schoolId?: string
  /** Yaratilgan vaqti (ISO) */
  createdAt?: string
  /** Aylantirilgan o'quvchi id'si (null = hali aylantirilmagan) */
  convertedStudentId?: string | null
  /** Birinchi dars davomat holati: "attended" | "absent" | "no-lesson" */
  firstLessonAttendance?: 'attended' | 'absent' | 'no-lesson'
  /**
   * TAKRORIY murojaatlar soni: odam ommaviy forma yoki daraja testi orqali YANA yozilgan
   * (dublikat lid ochilmaydi — natija shu lidga tushadi). 0/undefined = takror yo'q.
   */
  repeatCount?: number
  /** Oxirgi takroriy murojaat vaqti (ISO) — belgining tooltip'ida ko'rsatiladi */
  lastRepeatAt?: string
}

/**
 * Kunlik oqim (grafik uchun) — LID FORMALARI va DARAJA TESTI statistikasi bir xil shaklda
 * qaytaradi (serverda `DayCountDto`), shu sabab grafik kodi ham bir xil bo'ladi.
 */
export interface DayCount {
  /** "yyyy-MM-dd" */
  date: string
  count: number
}

/**
 * Bosqich kesimi — kelgan lidlar HOZIR kanbanning qaysi ustunida turibdi ("voronka qayerda
 * tiqilib qolgan"). Serverda `LeadStageCountDto`; formalar ham, daraja testi ham shundan.
 */
export interface LeadStageCount {
  stage: string
  color: string
  leads: number
}

/** Lid tarixidagi voqea turi */
export type LeadEventType = 'note' | 'stage' | 'call' | 'trial' | 'convert' | 'created'

/** Lid tarixi (timeline) yozuvi */
export interface LeadEvent {
  id: string
  type: LeadEventType
  text: string
  actorName: string
  createdAt: string
}

/** Sinov darsi natijasi */
export type TrialResult = 'pending' | 'stayed' | 'left'

/** Lidga belgilangan sinov darsi */
export interface TrialLesson {
  id: string
  leadId: string
  groupId: string
  groupName: string
  scheduledAt: string
  result: TrialResult
  createdAt: string
}

/** CRM statistikasi */
export interface CrmStats {
  totalLeads: number
  converted: number
  /** Konversiya foizi (0-100) */
  conversionRate: number
  byStage: { label: string; count: number }[]
  bySource: { label: string; count: number }[]
  monthly: { month: string; created: number; converted: number }[]
  /** Qiziqish fanlari (kurslar) bo'yicha — eng ko'pidan kamiga. */
  byInterest?: {
    label: string
    count: number
    converted: number
    /** Shu fan bo'yicha konversiya foizi (0-100) */
    conversionRate: number
  }[]
}

/* ---------- O'quvchilar ---------- */

/** O'quvchining bitta guruhdagi a'zoligi (ro'yxat/qidiruv uchun; serverda hisoblanadi). */
export interface StudentGroupState {
  groupId: string
  name: string
  /** Guruh o'qituvchisi — filtr guruh NOMI emas, shu id bo'yicha ishlaydi. */
  teacherId: string
  /** active | trial | frozen */
  status: string
  /**
   * «Aktiv muzlatish» — yangi o'quv yiliga o'tishda qilingan muzlatish belgisi.
   * `status` baribir "frozen" bo'lib qoladi (hisob-kitob oddiy muzlatish bilan AYNAN bir xil) —
   * bu bayroq FAQAT ro'yxatda ajratib ko'rsatish uchun.
   */
  yearFreeze: boolean
}

export interface Student {
  id: string
  /** Familiya Ism Sharif — parts'dan join qilinadi (saqlash + qidiruv uchun) */
  fullName: string
  /** Familiya (alohida) */
  lastName?: string
  /** Ism (alohida) */
  firstName?: string
  /** Otasining ismi / sharifi (alohida) */
  middleName?: string
  birthDate: string
  /**
   * O'QUVCHINING RASMI (profil surati) manzili — "/uploads/...".
   * NOMI ESKI: ilgari metrika (tug'ilganlik guvohnomasi) uchun edi, endi rasm sifatida
   * ishlatiladi. Boshqa DTO'larda (`StudentNotebookDto`, `StudentProfileDto`) shu maydon
   * `photoUrl` deb keladi. Batafsil sabab — `Student.BirthCertificateUrl` XML izohida.
   */
  birthCertificateUrl?: string | null
  address: string
  gender: Gender
  /** O'quvchining o'z telefon raqami */
  phone?: string
  /** Otasi F.I.SH */
  fatherFullName?: string
  /** Otasi telefon raqami */
  fatherPhone?: string
  /** Onasi F.I.SH */
  motherFullName?: string
  /** Onasi telefon raqami */
  motherPhone?: string
  /** Ota-onasi FISH — parts'dan join */
  parentFullName: string
  /** Ota-ona familiyasi */
  parentLastName?: string
  /** Ota-ona ismi */
  parentFirstName?: string
  /** Ota-ona otasining ismi / sharifi */
  parentMiddleName?: string
  /** Ota-onasi telefon raqami */
  parentPhone: string
  /** Ota-ona passport rasm/skani manzili */
  parentPassportUrl?: string | null
  /** Joylashuv kengligi (mobil ilovadan GPS) */
  latitude?: number | null
  /** Joylashuv uzunligi */
  longitude?: number | null
  /** Joylashuv manzili (reverse geocode) */
  locationAddress?: string | null
  /** Joylashuv oxirgi yangilangan vaqti (ISO) */
  locationUpdatedAt?: string | null
  /** Arxivlanganmi (o'quvchi markazdan ketgan/chiqarilgan) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
  /** Arxivga olish sababi */
  archiveReason?: string | null
  /** Biriktirilgan asosiy guruh (ClassName) */
  className: string
  /** Tegishli tuman (District id). Bo'sh = tanlanmagan. */
  districtId?: string
  /** Tegishli maktab (School id). Bo'sh = tanlanmagan. */
  schoolId?: string
  /** Tuman nomi (faqat ko'rsatish uchun — backend to'ldiradi). */
  districtName?: string
  /** Maktab nomi/raqami (faqat ko'rsatish uchun — backend to'ldiradi). */
  schoolName?: string
  /** O'quvchi FAOL a'zo bo'lgan barcha guruh nomlari (ro'yxat ko'rinishi uchun) */
  groups?: string[]
  /**
   * Har bir a'zolikning to'liq kesimi: guruh, o'qituvchi va HOLAT.
   * Filtrlar aynan shundan ishlaydi — "falon o'qituvchining AKTIV o'quvchilari" savoliga
   * guruh nomi va o'quvchi darajasidagi `active` bayrog'i javob bera olmaydi.
   */
  groupStates?: StudentGroupState[]
  /** Kursda aktiv — kamida bitta a'zoligi "active" (sinov/muzlatilgan/guruhsiz emas) */
  active?: boolean
  /** A'zolik holati yorlig'i: 'active' | 'trial' | 'yearFrozen' | 'frozen' | '' (guruhsiz).
   *  Bir nechta guruhda turlicha bo'lsa ustunlik: active > trial > yearFrozen > frozen.
   *  `yearFrozen` — muzlatilgan, lekin «Aktiv muzlatish» (yangi o'quv yili) bilan. */
  memberState?: 'active' | 'trial' | 'yearFrozen' | 'frozen' | ''
  /** Login/parol orqali tizimga kirish admin tomonidan cheklanganmi */
  loginBlocked?: boolean
  /** Markazga kelgan (qabul) sanasi (ISO) — oylik to'lov shu oydan boshlanadi */
  enrollmentDate: string
  /** Tizimga kiritilgan vaqt (ISO). "Yangi kiritilgani tepada" tartiblash uchun (eski yozuvda bo'sh). */
  createdAt?: string
  /** Balans (so'm): manfiy = qarzdor, 0 = qarzsiz, musbat = avans */
  balance: number
  /** Chegirma — foiz (0..100). Avval olib tashlanadi, keyin discountAmount ayriladi. */
  discountPct: number
  /** Chegirma — aniq summa (so'm). Foizdan keyin ayriladi. */
  discountAmount: number
  /** Chegirma izohi/sababi (masalan "Aka-uka chegirmasi"). */
  discountNote: string
  /** Chegirma amal qilish boshlanish oyi ("YYYY-MM"). Bo'sh — cheklovsiz (boshidan). */
  discountStartMonth?: string
  /** Chegirma amal qilish tugash oyi ("YYYY-MM"). Bo'sh — cheklovsiz. Ikkalasi bo'sh — har doim. */
  discountEndMonth?: string
  /** Chegirma qaysi GURUHGA tegishli (guruh id). Bo'sh/null — barcha guruh hisoblariga. */
  discountGroupId?: string | null
  /**
   * HOZIR amaldagi chegirmalar SONI — o'quvchi bir NECHTA fanda chegirma olishi mumkin.
   *
   * ⚠️ Yuqoridagi `discount*` maydonlari faqat BITTASINI (asosiysini) ko'rsatadi; ro'yxatdagi
   * belgi «+N ta fan» qismini aynan shu sanoqdan oladi. Eski javobda bo'lmasligi mumkin.
   */
  discountCount?: number
  /** O'quvchi USHLAB TURISH BONUSI tizimiga kiradimi (admin qo'lda belgilaydi). */
  retentionBonus?: boolean
  /** Bonus sanog'i qaysi oydan boshlanadi ("YYYY-MM"). Admin QO'LDA kiritadi; bo'sh = boshlanmagan. */
  retentionBonusStartMonth?: string
}

/* ---------- Tuman + maktab ---------- */

export interface School {
  id: string
  districtId: string
  /** Maktab raqami yoki nomi */
  name: string
  order: number
}

export interface District {
  id: string
  name: string
  order: number
  schools: School[]
}

/* ---------- AI tekshiruv (Speaking / Writing) ---------- */

export interface AiCheckScores {
  grammar: number
  vocabulary: number
  coherence: number
  task: number
  mechanics: number
  pronunciation: number
  fluency: number
}
export interface AiCorrection {
  original: string
  suggestion: string
  explanation: string
}
export interface AiVocab {
  word: string
  suggestion: string
  note: string
}
export interface AiCheckIelts {
  task: number
  coherence: number
  lexical: number
  grammar: number
  overall: number
  taskType: string
}
export interface AiCheckAnalysis {
  overall: number
  level: string
  scores: AiCheckScores
  summary: string
  strengths: string[]
  weaknesses: string[]
  corrections: AiCorrection[]
  vocabulary: AiVocab[]
  improved: string
  recommendations: string[]
  ielts?: AiCheckIelts | null
}
export interface SpeakingWord {
  word: string
  accuracy: number
  errorType: string
}
export interface AiCheckSpeech {
  recognizedText: string
  pronScore: number
  accuracy: number
  fluency: number
  completeness: number
  prosody: number
  words: SpeakingWord[]
}
/** "speaking" | "writing" */
export interface AiCheck {
  id: string
  type: 'speaking' | 'writing'
  prompt: string
  inputText: string
  recognizedText: string
  audioUrl: string
  score: number
  date: string
  createdAt: string
  analysis: AiCheckAnalysis | null
  speech: AiCheckSpeech | null
  taskType: string
}
export interface AiCheckListItem {
  id: string
  type: 'speaking' | 'writing'
  prompt: string
  score: number
  date: string
  createdAt: string
  hasAudio: boolean
}
export interface AiCheckStatus {
  geminiReady: boolean
  azureReady: boolean
  premium: boolean
  blocked: boolean
  limit: number
  usedToday: number
  remaining: number
  /** Markaz bo'limni ilovada OCHGANMI (admin: Ilova → AI check). Kalitlar tayyorligidan
   *  MUSTAQIL: kalit bo'lsa ham, yopiq bo'lsa bo'lim ishlamaydi. */
  enabled: boolean
}
/** Admin: foydalanuvchilar bo'yicha umumiy ko'rinish */
export interface AiCheckOverviewRow {
  studentId: string
  fullName: string
  className: string
  speakingCount: number
  writingCount: number
  total: number
  todayUsed: number
  effectiveLimit: number
  premium: boolean
  blocked: boolean
}

/* ---------- Xonalar ---------- */

export interface Room {
  id: string
  name: string
  capacity: number
  building?: string
  location?: string
  isActive: boolean
  createdAt: string
}

export interface RoomUtilization {
  roomId: string
  roomName: string
  capacity: number
  currentStudents: number
  totalSlots?: number
  gap?: number
  groupCount?: number
  occupancyPercent: number
  activeGroupCount: number
  weeklyActiveHours: number
  weeklyUtilizationPercent: number
  efficiencyScore: number
  efficiencyStatus: string
  building?: string
  location?: string
  groupNames?: string[]
}

/* ---------- Kurslar (fanlar) ---------- */

export interface Subject {
  id: string
  name: string
  /** Kurs narxi (so'm) — guruh oylik to'lovi shundan to'ldiriladi */
  price: number
  /** Bir dars uchun yaxlit narx (so'm) — qisman-oy aktivlashtirishda 12 tadan kam dars
   *  qolganda har bir dars uchun shu summa olinadi. 0 = kiritilmagan (eski pro-rata). */
  lessonPrice?: number
}

/* ---------- Guruhlar ---------- */

export type ClassLanguage = 'uz' | 'ru'

export interface Group {
  id: string
  /** Guruh nomi, masalan "3-A" */
  name: string
  /** Guruh darajasi (1-11), masalan 3 */
  grade: number
  /** O'zbek yoki rus tilidagi guruh */
  language: ClassLanguage
  /** Oylik to'lov (so'm) */
  monthlyFee: number
  /** Xona raqami (matnli, eski — backward compat) */
  room?: string
  /** Xona FK (Room.Id). Yangi guruhlarda shu ishlatiladi. */
  roomId?: string
  /** Guruh arxivlangan (arxivlanganda o'quvchilari ham arxivlanadi) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
  /** VAQTINCHA BLOKLANGAN — guruh O'QITUVCHI ilovasida umuman ko'rinmaydi (ro'yxat, jurnal,
   *  baholash, testlar, chat). Admin panelida esa odatdagidek qoladi; pul/a'zolikka tegmaydi. */
  isBlocked?: boolean
  /** Bloklangan sana (ISO) */
  blockedAt?: string | null
  /** Bloklash izohi (o'qituvchiga ko'rsatilmaydi) */
  blockNote?: string
  /** Guruh holati */
  status?: 'active' | 'full' | 'archived'
  /** Boshlanish/tashkil topgan sanasi (ISO "YYYY-MM-DD") */
  startDate?: string
  /** Tugash sanasi (ISO) */
  endDate?: string
  /** Sig'im (0 = cheksiz) */
  capacity?: number
  /** Biriktirilgan kurs (Subject) id'si */
  courseId?: string
  /** Biriktirilgan o'qituvchi (Teacher) id'si */
  teacherId?: string
  /** Izoh */
  note?: string
  /** Hafta kunlari (0=Dushanba .. 6=Yakshanba) */
  days?: number[]
  /** Dars boshlanish vaqti "HH:mm" */
  startTime?: string
  /** Dars tugash vaqti "HH:mm" */
  endTime?: string
  /** Shu guruh uchun o'qituvchi maoshi rejimi: '' (umumiy) | 'percent' (guruh to'lovidan foiz) | 'fixed' (qat'iy summa) */
  teacherSalaryMode?: string
  /** Foizli bo'lsa — o'qituvchiga beriladigan ulush (%) */
  teacherSalaryPercent?: number
  /** Qat'iy bo'lsa — shu guruh uchun o'qituvchiga beriladigan oylik summa (so'm) */
  teacherSalaryFixed?: number
  // ⚠️ `studentCount` ATAYIN YO'Q: `ClassesController` xom `Group` entity qaytaradi, unda bunday
  // ustun umuman yo'q. Maydon turda turgani uchun UI uni "bor" deb o'ylab, fallback bilan
  // pul hisoblab yuborardi (o'rinbosarlik summasi doim 10 o'quvchiga hisoblanardi).
  // Guruhdagi o'quvchilar soni kerak bo'lsa — shu ma'lumotni HAQIQATAN qaytaradigan
  // endpoint DTO'sidan oling (masalan moliya/xonalar DTO'lari).
}

/** Guruh a'zosi (many-to-many a'zolik) */
export interface GroupMember {
  studentId: string
  fullName: string
  joinedAt: string
  leftAt?: string | null
  isActive: boolean
  /** To'lov holati: 'trial' (sinov) | 'active' (aktiv) | 'frozen' (muzlatilgan) */
  status: string
  /** Aktivlashtirilgan sana (ISO) */
  activatedAt: string
  /** Muzlatilgan sana (ISO) */
  frozenAt: string
  /** SHU GURUH bo'yicha balans (manfiy = qarz) — o'quvchining umumiy balansi EMAS
   *  (boshqa guruhdagi qarz bu ro'yxatni qizil qilmaydi; server: GroupBalanceService). */
  balance: number
  /** «Aktiv muzlatish» — yangi o'quv yiliga o'tishdagi muzlatish belgisi (`status` = "frozen"). */
  yearFreeze: boolean
}

/** O'quvchining guruh a'zoligi */
export interface StudentGroupMembership {
  id: string
  groupId: string
  groupName: string
  joinedAt: string
  leftAt?: string | null
  isActive: boolean
  status: string
  courseName: string
  teacherName: string
  monthlyFee: number
  days: number[]
  startTime: string
  endTime: string
  room: string
  /** Aktivlashtirilgan sana ("yyyy-MM-dd"). Bo'sh = hali aktivlashtirilmagan (sinov). */
  activatedAt: string
  /** Muzlatilgan sana ("yyyy-MM-dd"). Bo'sh = muzlatilmagan. */
  frozenAt: string
  /** «Aktiv muzlatish» — yangi o'quv yiliga o'tishdagi muzlatish belgisi (`status` = "frozen"). */
  yearFreeze: boolean
}

/** Guruh to'ldirish qatori */
export interface GroupFillRow {
  groupId: string
  name: string
  grade: number
  capacity: number
  enrolled: number
  freeSeats: number
  status: 'active' | 'full' | 'archived'
}

/** Bitta guruh bo'yicha oylik hisob (to'lov oynasi uchun — aggregate emas) */
export interface GroupMonth {
  /** "YYYY-MM" */
  month: string
  /** Shu guruhning shu oyga oylik to'lovi (chegirma ayirilgan) */
  fee: number
  /** Shu guruhga teglangan to'langan summa */
  paid: number
  /** Qoldiq (fee − paid) */
  remaining: number
  status: MonthStatus
}

export interface GroupLedger {
  groupId: string
  groupName: string
  courseName: string
  months: GroupMonth[]
}

/* ---------- Jurnal ---------- */

/**
 * Dars o'zlashtirish darajasi (mastery level) — o'qituvchi darsda o'quvchining
 * o'zlashtirish holati qaysi darajada ekanini belgilaydi.
 * - 0 = NonReactive (reaktiv emas — o'rgani emas, tushunarli emas)
 * - 1 = Reactive (reaktiv — o'rgani lekin yordam bilan)
 * - 2 = Active (faol — o'rgani va mustaqil ishlay oladi)
 * - 3 = ProActive (proaktiv — chuqur o'rgani va boshqalarga o'rgata oladi)
 */
export type MasteryLevel = 0 | 1 | 2 | 3

/** Jurnal ustuni: bitta dars (sana + dars raqami). */
export interface JournalColumn {
  date: string
  /** Dars raqami (1-10) */
  period: number
}

export interface JournalEntry {
  studentId: string
  /** Dars sanasi (ISO) */
  date: string
  /** Dars raqami (1-10) — bir kunda bir necha dars bo'lsa farqlash uchun */
  period: number
  /** Baho (1-5), agar kelgan va baholangan bo'lsa */
  grade?: number
  /** Davomat sababi id'si, agar kelmagan bo'lsa */
  reasonId?: string
  /** Uyga vazifa: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi, 3 = chala qildi */
  homework?: number
  /** Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon */
  behavior?: number
  /** Shu darsni o'zlashtirish darajasi (MasteryLevel: 0-3); null/undefined = belgilanmagan */
  mastery?: MasteryLevel | null
  /** ANIQ "keldi (bor)" belgisi — "Keldi" tugmasi yoki "hammasi keldi" bosilganda.
   *  presentDefaultFrom cheklovidan qat'i nazar katak yashil ✓ ko'rsatiladi. */
  present?: boolean
}

/** Dars ma'lumoti (sana + dars raqami bo'yicha): mavzu, uyga vazifa, o'tildi */
export interface JournalTopic {
  date: string
  period: number
  topic: string
  homework?: string
  /** Dars o'tildimi (ptichka) */
  conducted: boolean
}

/* ---------- Sozlamalar ---------- */

export interface QuarterPeriod {
  /** Chorak raqami 1-4 */
  quarter: number
  startDate: string
  endDate: string
  /** O'qituvchilarga shu chorak bahosini kiritish ochiqmi (admin boshqaradi) */
  gradesOpen: boolean
}

/** Davomat sababi (kelmaganlik turi) */
export interface AbsenceReason {
  id: string
  name: string
  /** Jurnal katagida ko'rsatiladigan qisqa belgi */
  short: string
  /** "Kech keldi" turi — yo'qlik emas (davomatga ta'sir qilmaydi), baho qo'ysa bo'ladi */
  isLate: boolean
}

export interface SchoolSettings {
  quarters: QuarterPeriod[]
  absenceReasons: AbsenceReason[]
}

/* ---------- Moliya ---------- */

export type FinanceDirection = 'income' | 'expense'

export interface FinanceTransaction {
  id: string
  /** Sana (ISO) */
  date: string
  direction: FinanceDirection
  /** Toifa: tuition, salary, utilities, supplies, rent, donation, other ... */
  category: string
  /** Summa (musbat; yo'nalish belgini aniqlaydi) */
  amount: number
  note?: string
  /** O'quvchi to'lovi bo'lsa — tegishli o'quvchi id'si */
  studentId?: string
  /** Tuition to'lovi bo'lsa — qaysi guruh uchun (Group id); null = teglanmagan */
  groupId?: string | null
  /** Backend qaytaradigan o'quvchi nomi (qulaylik uchun) */
  studentName?: string
  /** O'qituvchi maoshi bo'lsa — tegishli o'qituvchi id'si */
  teacherId?: string
  /** Backend qaytaradigan o'qituvchi nomi */
  teacherName?: string
  /** Oylik to'lov bo'lsa — qaysi oy uchun ("YYYY-MM") */
  month?: string
  /** To'lov usuli: cash (Naqd) | card (Karta) | bank (Bank orqali) */
  method?: string
  /** Backend qaytaradigan guruh nomi (tuition to'lovi bo'lsa) */
  groupName?: string | null
  /** Kassir qo'lda yozgan izoh (chekda ko'rinadi) */
  comment?: string | null
  /** Kiritilgan vaqt (ISO "yyyy-MM-ddTHH:mm:ss", markaz mintaqasi UTC+5) — ro'yxatda soat ko'rsatish uchun */
  createdAt?: string
  /** KIM KIRITGAN — kassir/admin F.I.Sh ("To'lovlar" va "Kunlik hisobot" jadvallarida ustun) */
  createdBy?: string | null
  /** Kiritgan xodimning akkaunt id'si (eski yozuvlarda yo'q) — "Kiritgan" filtri shunga tayanadi */
  createdById?: string | null
  /** Bu to'lovdan jami qancha VOZVRAT (pul qaytarish) qilingani (>0 = qisman/to'liq qaytarilgan) */
  refunded?: number
  /** Bu yozuvning O'ZI vozvrat bo'lsa — qaysi asl to'lov uchun (id) */
  refundOfId?: string | null
  /** QOG'OZ kvitansiya raqami (naqd to'lovda kiritiladi) — "KV" seriyasi bilan, masalan "KV000123" */
  receiptNo?: string | null
  /** To'lov haqiqatan qilingan VAQT "HH:mm" (karta orqali to'lovda kiritiladi) */
  paidTime?: string | null
  /** KARTA raqamining oxirgi 4 raqami (karta to'lovida kiritiladi) — jadvalda "•••• 1234".
   *  Faqat oxirgi 4 raqam saqlanadi (to'liq karta raqami hech qachon saqlanmaydi). */
  cardLast4?: string | null
}

/** Vozvrat (pul qaytarish) yozuvi — "Vozvratlar tarixi" uchun, asl to'lov ma'lumoti bilan. */
export interface Refund {
  id: string
  date: string
  amount: number
  studentId?: string | null
  studentName?: string | null
  groupId?: string | null
  groupName?: string | null
  month?: string | null
  reason?: string | null
  paymentId?: string | null
  paymentAmount?: number | null
  paymentDate?: string | null
  createdBy?: string | null
  createdAt?: string | null
}

export interface CategoryAmount {
  category: string
  amount: number
}

export interface FinanceSummary {
  totalIncome: number
  totalExpense: number
  /** Sof = kirim - chiqim */
  net: number
  tuitionIncome: number
  otherIncome: number
  incomeByCategory: CategoryAmount[]
  expenseByCategory: CategoryAmount[]
  /** O'quvchilar jami qarzi (manfiy balanslar yig'indisi, musbat son) */
  studentDebt: number
  /** O'quvchilar jami avansi (musbat balanslar yig'indisi) */
  studentAdvance: number
  transactionsCount: number
}

export interface FinanceMonthly {
  /** "YYYY-MM" */
  month: string
  income: number
  expense: number
}

/* ---------- O'quvchi to'lov tarixi (ledger) ---------- */

export type MonthStatus = 'paid' | 'partial' | 'unpaid'

export interface MonthLedger {
  /** "YYYY-MM" */
  month: string
  /** Shu oyga hisoblangan TO'LIQ summa (guruh oylik narxi — chegirmasiz) */
  charged: number
  /** Shu oy uchun berilgan chegirma summasi */
  discount: number
  /** Qoplangan (haqiqiy naqd) summa — chegirma kirmaydi */
  paid: number
  /** Qolgan qarz = charged − discount − paid */
  remaining: number
  status: MonthStatus
  /** Shu oyda qaysi kurslarga (qancha) — breakdown */
  courses: MonthCourse[]
  /** Shu oy uchun guruh ID (per-group hisob bo'lsa) */
  groupId?: string
}

export interface MonthCourse {
  courseName: string
  fee: number
  /** Shu kurs ulushi qaysi guruh hisobiga tegishli (null = guruhsiz/ClassName) */
  groupId?: string | null
  /** GURUH nomi — ro'yxatda asosiy ko'rsatkich ("Guruh — Kurs" ko'rinishida) */
  groupName?: string | null
}

export interface LedgerPayment {
  date: string
  amount: number
  note?: string
  /** Foydalanuvchi kiritgan izoh (ixtiyoriy) */
  comment?: string
  /** Qaysi oy uchun to'langani ("YYYY-MM"), agar biriktirilgan bo'lsa */
  month?: string
  /** To'lov usuli: cash (Naqd) | card (Karta) | bank (Bank orqali) */
  method?: string
  /** To'lov QAYSI guruh uchun qilingani (guruhga teglanmagan bo'lsa yo'q) */
  groupName?: string
  /** O'sha guruhning o'qituvchisi */
  teacherName?: string
  /** O'sha guruhning kursi — ro'yxatda "Guruh — Kurs" bo'lib chiqadi */
  courseName?: string
  /** NAQD to'lovda kassir kiritgan QOG'OZ kvitansiya raqami ("KV000123") */
  receiptNo?: string
  /** KARTA to'lovida pul o'tkazilgan haqiqiy vaqt ("HH:mm") */
  paidTime?: string
  /** KARTA raqamining oxirgi 4 raqami ("1234") — to'liq raqam saqlanmaydi */
  cardLast4?: string
}

export interface StudentLedger {
  student: Student
  balance: number
  /** Hozirgi effektiv oylik to'lov (guruh narxi − chegirma) */
  monthlyFee: number
  /** Jami hisoblangan (to'liq narx — chegirmasiz) */
  totalCharged: number
  /** Jami berilgan chegirma (so'm) */
  totalDiscount: number
  /** Jami haqiqiy naqd to'langan summa (chegirma kirmaydi) */
  totalPaid: number
  months: MonthLedger[]
  payments: LedgerPayment[]
}

/* ---------- O'zgarishlar tarixi (audit) ---------- */

export type AuditAction = 'create' | 'update' | 'delete'

export interface AuditLog {
  id: string
  /**
   * Texnik tur: FinanceTransaction | TeacherSalary | ClassFee | Student | Group | Membership |
   * Lead | Course | Book | Contract | Vacancy | Staff | CenterMeta | ...
   * ⚠️ Nomlari tarixiy sabablarga ko'ra ALDAMCHI (masalan "StudentDiscount" arxivlash/bloklashda
   * ham yoziladi) — foydalanuvchiga ko'rsatish uchun `section` ishlatilsin.
   */
  entityType: string
  entityId: string
  action: AuditAction
  /** ISO "yyyy-MM-ddTHH:mm:ss" */
  timestamp: string
  /** O'zgartirgan foydalanuvchi nomi (yoki "Tizim") */
  actorName?: string
  /** O'qiladigan o'zbekcha izoh */
  summary: string
  /** O'zgarishdan oldingi holat (JSON satr) — create uchun yo'q */
  before?: string
  /** O'zgarishdan keyingi holat (JSON satr) — delete uchun yo'q */
  after?: string
  studentId?: string
  teacherId?: string
  /** Bo'lim kaliti — SERVER hisoblaydi (`AuditSections`): students | classes | finance | ... | other */
  section?: string
}

/* ---------- O'qituvchilar ---------- */

export interface Teacher {
  id: string
  /** Familiya Ism Sharif */
  fullName: string
  birthDate: string
  address: string
  gender: Gender
  /** Telefon raqami — Telegram bot orqali shartnoma olish uchun ro'yxatdan o'tishda moslashtiriladi */
  phone?: string
  /** O'qituvchining rasmi (profil surati) URL'i */
  photoUrl?: string | null
  /** Guruh rahbari bo'lsa — biriktirilgan guruh nomi; aks holda bo'sh */
  homeroomClass: string
  /** Dars beradigan fanlar (Subject id'lari) */
  subjectIds: string[]
  /** Maosh rejimi: 'fixed' (qat'iy summa) | 'percent' (guruh to'lovidan foiz). Standart 'fixed'. */
  salaryMode?: string
  /** Qat'iy oylik ish haqi (so'm) — salaryMode='fixed' da ishlatiladi */
  salary: number
  /** Foizli maosh ulushi (%) — salaryMode='percent' da: guruhdan yig'ilgan to'lovning shu foizi */
  salaryPercent?: number
  /** O'qituvchi toifasi: "oliy" | "1" | "2" | "mutaxasis" (bo'sh = belgilanmagan). Soat narxini belgilaydi. */
  category?: string
  /** Oylik qaysi oydan hisoblansin ("YYYY-MM"); bo'sh = hisobot davri boshidan (eski maydon) */
  salaryStartMonth: string
  /** Maosh qaysi KUNdan hisoblansin ("YYYY-MM-DD"); oy o'rtasida kelsa birinchi oy qisman */
  salaryStartDate?: string
  /** O'qituvchi web panelida ochiq bo'limlar (admin belgilaydi) */
  permissions: string[]
  /** Support o'qituvchimi — bo'sh vaqt slotlari + bron (Ilova → Support) */
  isSupport?: boolean
  /** Arxivlanganmi (ishdan ketgan/to'xtatilgan) */
  isArchived?: boolean
  /** Arxivga olingan sana (ISO) */
  archivedAt?: string | null
  /** Arxivga olish sababi */
  archiveReason?: string | null
  /** VAQTINCHA AKTIV EMAS — tizimga kira olmaydi, lekin paroli/guruhlari/tarixi saqlanadi
   *  (arxivdan farqi shu; bir tugma bilan qaytariladi) */
  isBlocked?: boolean
  /** Vaqtincha faolsizlantirilgan sana (ISO) */
  blockedAt?: string | null
  /** Vaqtincha faolsizlantirish izohi (faqat admin ko'radi) */
  blockNote?: string
}

/* ---------- O'qituvchi faollik hisoboti ---------- */

/** Bitta o'qituvchi qatori (umumiy ko'rinish). Status: active | low | none */
export interface TeacherReportRow {
  teacherId: string
  fullName: string
  isArchived: boolean
  /** Reja — jadvaldan kelib chiqib bugungacha bo'lishi kerak bo'lgan darslar */
  expected: number
  /** O'tilgan (jurnal "o'tildi" belgilari) */
  conducted: number
  /** Bajarilish foizi (conducted/expected); reja yo'q bo'lsa null */
  donePct: number | null
  /** Qo'yilgan baholar soni */
  grades: number
  /** O'tilgan darslarning necha %ida mavzu yozilgan */
  topicPct: number | null
  /** O'tilgan darslarning necha %ida uy vazifa berilgan */
  homeworkPct: number | null
  /** Oxirgi faollik sanasi (ISO) yoki null */
  lastActivity: string | null
  status: 'active' | 'low' | 'none'
  /** Jami kelgan o'quvchilar (barcha holatlar, shu o'qituvchi guruhlari) */
  came: number
  /** Faol (active) o'quvchilar soni */
  active: number
  /** Sinov (trial) o'quvchilar soni */
  trial: number
  /** Muzlatilgan (frozen) o'quvchilar soni */
  frozen: number
  /** Ketgan (IsActive=false) o'quvchilar soni */
  left: number
  /** Qolgan = HOZIRGI aktiv o'quvchilar (barcha guruhlarida; o'qituvchi performance "Faol" bilan bir xil).
   *  Kelgan/Faol/Sinov/Muzlatilgan/Ketgan esa tanlangan OYDAGI oqim. */
  remaining: number
  /** Sotuv konversiyasi foizi: active/came*100; came=0 bo'lsa null */
  conversionPct: number | null
}

/** Guruh/fan kesimida bitta qator (batafsil hisobot) */
export interface TeacherReportBreakdown {
  className: string
  subjectName: string
  expected: number
  conducted: number
  donePct: number | null
  grades: number
  topicPct: number | null
  homeworkPct: number | null
}

/** Bitta o'qituvchining batafsil hisoboti */
export interface TeacherReportDetail extends TeacherReportRow {
  rows: TeacherReportBreakdown[]
}

/**
 * Umumiy ko'rinish javobi: mavjud oylar ro'yxati ("yyyy-MM"), tanlangan oy
 * (bo'sh = Umumiy) va o'qituvchi qatorlari.
 */
export interface TeacherReportOverview {
  months: string[]
  month: string
  rows: TeacherReportRow[]
}

/* ---------- Shartnomalar ---------- */

/** target: 'parent' | 'staff' */
/** Foydalanuvchi qo'shgan qo'shimcha @-o'rinbosar (doimiy qiymat bilan) */
export interface ContractField {
  key: string
  value: string
}

export interface ContractTemplate {
  id: string
  target: 'parent' | 'staff'
  name: string
  fileUrl: string
  fileName: string
  /** Custom (matnli) andoza tanasi — bo'sh bo'lmasa matnli andoza (fayl emas) */
  body: string
  /** Foydalanuvchi qo'shgan qo'shimcha o'rinbosarlar (doimiy qiymatli) */
  fields: ContractField[]
  uploadedAt: string
}

/** O'quvchi oluvchi (shartnoma o'quvchi bo'yicha tuziladi) */
export interface StudentRecipient {
  studentId: string
  fullName: string
  parentName: string
  phone: string
  /** Faol guruh nomlari (vergul bilan) */
  groups: string
  /** Telegramda ro'yxatdan o'tganmi */
  registered: boolean
  /** Oxirgi shartnoma raqami */
  lastNumber: number | null
}

/** Xodim oluvchi */
export interface StaffRecipient {
  teacherId: string
  fullName: string
  phone: string
  registered: boolean
  lastNumber: number | null
}

/** Tuzilgan shartnoma hujjati (saqlangan PDF/DOCX nusxa bilan) */
export interface ContractDoc {
  id: string
  /** Shartnoma raqami */
  number: number
  /** Ko'rsatiladigan sarlavha, masalan "Shartnoma № 12" */
  title: string
  target: 'parent' | 'staff'
  /** Oluvchi kaliti (o'quvchi yoki xodim id) */
  recipientKey: string
  recipientName: string
  /** Andoza nomi (tarixiy nusxa) */
  templateName: string
  /** Tuzilgan sana (ISO) */
  date: string
  /** Superadmin yuklagan PDF ("/uploads/...") — bo'sh bo'lsa, PDF hali yuklanmagan */
  pdfUrl: string
  /** Tizim hosil qilgan .docx nusxa ("/uploads/...") */
  docxUrl: string
  /** Telegram orqali yetkazilganmi */
  delivered: boolean
  status: string
  /**
   * Ilovada (o'quvchi/o'qituvchi) ko'rinadimi.
   * Spec'dagi ContractDocDto'da bu maydon ixtiyoriy — kelmasa `true` deb hisoblanadi.
   */
  visible?: boolean
}

/** Bitta oluvchiga yuborish natijasi */
export interface SendResult {
  recipientKey: string
  ok: boolean
  number: number | null
  message: string
}

/* ---------- Boshqaruv ---------- */

/** Filial (branch) */
export interface Branch {
  id: string
  name: string
  address: string
  latitude: number
  longitude: number
  radiusMeters: number
  createdAt: string
}

/** Xodim (o'qituvchi bo'lmagan ishchi) */
export interface Staff {
  id: string
  fullName: string
  /** Lavozim yorlig'i (Kassir/Administrator/...) */
  position: string
  /** Tizim logini */
  login: string
  /** Ochiq admin bo'limlari (adminPermissions kalitlari) */
  permissions: string[]
  /** Telefon — botda yangi lid xabarnomasini olish uchun */
  phone?: string
  /** Akkaunt roli: 'staff' | 'admin' | 'superadmin'. Superadminda bo'lim ruxsatlari tekshirilmaydi. */
  role?: string
}

/** Xodim roli shabloni — yangi xodim qo'shishda template tanlab olsa, default ruxsatlari avtomatik belgilanadi */
export interface StaffRoleTemplate {
  id: string
  code: string
  name: string
  description: string
  defaultPermissions: string[]
}

/** Taklif yoki shikoyat (ota-ona ilovasidan) */
export interface Feedback {
  id: string
  studentName: string
  parentName: string
  className: string
  /** suggestion | complaint */
  type: 'suggestion' | 'complaint'
  text: string
  createdAt: string
  /** new | resolved */
  status: 'new' | 'resolved'
  /** parent | teacher — yuboruvchi roli */
  senderRole: 'parent' | 'teacher'
  /** Yuboruvchining ismi (ota-ona yoki o'qituvchi) */
  senderName: string
  /** Biriktirilgan rasm ("/uploads/...") yoki null */
  imageUrl: string | null
}

/* ---------- O'qituvchi maoshi ---------- */

export interface SalaryHistory {
  teacherId: string
  fullName: string
  salary: number
  totalPaid: number
  payments: LedgerPayment[]
}

/** Bitta oyda bitta guruhning jurnal holati — maosh ushlanmasining sababi */
export interface SalaryLessonStat {
  groupId: string
  groupName: string
  /** Rejadagi darslar (guruh kunlari bo'yicha, muhlati o'tganlari) */
  planned: number
  /** Jurnalda "o'tildi" deb belgilangani */
  conducted: number
  /** Belgilanmagani (o'tilmagan hisoblanadi) */
  missed: number
  /** Shu guruh uchun ushlangan summa */
  deduction: number
  /** Belgilanmagan dars sanalari ("YYYY-MM-DD") */
  missedDates: string[]
}

/** Oy bo'yicha maosh holati */
export interface MonthSalary {
  /** "YYYY-MM" */
  month: string
  /** Shu oy uchun YAKUNIY (ushlanmadan keyingi) oylik */
  expected: number
  /** Shu oyda berilgan */
  paid: number
  /** Qoldiq (belgilangan − berilgan) */
  remaining: number
  status: MonthStatus
  /** Ushlanmagacha hisoblangan summa */
  baseExpected?: number
  /** Jurnalda belgilanmagan darslar uchun ushlanma */
  deduction?: number
  plannedLessons?: number
  conductedLessons?: number
  missedLessons?: number
  /** Ushlanma tafsiloti (guruhlar bo'yicha) — maosh jurnalga bog'langanda */
  lessons?: SalaryLessonStat[]
  /**
   * FOIZLI maosh bazasi — shu OY UCHUN o'qituvchi guruhlaridan yig'ilgan tuition (vozvrat ayrilgan).
   * Qat'iy maoshda 0. To'lov QAYSI OY UCHUN qilingan bo'lsa shu oyga kiradi — to'lov sanasi emas
   * (3-avgustda iyul uchun to'lansa → iyul oyiga).
   */
  collected?: number
  /**
   * Shu OY UCHUN o'quvchilarga HISOBLANGAN (chegirma ayrilgan, to'lanmagan qarz ham kiradi)
   * tuition summasi. Qat'iy maoshda 0.
   */
  charged?: number
  /**
   * "Hammasi to'lansa" maosh qancha bo'lardi (hisoblangan × foiz, ushlanma ayrilgan).
   * Guruh yopilib pul hali yig'ilmagan oyda `expected` 0 bo'lib ko'rinadi — shu raqam
   * o'qituvchining o'sha oydagi haqiqiy hissasini ko'rsatadi. Qat'iy maoshda `expected` ga teng.
   */
  potentialExpected?: number
  /**
   * TUSHUM REJASI — o'qituvchining BARCHA guruhlarida shu oy uchun o'quvchilarga hisoblangan
   * (chegirma ayrilgan) summa: "aslida qancha tushum bo'lishi kerak edi".
   * ⚠️ `charged` MAOSH bazasi (faqat foizli guruhlar), bu esa TUSHUM (hamma guruh).
   * Faqat admin endpointida to'ladi (`/admin/teachers/{id}/salary-ledger`).
   */
  tuitionCharged?: number
  /** Shu oy uchun haqiqatda yig'ilgan tushum (vozvrat ayrilgan) — barcha guruhlar bo'yicha. */
  tuitionCollected?: number
  /** Shu oyda boshqa o'qituvchilar o'rniga o'rinbosar bo'lib o'tilgan darslar uchun qo'shimcha haq (so'm). */
  substituteFee?: number
  /** Shu oyda o'z guruhida o'rinbosar o'qituvchi dars o'tgani uchun chegirilgan summa (so'm). */
  substituteDeduction?: number
}

/** Maosh hisobida bitta guruhning ulushi (davr bo'yicha) */
export interface GroupSalaryLine {
  groupId: string
  groupName: string
  courseName: string
  monthlyFee: number
  /** Amaldagi rejim: 'percent' | 'fixed' */
  mode: string
  /** Foizli ulush (%) */
  percent: number
  /** Qat'iy summa (so'm) */
  fixed: number
  /** Shu davrda guruhdan yig'ilgan to'lov bazasi */
  periodCollected: number
  /** Shu guruh keltirgan hisoblangan maosh (davr bo'yicha) */
  periodExpected: number
  /** Guruh bo'yicha o'rinbosarga berilgan summa (so'm) */
  substituteDeduction?: number
  /** O'rinbosar o'tgan darslar soni */
  substitutedLessons?: number
}

/** O'qituvchi maoshi bo'yicha batafsil hisob (davr bo'yicha) */
export interface SalaryLedger {
  teacherId: string
  fullName: string
  salary: number
  /** Jami hisoblangan (oylik × davr oylari) */
  totalExpected: number
  /** Jami berilgan */
  totalPaid: number
  /** Umumiy qoldiq */
  remaining: number
  months: MonthSalary[]
  payments: LedgerPayment[]
  /** Maosh rejimi: 'fixed' | 'percent' */
  salaryMode?: string
  /** Foizli ulush (%) — salaryMode='percent' bo'lsa */
  salaryPercent?: number
  /** Per-guruh maosh taqsimoti (har guruh alohida rejim/qiymat + ulush) */
  groups?: GroupSalaryLine[]
  /** Davr bo'yicha jami ushlanma (jurnalda belgilanmagan darslar uchun) */
  totalDeduction?: number
  /** true — maosh jurnalga bog'langan (Guruhlar → Jurnal boshqaruvi) */
  journalLinked?: boolean
}

export interface SalaryReportRow {
  teacherId: string
  teacherName: string
  /** Belgilangan oylik */
  salary: number
  /** Davr ichida berilgan jami */
  totalPaid: number
  paymentsCount: number
  /** Davrdagi oylar soni */
  months: number
  /** Kerakli (oylik × davr oylari) */
  expected: number
  /** Qoldiq (kerakli − berilgan); manfiy = ortiqcha berilgan */
  remaining: number
  /** Maosh rejimi: 'fixed' | 'percent' */
  salaryMode?: string
  /** Foizli ulush (%) — salaryMode='percent' bo'lsa */
  salaryPercent?: number
  /** Jurnalda belgilanmagan darslar uchun ushlangan jami summa */
  deduction?: number
  /** Belgilanmagan darslar soni (davr bo'yicha) */
  missedLessons?: number
}

/** O'quvchilar bo'yicha moliya hisoboti qatori (joriy holat) */
export interface StudentFinanceRow {
  studentId: string
  fullName: string
  className: string
  /** Jami hisoblangan (TO'LIQ oylik to'lovlar yig'indisi — chegirmasiz) */
  charged: number
  /** Jami berilgan chegirma (so'm) */
  discount: number
  /** Jami HAQIQIY naqd to'lov (chegirma kirmaydi — to'langan summa o'zgarmaydi) */
  paid: number
  /** Qoldiq qarz (balansdan) */
  debt: number
  /** Ortiqcha to'langan (avans, balansdan) */
  advance: number
  /** Chegirma foizi qoidasi (0..100). 0 — chegirma yo'q. */
  discountPct: number
  /** Chegirma aniq summa qoidasi. 0 — chegirma yo'q. */
  discountAmount: number
}

/* ---------- Xabarlar (chat + e'lon + telegram) ---------- */

/** Guruh chatidagi bitta xabar */
export interface ChatMessage {
  id: string
  /** Qaysi guruh chati (guruh nomi) */
  className: string
  senderUserId: string
  senderName: string
  /** admin | teacher | student */
  senderRole: Role
  text: string
  /** ISO 8601 vaqt */
  createdAt: string
}

/** Admin "Xabarlar" bo'limidagi guruh kartasi */
export interface MessageClass {
  name: string
  grade: number
  studentCount: number
  /** Telegramda ro'yxatdan o'tgan (e'lon oluvchi) ota-onalar soni */
  parentCount: number
  /** Oxirgi chat xabari vaqti (ISO) yoki null */
  lastMessageAt: string | null
}

/** Telegram bot orqali yuborilgan e'lon */
export interface Broadcast {
  id: string
  className: string
  text: string
  senderName: string
  createdAt: string
  /** Yuborilganda ro'yxatda bo'lgan ota-onalar (chatlar) soni */
  recipientCount: number
  /** Muvaffaqiyatli yetkazilganlar soni */
  sentCount: number
}

/** Telegramda ro'yxatdan o'tgan ota-ona */
export interface TelegramParent {
  studentId: string
  studentName: string
  /** O'quvchi guruhi */
  className: string
  /** O'quvchi balansi (manfiy = qarz) — qarzdorlar filtri uchun */
  balance: number
  parentName: string
  phone: string
  chatId: string
  createdAt: string
}

/** Telegramda ro'yxatdan o'tgan o'qituvchi (xodim ro'yxati) — "Tanlab" e'lon uchun */
export interface TelegramTeacher {
  teacherId: string
  teacherName: string
  phone: string
  chatId: string
  createdAt: string
}

/** Bitta davomat sababidan o'quvchida necha marta bo'lgani (jurnal belgilaridan) */
export interface AttendanceReasonCount {
  reasonId: string
  name: string
  short: string
  isLate: boolean
  count: number
}

/** Telegram bot holati (admin UI uchun) */
export interface TelegramStatus {
  configured: boolean
  botUsername: string
}

/** Push uchun tanlanadigan oluvchi */
export interface PushRecipient {
  /** Akkaunt id (UserId) */
  userId: string
  name: string
  /** "Ota-ona" yoki "O'qituvchi" */
  group: string
  /** Qo'shimcha (ota-ona uchun guruh) */
  detail: string
  /** Qurilma ulanganmi (push haqiqatan yetadimi) */
  hasDevice: boolean
}

/** Yuborilgan push bildirishnoma (tarix) */
export interface PushMessage {
  id: string
  audience: string
  title: string
  body: string
  senderName: string
  createdAt: string
  recipientCount: number
  sentCount: number
  /** Nechta oluvchi "Tasdiqlash" bosgani */
  confirmedCount: number
  /** Jami oluvchi (bildirishnoma yozilganlar) */
  targetCount: number
}

/** Ota-onalar bo'limidagi farzand (qisqacha) */
export interface ParentChild {
  studentId: string
  fullName: string
  className: string
  firstLoginAt: string | null
  lastLoginAt: string | null
  /** Oxirgi faol qurilma nomi */
  deviceName?: string
  platform?: string
  /** Push provayder app_id */
  appId?: string
}

/** Ota-onalar ro'yxati qatori (telefon bo'yicha guruhlangan) */
export interface ParentRow {
  fullName: string
  phone: string
  childrenCount: number
  isActivated: boolean
  activatedAt: string | null
  lastSeenAt: string | null
  children: ParentChild[]
  /** Oxirgi faol qurilma (farzandlar bo'yicha) */
  deviceName?: string
  platform?: string
}

/** Ilova → O'qituvchilar qatori (o'qituvchi ilova faolligi + qurilma) */
export interface TeacherAppRow {
  teacherId: string
  fullName: string
  phone: string
  isActivated: boolean
  activatedAt: string | null
  lastSeenAt: string | null
  deviceName: string
  platform: string
  appId: string
}

/** Admin xarita sahifasi uchun — joylashuvi bor bitta o'quvchi qatori */
export interface StudentLocationRow {
  studentId: string
  fullName: string
  className: string
  latitude: number
  longitude: number
  address?: string | null
  updatedAt?: string | null
}

/** O'qituvchi dars beradigan guruh (o'qituvchi paneli uchun) */
export interface TeacherClass {
  classId: string
  className: string
  grade: number
  /** Shu guruhda o'qituvchi o'qitadigan kurs(lar) */
  subjects: Subject[]
}

/** Portal umumiy konteksti (choraklar, davomat sabablari + joriy chorak/hafta) */
export interface PortalMeta {
  quarters: QuarterPeriod[]
  absenceReasons: AbsenceReason[]
  currentQuarter: number
  currentWeek: number
}

// ===================== O'quv dasturi (curriculum / syllabus) — Kurs(Subject)dan mustaqil =====================

/** Topshiriq turi: matnli / video / audio / lug'at / test / pdf / interaktiv mashq (konstruktor) */
export type LessonType = 'text' | 'video' | 'audio' | 'vocab' | 'test' | 'pdf' | 'exercise'

export interface CurriculumItem {
  id: string
  text: string
  note: string
  order: number
  /** Topshiriq turi (kontent) — yaratishda tanlanadi, keyin ham o'zgartirilishi mumkin */
  type: LessonType
  /** Qisqa meta yorlig'i ("12 daq" / "15 so'z" / "10 savol") */
  meta: string
  /** Topshiriq kontenti to'liq kiritilganmi (tayyor) */
  ready: boolean
  /** Topshiriq ICHIDAGI elementlar soni: mashqda gap/savol/juftlik, testda savol, lug'atda so'z. */
  count: number
  /** Yaratilgan sana-vaqt (ISO) — eski (maydon qo'shilishidan oldingi) topshiriqlar uchun bo'sh */
  createdAt: string
}
/** Dars — ichiga kirilganda topshiriqlar (CurriculumItem) ro'yxati ko'rsatiladi, har biri o'z
 *  turini (video|matn|audio|pdf|lug'at|test) yaratishda tanlaydi; bitta dars ichida bir nechtasi
 *  bo'lishi mumkin. */
export interface CurriculumLesson {
  id: string
  title: string
  note: string
  order: number
  items: CurriculumItem[]
}
export interface CurriculumTopic {
  id: string
  title: string
  note: string
  order: number
  lessons: CurriculumLesson[]
}
export interface CurriculumModule {
  id: string
  name: string
  note: string
  order: number
  topics: CurriculumTopic[]
}
export interface Curriculum {
  id: string
  name: string
  modules: CurriculumModule[]
}

// ===================== Amal sabablari (action reasons) =====================

/** Kategoriya: freeze | return_trial | remove_active | remove_trial | remove_frozen | lead_delete | group_delete */
export interface ActionReason {
  id: string
  category: string
  label: string
  order: number
}

// ===================== Daraja testi (placement test) =====================

export interface LevelTestListItem {
  id: string
  title: string
  courseId: string
  courseName: string
  slug: string
  isActive: boolean
  createdAt: string
  questionCount: number
  submissionCount: number
}

export interface LevelTestQuestion {
  id: string
  text: string
  options: string[]
  correctIndex: number
  order: number
  /** "question" (baholanadigan, to'g'ri javobli) yoki "survey" (so'rovnoma, checkbox, baholanmaydi) */
  kind: 'question' | 'survey'
  /** survey uchun: ko'p tanlash (checkbox) mumkinmi */
  multiple: boolean
}

export interface LevelTestBand {
  id: string
  label: string
  minPercent: number
  order: number
}

export interface LevelTestDetail {
  id: string
  title: string
  courseId: string
  courseName: string
  slug: string
  intro: string
  isActive: boolean
  createdAt: string
  questions: LevelTestQuestion[]
  bands: LevelTestBand[]
}

export interface LevelTestSubmission {
  id: string
  fullName: string
  phone: string
  age: number
  score: number
  total: number
  percent: number
  level: string
  createdAt: string
  leadId: string
  /** So'rovnoma javoblari (baholanmagan) */
  survey: { question: string; answers: string[] }[]
}

/** Test yaratish/yangilash payload'i */
export interface LevelTestPayload {
  title: string
  courseId: string
  intro: string
  isActive: boolean
  questions: {
    id?: string
    text: string
    options: string[]
    correctIndex: number
    kind?: 'question' | 'survey'
    multiple?: boolean
  }[]
  bands: { id?: string; label: string; minPercent: number }[]
}

// Ommaviy (anonim)
export interface PublicTestQuestion {
  id: string
  text: string
  options: string[]
  kind: 'question' | 'survey'
  multiple: boolean
}

export interface PublicTest {
  title: string
  intro: string
  courseName: string
  questions: PublicTestQuestion[]
}

export interface TestResult {
  score: number
  total: number
  percent: number
  level: string
  message: string
}

/** Arxivlangan (o'chirilgan) yozuv — Arxiv bo'limida ko'rsatiladi. */
export interface ArchivedRecord {
  id: string
  type: string
  entityId: string
  title: string
  subtitle: string
  reason?: string
  deletedAt: string
  actorName: string
}

/* ---------- O'quvchi sertifikati ---------- */

export interface StudentCertificateDto {
  id: string
  courseName: string
  issuedAt: string
  expiresAt?: string | null
  /** "active" | "expired" | "revoked" */
  status: string
  fileName: string
  downloadUrl: string
  downloadCount: number
  metadata?: Record<string, string> | null
}

/* ---------- BALL / REYTING (jurnal baholari + bajarilgan baholash mezonlari) ---------- */

/** Bitta o'quvchining markaz bo'yicha bali (admin "O'quvchilar" ro'yxatidagi "Ball" ustuni) */
export interface StudentBall {
  studentId: string
  /** Jurnal baholari yig'indisi */
  journalTotal: number
  /** Bajarilgan baholash mezonlari soni */
  criteriaDone: number
  /** Ball = journalTotal + criteriaDone */
  ball: number
  /** O'rtacha baho (baho qo'yilgan darslar bo'yicha) */
  average: number
}

/** O'qituvchi reytingidagi bitta qator */
export interface TeacherRatingRow {
  /** O'rin (1 = eng yuqori ball) */
  rank: number
  studentId: string
  fullName: string
  /** Shu o'qituvchining qaysi guruhlarida o'qiydi (vergul bilan) */
  groups: string
  journalTotal: number
  criteriaDone: number
  ball: number
  average: number
  /** Davomat % (o'tilgan dars yo'q bo'lsa null) */
  attendance: number | null
}

/** O'qituvchi guruhlaridagi o'quvchilar reytingi */
export interface TeacherRating {
  teacherId: string
  fullName: string
  groupsCount: number
  studentsCount: number
  averageBall: number
  rows: TeacherRatingRow[]
  /** Qaysi oy kesimi qaytarildi ("yyyy-MM"); "" = Umumiy (barcha vaqt) — standart */
  month: string
  /** Tanlash mumkin bo'lgan oylar (eng erta ma'lumot oyidan JORIY oygacha) — kelajak yo'q */
  months: string[]
}

/** Lid manbasi (ma'lumotnoma) */
export interface LeadSource {
  id: string
  name: string
  order: number
}

// ===================== Test natijalari (O'quv bo'limi → Testlar natijalari) =====================

/** Testlar natijalari bosh sahifasi — guruh kartasi (yaratilgan testlar soni bilan). */
export interface TestGroupOverview {
  groupId: string
  name: string
  courseName: string
  teacherId: string
  teacherName: string
  studentCount: number
  testCount: number
}

/** Onlayn test sozlamalari (bot orqali ishlanadigan test). mode="offline" — eski tizim. */
export interface OnlineTest {
  /** "offline" (ballni o'qituvchi qo'lda kiritadi) | "online" (o'quvchi botdan ishlaydi) */
  mode: 'offline' | 'online' | string
  /** Savollar PDF fayli ("/uploads/xxx.pdf") — botga shu yuboriladi */
  pdfUrl: string
  pdfName: string
  /** Savollar soni (onlayn testda maxScore shunga teng — har savol 1 ball) */
  questionCount: number
  /** Variantlar soni: 4 → A–D, 5 → A–E */
  optionCount: number
  /** To'g'ri javoblar ("ABCDA...", uzunligi = questionCount) */
  answerKey: string
  /** Javob qabul qilish oynasi (ISO "yyyy-MM-ddTHH:mm") */
  startAt: string
  endAt: string
  /**
   * TEST KODI — markazda o'qimaydigan odam ham botda shu kod bilan testni ishlaydi
   * («📝 Testni ishlash» → «🔑 Test kodi bilan kirish» → KOD → F.I.Sh → test).
   * Bo'sh yuborilsa server o'zi noyob kod yaratadi.
   */
  code: string
  /**
   * true — test guruh a'zolariga ham e'lon qilinadi (va kod bilan tashqi odam ham qo'shiladi);
   * false — "FAQAT ONLAYN": guruhga e'lon qilinmaydi, faqat kod bilan ishlanadi.
   */
  groupOpen: boolean
}

/** Bitta test qatori (guruh testlar ro'yxatida). */
export interface GroupTest {
  id: string
  groupId: string
  name: string
  date: string
  maxScore: number
  createdAt: string
  createdBy: string
  studentCount: number
  scoredCount: number
  avgScore: number | null
  online: OnlineTest
  /** Botdan javob yuborgan o'quvchilar soni (onlayn test) */
  submittedCount: number
  /** Markazdan tashqari (test kodi bilan kirgan) ishtirokchilar soni */
  externalCount: number
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled: boolean
  /** Tanlangan sertifikat shabloni (bo'sh — standart shablon) */
  certificateTemplateId: string
  /** Shu test bo'yicha yaratilgan sertifikatlar soni */
  certificateCount: number
}

/** Test natijasi qatori — bitta o'quvchi bali (rank=0 → ball kiritilmagan). */
export interface TestScoreRow {
  studentId: string
  fullName: string
  score: number | null
  rank: number
  /** Onlayn: botdan yuborilgan javoblar ("ABDCA...") */
  answers: string
  /** Onlayn: yuborilgan vaqt (ISO) */
  submittedAt: string
  /** "bot" — o'quvchi botdan yubordi; "" — qo'lda kiritilgan */
  source: string
  /** Guruhning faol a'zosimi. false — markazning BOSHQA guruhidagi o'quvchi kod bilan qo'shilgan. */
  member: boolean
}

/** MARKAZDAN TASHQARI ishtirokchi qatori — test kodi bilan kirgan, markazda o'qimaydigan odam. */
export interface ExternalTestScoreRow {
  id: string
  fullName: string
  /** Botga ulashgan telefon raqami (bo'lmasa bo'sh) */
  phone: string
  score: number
  /** Shu ro'yxat ICHIDAGI o'rin */
  rank: number
  answers: string
  submittedAt: string
}

/** Test tafsiloti — test + o'quvchilar ballari (ball desc bo'yicha saralangan). */
export interface TestResultDetail {
  id: string
  groupId: string
  groupName: string
  name: string
  date: string
  maxScore: number
  createdAt: string
  createdBy: string
  /** MARKAZDAGILAR — guruh a'zolari + kod bilan qo'shilgan markaz o'quvchilari */
  rows: TestScoreRow[]
  online: OnlineTest
  /** MARKAZDAN TASHQARI — kod bilan kirgan, markazda o'qimaydigan ishtirokchilar */
  externalRows: ExternalTestScoreRow[]
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled: boolean
  /** Tanlangan sertifikat shabloni (bo'sh — standart shablon) */
  certificateTemplateId: string
  /** Yaratilgan sertifikatlar (eski javoblarda bo'lmasligi mumkin) */
  certificates?: TestCertificate[]
}

/** O'quvchi profilidagi test natijasi qatori (barcha guruhlaridan). */
export interface StudentTestResult {
  testId: string
  groupId: string
  groupName: string
  name: string
  date: string
  maxScore: number
  score: number | null
  rank: number
  total: number
}

/** O'rinbosar o'qituvchi tayinlovi */
export interface SubstituteTeacherAssignment {
  id: string
  groupId: string
  groupName: string
  originalTeacherId: string
  originalTeacherName: string
  substituteTeacherId: string
  substituteTeacherName: string
  date: string
  endDate: string | null
  reason: string
  createdBy: string
  createdAt: string
  isActive: boolean
  lessonCount?: number
  estimatedSalary?: number
  dates?: string[]
  perLessonFee?: number
  /** Asosiy o'qituvchidan ushlanadigan summa (so'm) — SERVER hisoblaydi, klient qayta hisoblamaydi. */
  estimatedDeduction?: number
  /**
   * Guruhdagi FAOL o'quvchilar soni (foizli maosh hovuzi shundan hisoblanadi).
   * Server DTO'da bor edi, turda esa yo'q edi — ya'ni maydon javobda kelib turgani bilan
   * TypeScript uni "yo'q" deb bilardi va UI'da ishlatib bo'lmasdi.
   * ⚠️ O'qituvchi ilovasida (`GET /teacher/substitutions`) `Salary` ruxsati yo'q bo'lsa
   * boshqa pul maydonlari bilan birga 0 ga tushiriladi — "0 ta o'quvchi" deb ko'rsatmang.
   */
  studentCount?: number
}

/**
 * O'rinbosar biriktirish oynasidagi JONLI hisob-kitob (server javobi).
 *
 * ⚠️ Bu raqamlar FAQAT serverdan keladi. Ilgari modal formulani o'zi qayta yozgan edi va
 * natijada bitta tayinlov uchun uch xil summa ko'rinardi (modal, jadval, maosh varaqasi).
 */
export interface SubstitutePreview {
  /** Tanlangan dars kunlari soni */
  lessonCount: number
  /** Bitta dars narxi (so'm) */
  perLessonFee: number
  /** O'rinbosar o'qituvchiga to'lanadigan summa (so'm) */
  estimatedSalary: number
  /** Asosiy o'qituvchidan ushlanadigan summa (so'm) */
  estimatedDeduction: number
  /** Guruhdagi hisobga olingan o'quvchilar soni */
  studentCount: number
  /** Shu oydagi jami dars soni (maxraj) */
  monthLessons: number
  /** Server ogohlantirishi (masalan "guruh maoshi sozlanmagan") — bo'lsa ko'rsatiladi */
  warning: string | null
}

/** Ro'yxat endpointi javobi — server 500 ta bilan CHEKLAYDI, shuning uchun jami son ham keladi. */
export interface SubstituteAssignmentsResult {
  items: SubstituteTeacherAssignment[]
  /** Filtrga mos JAMI yozuvlar soni (chegaradan oldin) */
  total: number
}

/** Yangi o'rinbosar o'qituvchi tayinlash so'rovi */
export interface CreateSubstituteAssignmentPayload {
  groupId: string
  substituteTeacherId: string
  dates?: string[]
  date?: string
  endDate?: string | null
  reason?: string | null
}

/** Guruhning oydagi dars sanasi (modal uchun) */
export interface GroupLessonDate {
  date: string
  dayName: string
  isScheduled: boolean
}

