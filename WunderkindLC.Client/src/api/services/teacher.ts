import type {
  GroupTest,
  PortalMeta,
  SalaryLedger,
  Subject,
  TeacherClass,
  TestResultDetail,
  TeacherRating,
  SendTestResultsResult,
  TestScoreBatchResult,
  TestScoreBatchRow,
} from '@/types'
import { api, USE_MOCK } from '../client'
import { delay } from '@/lib/utils'
import type { GroupJournal, LessonReschedule } from './journal'
import type { GroupCurriculum } from './curriculum'
import type { GradingBoard, SetGrade, BulkGrade } from './grading'
import type { TeacherRetentionSummary } from './retentionBonus'

/** O'qituvchi profili (panel sarlavhasi/salom uchun) */
export interface TeacherProfile {
  id: string
  fullName: string
  email: string
  homeroomClass: string
  subjects: Subject[]
  /** Support o'qituvchimi (bo'sh vaqt/bron bo'limi ko'rinadimi). */
  isSupport?: boolean
}

export async function getTeacherProfile(): Promise<TeacherProfile | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<TeacherProfile>('/teacher/me')
  return data
}

/** O'qituvchi dars beradigan guruhlar (har biri o'qitadigan fanlari bilan) */
export async function getMyClasses(): Promise<TeacherClass[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherClass[]>('/teacher/classes')
  return data
}

/* ---------- Meta (choraklar, dars vaqtlari, davomat sabablari) ---------- */

export async function getTeacherMeta(): Promise<PortalMeta | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<PortalMeta>('/teacher/meta')
  return data
}

/** Markaz nomi + Telegram kanali (o'qituvchi ilovasi uchun). */
export async function getTeacherSchool(): Promise<{ name: string; telegramChannel: string; logoUrl?: string }> {
  if (USE_MOCK) return { name: '', telegramChannel: '' }
  const { data } = await api.get<{ name: string; telegramChannel: string; logoUrl?: string }>('/teacher/school')
  return data
}

/* ---------- Bildirishnomalar (ilova tarixi) ---------- */
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

export async function getTeacherNotifications(): Promise<NotificationsResponse> {
  if (USE_MOCK) return { unread: 0, items: [] }
  const { data } = await api.get<NotificationsResponse>('/teacher/notifications')
  return data
}
export async function markTeacherNotificationsRead(): Promise<void> {
  await api.post('/teacher/notifications/read')
}
export async function confirmTeacherNotification(id: string): Promise<void> {
  await api.post(`/teacher/notifications/${id}/confirm`)
}

/* ---------- Baholash mezonlari (o'z guruhi) ---------- */
export async function getTeacherGradingBoard(groupId: string, month?: string): Promise<GradingBoard> {
  const { data } = await api.get<GradingBoard>(`/teacher/grading/group/${groupId}/board`, {
    params: month ? { month } : {},
  })
  return data
}
export async function setTeacherGrade(req: SetGrade): Promise<void> {
  await api.post('/teacher/grading/grade', req)
}
export async function bulkTeacherGrade(req: BulkGrade): Promise<void> {
  await api.post('/teacher/grading/grade/bulk', req)
}

/* ---------- Taklif va shikoyat (o'qituvchi → admin) ---------- */
export async function sendTeacherFeedback(
  type: 'suggestion' | 'complaint',
  text: string,
  image?: File | null,
): Promise<void> {
  const fd = new FormData()
  fd.append('type', type)
  fd.append('text', text)
  if (image) fd.append('image', image)
  await api.post('/teacher/feedback', fd, { headers: { 'Content-Type': 'multipart/form-data' } })
}

/* ---------- Maosh (faqat o'ziniki) ---------- */

export async function getTeacherSalary(from?: string, to?: string): Promise<SalaryLedger | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<SalaryLedger>('/teacher/salary', {
    params: { from, to },
  })
  return data
}

/**
 * O'quvchini ushlab turish bonuslari (faqat o'ziniki): berilganlar (`items`) va oylari
 * to'planayotgan (o'quvchi × fan) sikllari (`inProgress` — backend `null` yuborishi mumkin).
 * Maosh raqamlariga QO'SHILMAGAN — alohida qayd; pul odatdagi maosh to'lovi orqali beriladi.
 */
export async function getMyRetentionBonuses(): Promise<TeacherRetentionSummary | null> {
  if (USE_MOCK) return null
  const { data } = await api.get<TeacherRetentionSummary>('/teacher/retention-bonus')
  return data
}

/* ---------- Guruh OYLIK jurnali (o'qituvchi guruh sahifasi — admin bilan bir xil shakl) ---------- */

interface TeacherEntryPayload {
  grade?: number | null
  reasonId?: string | null
  /** 0 = belgilanmagan, 1 = qildi, 2 = qilmadi, 3 = chala qildi */
  homework?: number
  behavior?: number
  mastery?: number | null
  /** ANIQ "keldi (bor)" belgisi — presentDefaultFrom cheklovidan qat'i nazar yashil ✓ */
  present?: boolean
}

/** Guruhning bitta oylik jurnali (ustunlar guruh dars kunlaridan, qatorlar faqat faol o'quvchilar). */
export async function getTeacherGroupJournal(classId: string, month?: string): Promise<GroupJournal> {
  if (USE_MOCK) {
    return {
      group: { id: classId, name: '', courseId: '', courseName: '', teacherName: '', days: [], startTime: '', endTime: '', room: '', startDate: '', monthlyFee: 0 },
      months: [], month: month ?? '', columns: [], students: [], entries: [], conductedDates: [], reschedules: [],
    }
  }
  const { data } = await api.get<GroupJournal>('/teacher/journal/group', { params: { classId, month } })
  return data
}

/** Bitta katakni belgilash (baho/davomat/uy vazifa/xulq/o'zlashtirish). subjectId = guruh kursi. */
export async function setTeacherJournalEntry(
  classId: string,
  courseId: string,
  studentId: string,
  date: string,
  payload: TeacherEntryPayload,
): Promise<void> {
  await api.put('/teacher/journal', {
    classId, subjectId: courseId, quarter: 1, studentId, date, period: 1, ...payload,
  })
}

/** Bitta katakni tozalash. */
export async function clearTeacherJournalEntry(
  classId: string,
  courseId: string,
  studentId: string,
  date: string,
): Promise<void> {
  await api.delete('/teacher/journal', {
    params: { classId, subjectId: courseId, quarter: 1, studentId, date, period: 1 },
  })
}

/** Bitta dars (sana) uchun BARCHA faol o'quvchiga birdan davomat. absent=false → keldi; true → kelmadi.
 * Backend `BulkAttendanceRequest` subjectId/period/studentIds'ni ham talab qiladi (bo'lmasa 400 qaytadi). */
export async function bulkTeacherAttendance(
  classId: string,
  subjectId: string,
  period: number,
  studentIds: string[],
  date: string,
  absent: boolean,
  reasonId?: string | null,
): Promise<void> {
  await api.post('/teacher/journal/bulk-attendance', {
    classId, subjectId, period, studentIds, date, absent, reasonId: reasonId ?? null,
  })
}

/** Bitta darsni bir martalik boshqa kunga ko'chiradi (admin bilan bir xil), faqat o'z guruhi uchun. */
export async function rescheduleTeacherLesson(
  classId: string,
  fromDate: string,
  toDate: string,
  time?: string,
): Promise<LessonReschedule> {
  if (USE_MOCK) {
    await delay(120)
    return { id: 'mock', fromDate, toDate, time }
  }
  const { data } = await api.post<LessonReschedule>('/teacher/journal/reschedule', {
    classId, fromDate, toDate, time: time || null,
  })
  return data
}

/** Ko'chirishni bekor qiladi — dars asl kuniga qaytadi. */
export async function cancelTeacherReschedule(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    return
  }
  await api.delete(`/teacher/journal/reschedule/${id}`)
}

/* ---------- Guruh o'quv dasturi (darsda o'tilgan bandlar + tugatish prognozi) ---------- */

export async function getTeacherGroupCurriculum(groupId: string): Promise<GroupCurriculum> {
  const { data } = await api.get<GroupCurriculum>(`/teacher/curriculum/group/${groupId}`)
  return data
}

export async function setTeacherGroupCover(groupId: string, itemId: string, covered: boolean): Promise<void> {
  await api.post(`/teacher/curriculum/group/${groupId}/cover`, { itemId, covered })
}

export async function changeTeacherGroupRevision(groupId: string, delta: number): Promise<void> {
  await api.post(`/teacher/curriculum/group/${groupId}/revision`, { delta })
}

/* ---------- O'quvchilar reytingi (o'z guruhlari, ball bo'yicha) ---------- */

/**
 * Ball = jurnal baholari yig'indisi + bajarilgan baholash mezonlari (faqat o'z guruhlarida).
 * `month` ("yyyy-MM") berilmasa — Umumiy (barcha vaqt), ya'ni avvalgi xatti-harakat.
 */
export async function getMyStudentRating(month?: string): Promise<TeacherRating | null> {
  if (USE_MOCK) return null
  const q = month ? `?month=${encodeURIComponent(month)}` : ''
  const { data } = await api.get<TeacherRating>(`/teacher/rating${q}`)
  return data
}

/* ---------- Test natijalari (o'z guruhlari) ---------- */

/** Bitta guruhning testlar ro'yxati (CRM U4a: `month` "yyyy-MM" — bo'sh = hammasi, `kind` — tur). */
export async function getTeacherGroupTests(
  classId: string,
  filter: { month?: string; kind?: string } = {},
): Promise<GroupTest[]> {
  if (USE_MOCK) return []
  const params: Record<string, string> = { classId }
  if (filter.month) params.month = filter.month
  if (filter.kind) params.kind = filter.kind
  const { data } = await api.get<GroupTest[]>('/teacher/test-results', { params })
  return data
}

/** Test tafsiloti — o'quvchilar + ballari (ball desc). */
export async function getTeacherTestDetail(id: string): Promise<TestResultDetail> {
  const { data } = await api.get<TestResultDetail>(`/teacher/test-results/${id}`)
  return data
}

export async function createTeacherTest(payload: {
  groupId: string
  name: string
  date: string
  /** CRM U4a: «monthly» | «interim» — SHART. */
  kind?: string
  /** CRM U4a: «Savollar soni» 1..200 — SHART (maks ball = savollar soni). */
  questionCount?: number | null
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled?: boolean
  /** Sertifikat shabloni; bo'sh/null — standart shablon */
  certificateTemplateId?: string | null
}): Promise<GroupTest> {
  const { data } = await api.post<GroupTest>('/teacher/test-results', { maxScore: 0, ...payload })
  return data
}

export async function updateTeacherTest(
  id: string,
  payload: {
    name: string
    date: string
    kind?: string
    /** «Savollar soni»; null — o'zgarmaydi (eski testda `maxScore` tahrirlanadi). */
    questionCount?: number | null
    /** Faqat ESKI (savollar sonisiz) test uchun. */
    maxScore?: number
    /** true — test natijasi bo'yicha sertifikat beriladi */
    certificateEnabled?: boolean
    /** Sertifikat shabloni; bo'sh/null — standart shablon */
    certificateTemplateId?: string | null
  },
): Promise<void> {
  await api.put(`/teacher/test-results/${id}`, { maxScore: 0, ...payload })
}

/** CRM U4a: jadvaldan bir nechta katakni bittada saqlash (o'z guruhi) — noto'g'rilari `errors` da. */
export async function saveTeacherTestScoresBatch(
  id: string,
  rows: TestScoreBatchRow[],
): Promise<TestScoreBatchResult> {
  const { data } = await api.put<TestScoreBatchResult>(`/teacher/test-results/${id}/scores/batch`, { rows })
  return data
}

/** CRM U4a: «Ota-onalarga yuborish» (o'z guruhi) — faqat o'zgargan natijalar (`resend` — hammasini). */
export async function sendTeacherTestResults(id: string, resend = false): Promise<SendTestResultsResult> {
  const { data } = await api.post<SendTestResultsResult>(`/teacher/test-results/${id}/send-results`, { resend })
  return data
}

export async function deleteTeacherTest(id: string): Promise<void> {
  await api.delete(`/teacher/test-results/${id}`)
}

/** Bitta o'quvchiga ball qo'yish/tozalash (score=null). Qaytadi: qayta saralangan tafsilot. */
export async function setTeacherTestScore(
  id: string,
  studentId: string,
  score: number | null,
): Promise<TestResultDetail> {
  const { data } = await api.put<TestResultDetail>(`/teacher/test-results/${id}/scores`, {
    studentId,
    score,
  })
  return data
}

/* ---------- Bosh sahifa · Dars jadvali · O'quvchilarim ---------- */
// Backend: TeacherPortalController.Home.cs → TeacherHomeService (faqat o'qish).

/** O'qituvchining bitta darsi (aniq sanada). */
export interface TeacherLesson {
  groupId: string
  groupName: string
  courseName: string
  /** "yyyy-MM-dd" */
  date: string
  /** "HH:mm" (bo'sh bo'lishi mumkin — guruhda vaqt belgilanmagan) */
  startTime: string
  endTime: string
  room: string
  /** Jurnalda ko'rinadigan (muzlatilmagan) o'quvchilar soni */
  studentCount: number
  /** Darsni o'qituvchi O'RINBOSAR sifatida o'tadi */
  isSubstitute: boolean
  /** O'z guruhi, lekin shu kuni darsni boshqa odam o'tadi — uning ismi */
  substitutedBy: string
  /** Boshqa kundan shu kunga ko'chirilgan dars */
  moved: boolean
  /** Jurnalda "o'tildi" (bitta baho ham yetadi) — DAVOMAT OLINDI degani EMAS. */
  conducted: boolean
  /** Davomat olingan: shu kuni guruhning birinchi davomat belgisi bor (`LessonNote.AttendanceTakenAt` — eslatma, jarima
   *  va monitor bilan bir xil yagona ta'rif, journal.md). Dars holati, «Davomat kutilmoqda» va tezkor davomat SHUNGA
   *  qaraydi. Eski server javobida bo'lmasligi mumkin (undefined = olinmagan). */
  attendanceTaken?: boolean
}

export interface TeacherGroupCard {
  id: string
  name: string
  courseId: string
  courseName: string
  /** 0=Dushanba … 6=Yakshanba */
  days: number[]
  startTime: string
  endTime: string
  room: string
  startDate: string
  active: number
  trial: number
  frozen: number
  isSubstitute: boolean
  /** "yyyy-MM-dd" yoki "" (yaqin 3 haftada dars yo'q / jadval ruxsati yo'q) */
  nextLessonDate: string
  /** Joriy oy davomati % (faqat o'z guruhida; dars o'tilmagan bo'lsa null). */
  attendancePct?: number | null
  /** Qarzdor o'quvchilar SONI (faqat o'z guruhida; summa berilmaydi). */
  debtorCount?: number | null
}

export interface TeacherOverview {
  today: string
  groups: TeacherGroupCard[]
  todayLessons: TeacherLesson[]
  studentCount: number
  /** "Dars jadvali" ruxsati bormi (yo'q bo'lsa todayLessons bo'sh keladi) */
  canSchedule: boolean
  /** O'z guruhlaridagi qarzdorlar soni va jami qarz (faqat JAMI — o'quvchi kesimi ko'rsatilmaydi). */
  debtorCount?: number
  debtTotal?: number
}

export interface TeacherDebtSummary {
  debtorCount: number
  debtTotal: number
}

/** O'z guruhlaridagi qarzdorlik jamlanmasi (maosh sahifasi: "qarzdorlar to'lasa"). */
export async function getTeacherDebtSummary(): Promise<TeacherDebtSummary> {
  if (USE_MOCK) return { debtorCount: 0, debtTotal: 0 }
  const { data } = await api.get<TeacherDebtSummary>('/teacher/debt-summary')
  return data
}

export interface TeacherStudentRow {
  studentId: string
  fullName: string
  photoUrl: string
  groupId: string
  groupName: string
  courseName: string
  /** 'active' | 'trial' | 'frozen' */
  status: string
  memberStart: string
  /** Shu guruh bo'yicha qarzdor oylar soni (0 — qarzi yo'q) */
  debtMonths: number
  /** Tanlangan oyda o'tilgan (hisobga olinadigan) darslar */
  held: number
  attended: number
  absent: number
  late: number
  /** Davomat foizi; dars o'tilmagan bo'lsa null */
  attendancePct: number | null
  /** CRM upgrade U6b: botga ulangan (bloklamagan) Telegram chatlari soni — raqam/ism YO'Q. */
  telegramLinked?: number
  /** U6b: ulangan chatlarning qarindoshliklari. */
  telegramRelations?: string[] | null
  /** 2026-10-15: kabinetga kirilgan — «Ulangan» deb sanaladi. */
  cabinetLoggedIn?: boolean
}

export interface TeacherStudents {
  month: string
  rows: TeacherStudentRow[]
}

export async function getTeacherOverview(): Promise<TeacherOverview> {
  if (USE_MOCK) return { today: '', groups: [], todayLessons: [], studentCount: 0, canSchedule: true }
  const { data } = await api.get<TeacherOverview>('/teacher/overview')
  return data
}

/** Oraliqdagi darslar ("yyyy-MM-dd", ikkalasi ham kiradi; ko'pi bilan 62 kun). */
export async function getTeacherSchedule(from: string, to: string): Promise<TeacherLesson[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherLesson[]>('/teacher/schedule', { params: { from, to } })
  return data
}

/** Barcha guruhlardagi o'quvchilar + oylik davomat (`month` "yyyy-MM", ixtiyoriy). */
export async function getTeacherStudents(month?: string): Promise<TeacherStudents> {
  if (USE_MOCK) return { month: month ?? '', rows: [] }
  const { data } = await api.get<TeacherStudents>('/teacher/students', { params: { month } })
  return data
}

/* ---------- Guruh o'quvchilari (o'qituvchi tahriri) ---------- */

/**
 * O'z guruhining faol a'zosi — guruh sahifasidagi «O'quvchilar» tabi uchun. Telefon BOR (o'qituvchi
 * uni tahrirlaydi), balans va ota-ona ma'lumoti YO'Q.
 */
export interface TeacherGroupStudent {
  id: string
  fullName: string
  phone: string
  /** JORIY maktab sinfi (server har yili o'zi oshiradi; `lib/schoolGrades.ts`). */
  schoolGradeNow: number | null
  /** A'zolik holati: `active` | `trial` | `frozen`. */
  status: string
  /** CRM upgrade U6b: botga ulangan Telegram chatlari soni (tahrir javobida 0 keladi — saqlab qoling). */
  telegramLinked?: number
  telegramRelations?: string[] | null
  /** 2026-10-15: kabinetga kirilgan — «Ulangan» deb sanaladi. */
  cabinetLoggedIn?: boolean
}

/** O'qituvchi o'zgartira oladigan FAQAT uch maydon. `schoolGrade`: 0 — tozalash. */
/** O'qituvchi tahriri — FAQAT ism va sinf. Telefon raqamlari o'qituvchiga YOPIQ (H1, 2026-10-12): ular Telegram
 *  botida ro'yxatdan o'tish uchun moslash maydoni (kirish omili) — server bu maydonni umuman qabul qilmaydi. */
export interface TeacherStudentEditPayload {
  fullName: string
  schoolGrade: number
}

export async function getTeacherGroupStudents(classId: string): Promise<TeacherGroupStudent[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<TeacherGroupStudent[]>(`/teacher/groups/${classId}/students`)
  return data
}

export async function updateTeacherGroupStudent(
  classId: string,
  studentId: string,
  payload: TeacherStudentEditPayload,
): Promise<TeacherGroupStudent> {
  const { data } = await api.put<TeacherGroupStudent>(`/teacher/groups/${classId}/students/${studentId}`, payload)
  return data
}
