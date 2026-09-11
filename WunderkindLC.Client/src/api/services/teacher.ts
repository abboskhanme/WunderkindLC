import type {
  ChatMessage,
  GroupTest,
  OnlineTest,
  PortalMeta,
  SalaryLedger,
  Subject,
  TeacherClass,
  TestResultDetail,
  TeacherRating,
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
export async function getTeacherSchool(): Promise<{ name: string; telegramChannel: string }> {
  if (USE_MOCK) return { name: '', telegramChannel: '' }
  const { data } = await api.get<{ name: string; telegramChannel: string }>('/teacher/school')
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

/* ---------- Guruh chati ---------- */

export async function getTeacherChatClasses(): Promise<string[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<string[]>('/teacher/chat/classes')
  return data
}

export async function getTeacherChat(className: string, since?: string): Promise<ChatMessage[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<ChatMessage[]>(`/teacher/chat/${encodeURIComponent(className)}`, {
    params: since ? { since } : undefined,
  })
  return data
}

export async function sendTeacherChat(className: string, text: string): Promise<ChatMessage> {
  const { data } = await api.post<ChatMessage>(`/teacher/chat/${encodeURIComponent(className)}`, { text })
  return data
}

/**
 * Har bir kanal uchun oxirgi xabar vaqti — o'qilmagan xabarlarni aniqlash uchun (unread-context).
 * Qaytadi: { [channelName]: ISO vaqt yoki null (xabari yo'q kanal) }
 */
export async function getTeacherLastMessages(): Promise<Record<string, string | null>> {
  if (USE_MOCK) return {}
  const { data } = await api.get<Record<string, string | null>>('/teacher/chat/last-messages')
  return data
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

/** Bitta guruhning testlar ro'yxati. */
export async function getTeacherGroupTests(classId: string): Promise<GroupTest[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<GroupTest[]>('/teacher/test-results', { params: { classId } })
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
  maxScore: number
  /** Onlayn test sozlamalari; berilmasa (yoki mode="offline") — oflayn test. */
  online?: OnlineTest
  /** true — test natijasi bo'yicha sertifikat beriladi */
  certificateEnabled?: boolean
  /** Sertifikat shabloni; bo'sh/null — standart shablon */
  certificateTemplateId?: string | null
}): Promise<GroupTest> {
  const { data } = await api.post<GroupTest>('/teacher/test-results', payload)
  return data
}

export async function updateTeacherTest(
  id: string,
  payload: {
    name: string
    date: string
    maxScore: number
    online?: OnlineTest
    /** true — test natijasi bo'yicha sertifikat beriladi */
    certificateEnabled?: boolean
    /** Sertifikat shabloni; bo'sh/null — standart shablon */
    certificateTemplateId?: string | null
  },
): Promise<void> {
  await api.put(`/teacher/test-results/${id}`, payload)
}

/** Yuklangan fayl metadatasi (test savollari PDF'i va shunga o'xshash yuklamalar uchun) */
export interface MaterialInput {
  name: string
  url: string
  size: number
  contentType: string
  /** Ixtiyoriy hamrohlik audio (masalan shu materialni ovoz chiqarib o'qigan yozuv) */
  audioUrl?: string | null
}

/** Onlayn test savollari (PDF) faylini yuklash; yuklangan fayl metadatasini qaytaradi. */
export async function uploadTeacherTestFile(file: File): Promise<MaterialInput> {
  const fd = new FormData()
  fd.append('file', file)
  const { data } = await api.post<MaterialInput>('/teacher/test-results/uploads', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
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
