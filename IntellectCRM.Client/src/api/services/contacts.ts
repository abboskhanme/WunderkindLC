import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

/**
 * "BOG'LANISH KERAK" — o'quvchi bilan bog'lanish navbati (follow-up).
 *
 * Bosqich va natija KALITLARI serverdan keladi (`getContactMeta`) — bu yerdagi tiplar faqat
 * shakl, yorliqlar emas. Yagona manba: backend `ContactService`.
 */

/** Talab bosqichi: navbatda turadiganlar `new`/`callback`, yakuniylar `done`/`failed`. */
export type ContactStatus = 'new' | 'callback' | 'done' | 'failed'

export interface ContactAttempt {
  id: string
  /** created | contact | note | reopen */
  type: string
  /** answered | no_answer | busy | wrong_number | other ('' — bog'lanish urinishi emas) */
  result: string
  resultLabel: string
  /** "Javobi nima dedi" */
  response: string
  nextStatus: string
  nextStatusLabel: string
  dueDate: string
  actorName: string
  createdAt: string
}

export interface ContactRequestItem {
  id: string
  studentId: string
  studentName: string
  reasonId: string
  reasonLabel: string
  note: string
  status: ContactStatus
  statusLabel: string
  /** Qayta qo'ng'iroq sanasi ("yyyy-MM-dd"), faqat `callback` da. */
  dueDate: string
  /** Muddati o'tganmi — serverda hisoblanadi (klient sanasiga ishonilmaydi). */
  overdue: boolean
  attemptCount: number
  lastResponse: string
  lastActorName: string
  lastActionAt: string
  createdAt: string
  createdBy: string
  closedAt: string
  closedBy: string
  /** O'quvchi + ota-ona raqamlari (takrorsiz). */
  phones: string[]
  /** Faqat bitta talab so'ralganda to'ladi. */
  history?: ContactAttempt[]
}

/** Navbatning MUDDAT guruhlari — "bugun kimga qo'ng'iroq qilish kerak?". */
export type ContactDue =
  | 'todo' | 'overdue' | 'today' | 'tomorrow' | 'week' | 'later' | 'nodate'

export interface ContactDueCounts {
  /** BUGUN qilinishi kerak = overdue + today + nodate. Operatorning asosiy raqami. */
  todo: number
  overdue: number
  today: number
  tomorrow: number
  /** Ertadan keyingi 6 kun (bugundan +2..+7). */
  week: number
  later: number
  /** Sana belgilanmagan ("Bog'lanish kerak" holatidagilar). */
  nodate: number
}

export interface ContactMeta {
  statuses: { key: string; label: string; isOpen: boolean; color: string }[]
  results: { key: string; label: string; reached: boolean }[]
  counts: { key: string; count: number }[]
  overdue: number
  /** Muddat kesimi (eski server javob bermasa — undefined). */
  due?: ContactDueCounts
  /** Yaqin 14 kun rejasi: qaysi kuni nechta qayta qo'ng'iroq (faqat ish BOR kunlar). */
  days?: { date: string; count: number }[]
}

export interface ContactDailyRow {
  date: string
  created: number
  attempts: number
  /** Odam bilan HAQIQATAN gaplashilgan urinishlar. */
  reached: number
  done: number
  callback: number
  failed: number
}

export interface ContactStats {
  from: string
  to: string
  created: number
  attempts: number
  reached: number
  done: number
  callback: number
  failed: number
  openNow: number
  overdueNow: number
  daily: ContactDailyRow[]
  byStaff: { actorName: string; attempts: number; reached: number; done: number; callback: number; failed: number }[]
  byReason: { reasonLabel: string; created: number; done: number; failed: number; open: number }[]
  byResult: { key: string; label: string; count: number }[]
  /** Javoblarda eng ko'p uchragan so'zlar — "nima deb yozilyapti" ni bir qarashda ko'rsatadi. */
  topWords?: { word: string; count: number }[]
  /** Javob YOZILGAN urinishlar soni (bo'sh javoblar hisobga olinmaydi). */
  withResponse?: number
}

const emptyMeta: ContactMeta = { statuses: [], results: [], counts: [], overdue: 0 }

/**
  * Bosqich/natija katalogi + navbat sanoqlari (sahifa bir so'rovda ochiladi).
  *
  * `month` ("yyyy-MM") — KUNLIK REJA qaysi oy uchun qaytsin. Chiplar va sanoqlar oyga
  * bog'liq emas: ular har doim JORIY holatni bildiradi.
  */
export async function getContactMeta(month?: string): Promise<ContactMeta> {
  if (USE_MOCK) {
    await delay()
    return emptyMeta
  }
  const { data } = await api.get<ContactMeta>('/admin/contacts/meta', { params: { month } })
  return data
}

/**
 * Navbat. `status` berilmasa — FAQAT ochiqlar (new + callback); `'all'` — hammasi.
 */
export async function getContactRequests(params: {
  status?: string
  q?: string
  overdue?: boolean
  /** MUDDAT guruhi — "bugun qilinadiganlar", "ertaga" va h.k. */
  due?: ContactDue
  /** ANIQ kun ("yyyy-MM-dd") — "yaqin kunlar" chizig'idan tanlanganda. */
  dueDate?: string
  /**
   * Ko'pi bilan nechta qator (server: 1..500, standart 200).
   *
   * Kanban taxtasi BUTUN navbatni bir so'rovda oladi va ustunlarga o'zi bo'ladi
   * (`lib/contactDue.ts`), shuning uchun u chegarani maksimumga qo'yadi.
   */
  limit?: number
} = {}): Promise<ContactRequestItem[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactRequestItem[]>('/admin/contacts', { params })
  return data
}

/** Bitta talab — TARIXI bilan. */
export async function getContactRequest(id: string): Promise<ContactRequestItem> {
  const { data } = await api.get<ContactRequestItem>(`/admin/contacts/${id}`)
  return data
}

/** O'quvchining barcha talablari (profil sahifasi uchun). */
export async function getStudentContactRequests(studentId: string): Promise<ContactRequestItem[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactRequestItem[]>(`/admin/contacts/student/${studentId}`)
  return data
}

/**
 * Yangi talab ("Bog'lanish kerak"). O'quvchida allaqachon ochiq talab bo'lsa server 400 qaytaradi
 * va javobda `existingId` beradi — klient o'sha talabni ocha oladi.
 */
export async function createContactRequest(payload: {
  studentId: string
  reasonId?: string
  note?: string
  dueDate?: string
}): Promise<ContactRequestItem> {
  const { data } = await api.post<ContactRequestItem>('/admin/contacts', payload)
  return data
}

/** BOG'LANILDI — natija + javobi + keyingi bosqich. */
export async function addContactAttempt(
  id: string,
  payload: { result: string; response?: string; nextStatus: string; dueDate?: string },
): Promise<ContactRequestItem> {
  const { data } = await api.post<ContactRequestItem>(`/admin/contacts/${id}/attempt`, payload)
  return data
}

/** Bosqichni o'zgartirmasdan izoh qo'shish. */
export async function addContactNote(id: string, text: string): Promise<ContactRequestItem> {
  const { data } = await api.post<ContactRequestItem>(`/admin/contacts/${id}/note`, { text })
  return data
}

/** Yakunlangan talabni qayta ochish. */
export async function reopenContactRequest(id: string, note?: string): Promise<ContactRequestItem> {
  const { data } = await api.post<ContactRequestItem>(`/admin/contacts/${id}/reopen`, { note })
  return data
}

export async function deleteContactRequest(id: string): Promise<void> {
  await api.delete(`/admin/contacts/${id}`)
}

/** Davr bo'yicha hisobot (kunlik oqim, xodimlar/sabablar/natijalar kesimi). */
export async function getContactStats(from?: string, to?: string): Promise<ContactStats> {
  const { data } = await api.get<ContactStats>('/admin/contacts/stats', { params: { from, to } })
  return data
}

/* ---------- Ko'plab qo'shish (o'quvchilar ro'yxatidan) ---------- */

export interface ContactBulkResult {
  created: number
  /** Ochiq talabi borligi uchun chetlab o'tilganlar. */
  skipped: number
  /** Ulardan bir nechtasining ismi (xabarda ko'rsatish uchun). */
  skippedNames: string[]
  /** Topilmagan o'quvchilar (ro'yxat eskirgan bo'lsa). */
  notFound: number
}

/**
 * Bir nechta o'quvchini birdan navbatga qo'shadi.
 *
 * ⚠️ Ochiq talabi bor o'quvchi CHETLAB O'TILADI (butun amal to'xtamaydi) — natijada
 * `skipped` qaytadi. Bitta o'quvchi uchun ham shu ishlatiladi.
 */
export async function createContactRequestsBulk(payload: {
  studentIds: string[]
  reasonId?: string
  note?: string
  dueDate?: string
}): Promise<ContactBulkResult> {
  if (USE_MOCK) {
    await delay()
    return { created: payload.studentIds.length, skipped: 0, skippedNames: [], notFound: 0 }
  }
  const { data } = await api.post<ContactBulkResult>('/admin/contacts/bulk', payload)
  return data
}

/* ---------- O'qituvchi tomoni (guruh jurnalidagi "Aloqa" tabi) ---------- */

/**
 * Bog'lanish sabablari — O'QITUVCHI uchun (`/api/teacher/...`).
 * Admin endpointi (`/admin/action-reasons`) o'qituvchiga yopiq.
 */
export async function getTeacherContactReasons(): Promise<{ id: string; label: string }[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<{ id: string; label: string }[]>('/teacher/contact-reasons')
  return data
}

/**
 * O'qituvchi o'z guruhidagi o'quvchi(lar)ni navbatga yuboradi.
 * SANA YO'Q — talab darhol navbatga tushadi (bugungi ish).
 */
export async function sendTeacherGroupContacts(
  classId: string,
  payload: { studentIds: string[]; reasonId?: string; note?: string },
): Promise<ContactBulkResult> {
  const { data } = await api.post<ContactBulkResult>(`/teacher/groups/${classId}/contacts`, payload)
  return data
}

/* ---------- Javoblar tahlili ---------- */

/** Yozilgan javob ("javobi nima dedi") — hisobotdagi javoblar lentasi. */
export interface ContactResponseRow {
  id: string
  requestId: string
  studentId: string
  studentName: string
  reasonLabel: string
  result: string
  resultLabel: string
  nextStatus: string
  nextStatusLabel: string
  response: string
  actorName: string
  createdAt: string
}

/** Javob YOZILGAN urinishlar (bo'sh javoblar qaytmaydi) — o'qish uchun. */
export async function getContactResponses(params: {
  from?: string
  to?: string
  result?: string
  actor?: string
  q?: string
  limit?: number
} = {}): Promise<ContactResponseRow[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactResponseRow[]>('/admin/contacts/responses', { params })
  return data
}

/* ---------- Kunlik jurnal ("bugun kimga qo'ng'iroq qilindi") ---------- */

/** Jurnaldagi BITTA hodisa: kimga, qachon, nima deyilgani. */
export interface ContactJournalItem {
  id: string
  requestId: string
  studentId: string
  studentName: string
  reasonLabel: string
  /** created | contact | note | reopen */
  type: string
  typeLabel: string
  result: string
  resultLabel: string
  nextStatus: string
  nextStatusLabel: string
  dueDate: string
  response: string
  actorName: string
  /** "HH:mm" — jurnalda soat ko'rinadi. */
  time: string
  createdAt: string
  /** O'quvchi + ota-ona raqamlari — jurnaldan darhol qayta qo'ng'iroq qilish uchun. */
  phones: string[]
}

/** BITTA KUN — jamlanmasi va hodisalari (kun ichida ertalabdan kechgacha). */
export interface ContactJournalDay {
  date: string
  created: number
  attempts: number
  reached: number
  done: number
  callback: number
  failed: number
  items: ContactJournalItem[]
}

/**
 * KUNLIK JURNAL — har kun alohida: kimga qo'ng'iroq qilindi, qachon, nima dedi, qaysi sabab bilan.
 *
 * Kunlar yangisidan eskisiga; chegara ENG YANGI hodisalardan olinadi (uzun davr tanlanganda
 * eng so'nggilari qaytadi).
 */
export async function getContactJournal(params: {
  from?: string
  to?: string
  /** Faqat shu turdagi hodisalar (vergul bilan): contact | created | note | reopen. */
  type?: string
  limit?: number
} = {}): Promise<ContactJournalDay[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactJournalDay[]>('/admin/contacts/journal', { params })
  return data
}

/* ---------- AI tahlil (sabablar, javoblar va natijalar bo'yicha) ---------- */

/** Sohaviy baholar (0..100). */
export interface ContactAiScores {
  /** Navbat ishlanyaptimi (ochiq/muddati o'tganlarga nisbatan urinishlar). */
  qamrov: number
  /** Odam bilan haqiqatan gaplashish ulushi. */
  aloqa: number
  /** Bog'lanishlar natija berdimi. */
  natija: number
  /** "Javobi nima dedi" to'ldirilyaptimi va mazmunlimi. */
  sifat: number
  umumiy: number
}

/** AI yozgan narrativ — bo'sh maydon ekranda umuman chizilmaydi. */
export interface ContactAiNarrative {
  umumiy: string
  sabablar: string
  javoblar: string
  sifat: string
  xodimlar: string
  ozgarishlar: string
  kuchli: string[]
  zaif: string[]
  xavflar: string[]
  tavsiyalar: string[]
  baholar: ContactAiScores
  trend: string
}

/** Promptga ketgan javob namunasi — o'quvchi ismi/telefoni ATAYIN yo'q (maxfiylik). */
export interface ContactAiSample {
  date: string
  reasonLabel: string
  resultLabel: string
  nextStatusLabel: string
  response: string
  actorName: string
}

/** Tahlil paytidagi DETERMINISTIK raqamlar — hisobot sahifasidagi sonlar bilan AYNAN bir xil. */
export interface ContactAiMetrics extends ContactStats {
  samples: ContactAiSample[]
}

/** Saqlangan bitta tahlil (raqamlari bilan — eski tahlil ochilganda ham to'liq ko'rinadi). */
export interface ContactAiRecord {
  id: string
  /** Tahlil qilingan DAVR. */
  from: string
  to: string
  /** Tahlil YARATILGAN kun ("yyyy-MM-dd"). */
  date: string
  createdAt: string
  model: string
  overallScore: number
  ai: ContactAiNarrative
  metrics: ContactAiMetrics
}

/**
 * ⚠️ `alreadyToday=true` — XATO EMAS: shu DAVR uchun bugun tahlil qilingan, `record` da o'sha
 * qaytadi (Gemini qayta chaqirilmaydi). Xato faqat `ok=false` da — matn `error` da.
 */
export interface ContactAiResponse {
  ok: boolean
  alreadyToday: boolean
  record: ContactAiRecord | null
  error: string | null
}

/** Saqlangan tahlillar — eng yangisi birinchi. Davr berilsa faqat AYNI o'sha davrniki. */
export async function getContactAiAnalyses(from?: string, to?: string): Promise<ContactAiRecord[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ContactAiRecord[]>('/admin/contacts/ai-analyses', {
    params: { from, to },
  })
  return data
}

/** Tanlangan davr uchun yangi AI tahlil (shu davr uchun kuniga bir marta — serverda darvozalangan). */
export async function runContactAiAnalysis(from?: string, to?: string): Promise<ContactAiResponse> {
  const { data } = await api.post<ContactAiResponse>('/admin/contacts/ai-analysis', { from, to })
  return data
}
