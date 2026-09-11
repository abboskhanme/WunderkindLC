import type { ContractDoc } from '@/types'
import { api } from '../client'

/* ============================================================
   O'quvchi portali API — /api/student/*
   Rol: student (to'liq) / parent (o'qish + ba'zi amallar) / admin (?studentId= bilan o'qish).
   JSON camelCase. Quarter opaque (=1).
   ============================================================ */

// ---------- Tiplar ----------
export interface StudentProfile {
  id: string
  fullName: string
  className: string
  birthDate: string
  gender: string
  parentFullName: string
  parentPhone: string
  enrollmentDate: string
  photoUrl?: string
  parentPhotoUrl?: string
}

export interface LessonTime { period: number; startTime: string; endTime: string }
export interface AbsenceReasonMeta { id: string; name: string; short: string; isLate: boolean }
export interface PortalMeta {
  lessonTimes: LessonTime[]
  absenceReasons: AbsenceReasonMeta[]
  currentQuarter: number
  currentWeek: number
}

export interface HomeworkItem {
  date: string
  period: number
  subjectId: string
  subjectName: string
  topic: string
  homework?: string
  conducted: boolean
  grade?: number | null
  reasonId?: string | null
  reasonName?: string | null
  isLate: boolean
}
export interface StudentLesson {
  day: number
  period: number
  startTime?: string
  endTime?: string
  subjectId: string
  subjectName: string
  teacherId: string
  teacherName: string
}
export interface StudentDashboard {
  profile: StudentProfile
  meta: PortalMeta
  todayLessons: StudentLesson[]
  todayGrades: HomeworkItem[]
  balance: number
  monthlyFee: number
}

export interface SubjectRef { id: string; name: string }
export interface StudentAttendanceSummary {
  missedDays: Record<number, number>
  illnessDays: Record<number, number>
  missedLessons: Record<number, number>
  illnessLessons: Record<number, number>
  lateCount: Record<number, number>
}
export interface StudentGradesReport {
  studentId: string
  fullName: string
  className: string
  homeroomTeacher: string
  subjects: SubjectRef[]
  grades: Record<string, Record<number, number>>
  attendance: StudentAttendanceSummary
}

export interface AbsenceRow {
  date: string
  period: number
  subjectId: string
  subjectName: string
  reasonId: string
  reasonName: string
  isLate: boolean
  isIll: boolean
}
export interface StudentAttendanceFull {
  summary: StudentAttendanceSummary
  rows: AbsenceRow[]
}

export interface RatingRow {
  rank: number
  studentId: string
  fullName: string
  className: string
  average: number
  attendance?: number | null
  /** Yig'ilgan ball (jurnal baholari + bajarilgan mezonlar) — reyting shu bo'yicha */
  ball?: number
}
/** O'quvchining BITTA guruhi bo'yicha reyting (o'rin guruh ichida qayta raqamlangan). */
export interface RatingGroup {
  groupId: string
  groupName: string
  rows: RatingRow[]
  /** O'quvchining shu guruhdagi o'rni (0 — ro'yxatda yo'q) */
  meRank: number
  size: number
}
export interface StudentRating {
  meStudentId: string
  /** ESKI nom — birinchi guruh qatorlari (orqaga moslik). Yangi kod `groups` dan foydalansin. */
  classRows: RatingRow[]
  schoolRows: RatingRow[]
  meSchoolRank?: number | null
  schoolSize: number
  /** Har bir faol guruh alohida (o'quvchi bir necha kursda o'qishi mumkin) */
  groups?: RatingGroup[]
}

export interface SubjectProgress {
  subjectId: string
  subjectName: string
  planned: number
  conducted: number
  remaining: number
  percent: number
  expectedByToday?: number
  nextLessonDate?: string | null
  lastLessonDate?: string | null
}
export interface StudentSubjectsProgress {
  quarter: number
  totalPlanned: number
  totalConducted: number
  totalPercent: number
  subjects: SubjectProgress[]
}
export interface SubjectLesson { date: string; period: number; startTime?: string; endTime?: string; topic: string; homework?: string; conducted: boolean; isPast: boolean }
export interface SubjectProgressDetail {
  subjectId: string
  subjectName: string
  quarter: number
  planned: number
  conducted: number
  remaining: number
  percent: number
  lessons: SubjectLesson[]
}


export interface MonthCourse { courseName: string; fee: number }
export interface MonthLedger { month: string; charged: number; discount: number; paid: number; remaining: number; status: string; courses: MonthCourse[] }
export interface StudentPayment { date: string; amount: number; note?: string | null; month?: string | null; comment?: string | null }
export interface StudentFinance {
  student: { id: string; fullName: string; className: string }
  balance: number
  monthlyFee: number
  totalCharged: number
  totalDiscount: number
  totalPaid: number
  months: MonthLedger[]
  payments: StudentPayment[]
}

export interface StudentChatMessage { id: string; className: string; senderUserId: string; senderName: string; senderRole: string; text: string; createdAt: string }
export interface UserSettings { language: string; theme: string; notificationsEnabled: boolean }
export interface TelegramStatus { configured: boolean; botUsername: string; botName: string; deepLink: string; registered: boolean }

// ---------- Profil / auth / meta ----------
const sid = (studentId?: string) => (studentId ? { studentId } : {})

export async function getStudentMe(studentId?: string) {
  const { data } = await api.get<StudentProfile>('/student/me', { params: sid(studentId) })
  return data
}
export async function getStudentSettings(studentId?: string) {
  const { data } = await api.get<UserSettings>('/student/settings', { params: sid(studentId) })
  return data
}
export async function saveStudentSettings(body: Partial<UserSettings>) {
  const { data } = await api.put<UserSettings>('/student/settings', body)
  return data
}
export async function changeStudentPassword(currentPassword: string, newPassword: string) {
  await api.put('/student/password', { currentPassword, newPassword })
}
export async function getStudentMeta() {
  const { data } = await api.get<PortalMeta>('/student/meta')
  return data
}
export async function getStudentSchool() {
  const { data } = await api.get<{ name: string; telegramChannel: string }>('/student/school')
  return data
}

// ---------- Uy joylashuvi ----------
export interface StudentLocation {
  latitude: number | null
  longitude: number | null
  address: string | null
  updatedAt: string | null
}
/** Saqlangan uy joylashuvini o'qish (hali yo'q bo'lsa null'lar). */
export async function getStudentLocation(studentId?: string) {
  const { data } = await api.get<StudentLocation>('/student/location', { params: sid(studentId) })
  return data
}
/** Uy joylashuvini yangilash (GPS yoki xaritadan tanlangan nuqta). */
export async function updateStudentLocation(latitude: number, longitude: number, address?: string) {
  await api.put('/student/location', { latitude, longitude, address: address ?? '' })
}
export async function getStudentTelegram(studentId?: string) {
  const { data } = await api.get<TelegramStatus>('/student/telegram', { params: sid(studentId) })
  return data
}

// ---------- O'quv dasturi (curriculum roadmap) ----------
export interface CurriculumItem {
  id: string
  text: string
  note: string
  order: number
  covered: boolean
  coveredDate: string
}
export interface CurriculumTopic {
  id: string
  title: string
  note: string
  order: number
  items: CurriculumItem[]
}
export interface CurriculumModule {
  id: string
  name: string
  note: string
  order: number
  topics: CurriculumTopic[]
}
export interface StudentCurriculum {
  groupId: string
  courseId: string
  courseName: string
  totalItems: number
  coveredCount: number
  revisionLessons: number
  totalLessons: number
  remainingItems: number
  estLessonsLeft: number
  lessonsPerWeek: number
  estFinishDate: string
  modules: CurriculumModule[]
}

/** O'quvchining har faol guruh kursi bo'yicha o'quv dasturi (o'tilgan/qolgan + prognoz). */
export async function getStudentCurriculum(studentId?: string) {
  const { data } = await api.get<StudentCurriculum[]>('/student/curriculum', { params: sid(studentId) })
  return data
}

// ---------- Baholash statistikasi (oylik + har darslik) ----------
export interface StudentGradingCriterion { id: string; name: string; done: number; total: number }
export interface StudentGradingDate { date: string; doneCriterionIds: string[] }
export interface StudentGradingGroup {
  groupId: string
  groupName: string
  months: string[]
  month: string
  dates: string[]
  criteria: StudentGradingCriterion[]
  lessons: StudentGradingDate[]
  /** Shu oyda yig'ilgan ball (bajarilgan mezonlar soni) */
  monthBall?: number
  /** Shu guruhda barcha vaqt bo'yicha yig'ilgan jami ball */
  totalBall?: number
}
export async function getStudentGrading(month?: string, studentId?: string) {
  const { data } = await api.get<StudentGradingGroup[]>('/student/grading', {
    params: { ...sid(studentId), ...(month ? { month } : {}) },
  })
  return data
}

// ---------- Dars kontenti (Duolingo node bosilganda) ----------
export type LessonType = 'text' | 'video' | 'audio' | 'vocab' | 'test' | 'pdf' | 'exercise'
export interface LessonVocab { term: string; meaning: string }
export interface LessonQuestion { id: string; text: string; options: string[]; correctIndex: number }
export interface LessonContent {
  id: string
  topicId: string
  text: string
  note: string
  order: number
  type: LessonType
  videoUrl: string
  audioUrl: string
  textContent: string
  pdfUrl: string
  pdfName: string
  meta: string
  vocab: LessonVocab[]
  questions: LessonQuestion[]
  /** Interaktiv mashq turi ("sentence-order", "reading-choice", ...) — "exercise" turida. */
  exerciseKind: string
  /** Interaktiv mashq mazmuni (JSON) — o'quvchi shu asosda mashqni ishlaydi. */
  exerciseJson: string
}
/** Bitta darsning to'liq kontenti (video/matn/audio/lug'at/test). */
export async function getStudentLesson(itemId: string, studentId?: string) {
  const { data } = await api.get<LessonContent>(`/student/curriculum/item/${itemId}`, { params: sid(studentId) })
  return data
}

/** Kurs o'quv dasturi o'tilgan bandlar (itemId'lar). */
export async function getStudentCourseProgress(courseId: string, studentId?: string): Promise<string[]> {
  const { data } = await api.get<string[]>(`/student/curriculum/${courseId}/progress`, { params: sid(studentId) })
  return data || []
}

/** Bandni bajarilgan/bajarilmagan deb belgilash. */
export async function setStudentCourseProgress(itemId: string, done: boolean, studentId?: string): Promise<void> {
  await api.post('/student/curriculum/progress', { itemId, done }, { params: sid(studentId) })
}

// ---------- Topshiriq urinishlari (o'quvchi natijasi) ----------

/** Bitta savol/element bo'yicha o'quvchi javobi (serverda AnswersJson ichida saqlanadi). */
export interface AttemptAnswer {
  index: number
  prompt: string
  answer: string
  expected: string
  ok: boolean
  sec: number
}

/** Yakunlangan urinish — mashq/test/ko'rish bo'limi tugaganda serverga yuboriladi. */
export interface AttemptPayload {
  itemId: string
  /** exercise — interaktiv mashq · test — dars ichidagi test · view — ko'rish (ballsiz). */
  section: 'exercise' | 'test' | 'view'
  exerciseKind?: string
  correct: number
  total: number
  durationSec: number
  answers?: AttemptAnswer[]
}

/**
 * O'quvchi topshiriqni yakunlaganda natijani saqlaydi (har chaqiruv YANGI urinish — tarix).
 * Server bandni avtomatik "bajarildi" ham qiladi.
 * Xato bo'lsa YUTILADI: internet uzilgani uchun o'quvchi ekranida natija ko'rsatilmay qolmasin.
 */
export async function saveCourseAttempt(payload: AttemptPayload): Promise<void> {
  try {
    await api.post('/student/curriculum/attempt', payload)
  } catch {
    /* natija ko'rsatilishi saqlanishga bog'liq emas */
  }
}

/** O'quvchining shu topshiriq bo'yicha oldingi urinishlari (eng yangisidan). */
export interface MyAttempt {
  id: string
  section: string
  exerciseKind: string
  attemptNo: number
  correct: number
  total: number
  scorePct: number
  durationSec: number
  finishedAt: string
}

export async function getMyCourseAttempts(itemId: string, studentId?: string): Promise<MyAttempt[]> {
  const { data } = await api.get<MyAttempt[]>(`/student/curriculum/item/${itemId}/attempts`, { params: sid(studentId) })
  return data || []
}

// ---------- Dashboard ----------
export async function getStudentDashboard(studentId?: string) {
  const { data } = await api.get<StudentDashboard>('/student/dashboard', { params: sid(studentId) })
  return data
}

// ---------- Academic ----------
export async function getStudentGrades(studentId?: string) {
  const { data } = await api.get<StudentGradesReport>('/student/grades', { params: sid(studentId) })
  return data
}
export interface AttendanceReasonCount { reasonId: string; name: string; short: string; isLate: boolean; count: number }
export interface MonthlyAttendance {
  missedDays: Record<string, number>
  illnessDays: Record<string, number>
  missedLessons: Record<string, number>
  illnessLessons: Record<string, number>
  lateCount: Record<string, number>
}
export interface MonthMarks { month: string; homeworkDone: number; homeworkMissed: number; behaviorGood: number; behaviorBad: number }
export interface StudentNotebook {
  id: string
  fullName: string
  className: string
  balance: number
  avgGrade: number
  subjects: SubjectRef[]
  /** fan nomi → oy ("yyyy-MM") → o'rtacha baho */
  grades: Record<string, Record<string, number>>
  attendance: MonthlyAttendance
  conducted: number
  attended: number
  attendancePct: number
  reasons: AttendanceReasonCount[]
  homeworkDone: number
  homeworkMissed: number
  behaviorGood: number
  behaviorBad: number
  marksTrend: MonthMarks[]
}

export async function getStudentNotebook(studentId?: string) {
  const { data } = await api.get<StudentNotebook>('/student/notebook', { params: sid(studentId) })
  return data
}
export async function getStudentAttendance(quarter = 1, studentId?: string) {
  const { data } = await api.get<StudentAttendanceFull>('/student/attendance', { params: { quarter, ...sid(studentId) } })
  return data
}
export async function getStudentRating(studentId?: string) {
  const { data } = await api.get<StudentRating>('/student/rating', { params: sid(studentId) })
  return data
}
export async function getStudentSubjectsProgress(quarter = 1, studentId?: string) {
  const { data } = await api.get<StudentSubjectsProgress>('/student/subjects-progress', { params: { quarter, ...sid(studentId) } })
  return data
}
export async function getStudentSubjectProgressDetail(subjectId: string, quarter = 1, studentId?: string) {
  const { data } = await api.get<SubjectProgressDetail>(`/student/subjects-progress/${subjectId}`, { params: { quarter, ...sid(studentId) } })
  return data
}


// ---------- Finance ----------
export async function getStudentFinance(studentId?: string) {
  const { data } = await api.get<StudentFinance>('/student/finance', { params: sid(studentId) })
  return data
}

// ---------- Chat ----------
export async function getStudentChat(since?: string, studentId?: string) {
  const { data } = await api.get<StudentChatMessage[]>('/student/chat', { params: { since, ...sid(studentId) } })
  return data
}
export async function sendStudentChat(text: string) {
  const { data } = await api.post<StudentChatMessage>('/student/chat', { text })
  return data
}

// ---------- Bildirishnomalar (ilova tarixi) ----------
export interface AppNotification {
  id: string
  title: string
  body: string
  type: string
  createdAt: string
  read: boolean
  confirmed: boolean
}
export interface NotificationsResponse {
  unread: number
  items: AppNotification[]
}

export async function getStudentNotifications(): Promise<NotificationsResponse> {
  const { data } = await api.get<NotificationsResponse>('/student/notifications')
  return data
}
export async function markStudentNotificationsRead(): Promise<void> {
  await api.post('/student/notifications/read')
}
export async function confirmStudentNotification(id: string): Promise<void> {
  await api.post(`/student/notifications/${id}/confirm`)
}

// ---------- Feedback ----------
export async function sendStudentFeedback(type: 'suggestion' | 'complaint', text: string, image?: File | null) {
  const fd = new FormData()
  fd.append('type', type)
  fd.append('text', text)
  if (image) fd.append('image', image)
  await api.post('/student/feedback', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
}

// ---------- Sertifikatlar ----------
export interface StudentCertificateDto {
  id: string
  courseName: string
  issuedAt: string
  expiresAt?: string | null
  status: string
  fileName: string
  downloadUrl: string
  downloadCount: number
  metadata?: Record<string, string> | null
}

export async function getStudentCertificates(): Promise<StudentCertificateDto[]> {
  const { data } = await api.get<StudentCertificateDto[]>('/student/certificates')
  return data
}

// ---------- Shartnoma ----------

/** O'quvchi (ota-ona) uchun tuzilgan shartnomalar — faqat ilovada ko'rinadiganlari */
export async function getStudentContracts(studentId?: string): Promise<ContractDoc[]> {
  const { data } = await api.get<ContractDoc[]>('/student/contracts', { params: sid(studentId) })
  return data
}
