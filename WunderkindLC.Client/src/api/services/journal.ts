import type { JournalColumn, JournalEntry, JournalTopic, MasteryLevel } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'
import { journalMock } from '../mock/journal'
import { journalTopicsMock } from '../mock/journalTopics'

const gkey = (classId: string, subjectId: string, quarter: number) =>
  `${classId}-${subjectId}-${quarter}`

/** Berilgan sanada o'tilgan darslar (guruh+fan+dars raqami) — bosh sahifada yashil/qizil ko'rsatish uchun */
export interface ConductedLesson {
  classId: string
  subjectId: string
  period: number
}

export async function getConductedLessons(date: string): Promise<ConductedLesson[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<ConductedLesson[]>('/admin/journal/conducted', {
    params: { date },
  })
  return data
}

/* ---------- Guruh OYLIK jurnali (guruh sahifasi) ---------- */

export interface GroupJournalInfo {
  id: string
  name: string
  courseId: string
  courseName: string
  teacherName: string
  days: number[]
  startTime: string
  endTime: string
  room: string
  startDate: string
  monthlyFee: number
}
/** O'quvchi o'qituvchi jurnalida NEGA ko'rinmayotgani (to'lov nazorati sozlamasi bo'yicha).
 *  '' = yashirilmagan; 'prevMonth' = o'tgan oy(lar) uchun to'lov yo'q;
 *  'cutoff' = joriy oy uchun belgilangan sanagacha to'lov yo'q. */
export type PaymentHiddenReason = 'prevMonth' | 'cutoff' | ''

export interface GroupJournalStudent {
  studentId: string
  fullName: string
  status: string
  activatedAt: string
  /** SHU GURUH bo'yicha balans (manfiy = qarz) — o'quvchining umumiy balansi EMAS. Ko'p guruhda
   *  o'qiydigan o'quvchi to'lagan guruhida yashil, to'lamaganida qizil ko'rinadi (server: GroupBalanceService). */
  balance: number
  /** SHU GURUH bo'yicha to'liq yopilmagan (qarzdor) OYLAR soni. 0 = qarz yo'q, 1 = bir oylik qarz
   *  (qizil), 2+ = og'ir qarzdorlik — jurnalda binafsha-pushti (fuchsia) rangda ko'rsatiladi. */
  debtMonths: number
  /** O'quvchi guruhda boshlangan sana ("yyyy-MM-dd"). Undan oldingi darslarga davomat/baho kiritilmaydi
   *  (bu sanadan oldingi kataklar bloklanadi). Aktivlashtirilgan bo'lsa ActivatedAt, aks holda JoinedAt. */
  memberStart: string
  /** Shu sanadan OLDINGI (memberStart bilan bu orasidagi, orqaga sanalgan) o'tilgan darslarda yozuv
   *  bo'lmasa ham katak avtomatik "keldi" (yashil ✓) bo'lib ko'rsatilmaydi — bo'sh qoladi, o'qituvchi
   *  qo'lda belgilashi kerak (bloklanmaydi, faqat default holat farq qiladi). Bo'sh = cheklovsiz. */
  presentDefaultFrom: string
  /** Muzlatilgan sana ("yyyy-MM-dd") — status=="frozen" bo'lsa. Bo'sh = muzlatilmagan. Shu sanadan
   *  KEYINGI o'tilgan darslarda o'quvchi qatnashmagan, shuning uchun avto-"keldi" ✓ ko'rsatilmaydi. */
  frozenAt: string
  /** true — to'lov nazorati sozlamasi bo'yicha bu o'quvchi O'QITUVCHI jurnalida KO'RINMAYDI
   *  (o'qituvchi unga davomat/baho qo'ya olmaydi). Bu MUZLATISH EMAS: a'zolik va oylik hisob
   *  odatdagidek davom etadi, to'lov qilinishi bilan qator o'z-o'zidan qaytadi. ADMIN jurnalida
   *  esa qator har doim ko'rinadi — faqat shu belgi bilan. */
  paymentHidden: boolean
  /** Yashirish sababi — `paymentHidden` true bo'lganda to'ldiriladi. */
  paymentHiddenReason: PaymentHiddenReason
  /** O'quvchining profil surati ("/uploads/..."), yuklanmagan bo'lsa bo'sh satr. Jurnalda F.I.SH
   *  ustiga bosilganda ochiladi — o'qituvchi o'quvchini yuzidan tanishi uchun. Eski javoblarda
   *  bo'lmasligi mumkin (shuning uchun ixtiyoriy). */
  photoUrl?: string
}
/** Bitta darsning bir martalik boshqa kunga ko'chirilishi (shu oyga tegishli). */
export interface LessonReschedule {
  id: string
  /** Asl (endi yo'q) kun */
  fromDate: string
  /** Yangi kun (jurnalda ko'rinadigan ustun) */
  toDate: string
  /** Yangi vaqt ("HH:mm", ixtiyoriy) */
  time?: string | null
}
export interface GroupJournal {
  group: GroupJournalInfo
  months: string[]
  month: string
  columns: JournalColumn[]
  students: GroupJournalStudent[]
  entries: JournalEntry[]
  /** "O'tildi" deb belgilangan dars sanalari — sababsiz o'quvchi shu kunda keldi (yashil). */
  conductedDates: string[]
  /** Shu oyga tegishli dars ko'chirishlari (bir martalik). */
  reschedules: LessonReschedule[]
}

/** Guruhning bitta oylik jurnali — ustunlar guruh dars kunlari bo'yicha avtomatik, qatorlar faqat faol o'quvchilar. */
export async function getGroupJournal(classId: string, month?: string): Promise<GroupJournal> {
  if (USE_MOCK) {
    await delay()
    return {
      group: { id: classId, name: '', courseId: '', courseName: '', teacherName: '', days: [], startTime: '', endTime: '', room: '', startDate: '', monthlyFee: 0 },
      months: [], month: month ?? '', columns: [], students: [], entries: [], conductedDates: [], reschedules: [],
    }
  }
  const { data } = await api.get<GroupJournal>('/admin/journal/group', { params: { classId, month } })
  return data
}

/** Bitta darsni bir martalik boshqa kunga ko'chirish (asl kun → yangi kun + ixtiyoriy vaqt). */
export async function rescheduleLesson(
  classId: string,
  fromDate: string,
  toDate: string,
  time?: string,
): Promise<LessonReschedule> {
  if (USE_MOCK) {
    await delay(120)
    return { id: 'mock', fromDate, toDate, time }
  }
  const { data } = await api.post<LessonReschedule>('/admin/journal/reschedule', {
    classId, fromDate, toDate, time: time || null,
  })
  return data
}

/** Dars ko'chirishni bekor qilish — dars asl kuniga qaytadi. */
export async function cancelReschedule(id: string): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    return
  }
  await api.delete(`/admin/journal/reschedule/${id}`)
}

export async function getJournalColumns(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalColumn[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<JournalColumn[]>('/admin/journal/columns', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function getJournalEntries(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalEntry[]> {
  if (USE_MOCK) {
    await delay()
    return journalMock[gkey(classId, subjectId, quarter)] ?? []
  }
  const { data } = await api.get<JournalEntry[]>('/admin/journal', {
    params: { classId, subjectId, quarter },
  })
  return data
}

interface EntryPayload {
  /** null — bahoni o'chiradi; berilmasa o'zgartirmaydi (API'da ikkalasi ham yuboriladi) */
  grade?: number | null
  /** null — davomat sababini o'chiradi */
  reasonId?: string | null
  /** Uyga vazifa: 0 = belgilanmagan, 1 = qildi, 2 = qilmadi, 3 = chala qildi */
  homework?: number
  /** Xulq: 0 = belgilanmagan, 1 = yaxshi, 2 = yomon */
  behavior?: number
  /** O'zlashtirish darajasi (MasteryLevel); null = tozalash */
  mastery?: MasteryLevel | null
  /** ANIQ "keldi (bor)" belgisi — presentDefaultFrom cheklovidan qat'i nazar yashil ✓ */
  present?: boolean
}

/** Bitta katakni belgilash — baho yoki davomat sababi (sana + dars raqami bo'yicha) */
export async function setJournalEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
  payload: EntryPayload,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = (journalMock[k] ??= [])
    const entry: JournalEntry = {
      studentId, date, period,
      grade: payload.grade ?? undefined,
      reasonId: payload.reasonId ?? undefined,
      homework: payload.homework ?? 0,
      behavior: payload.behavior ?? 0,
      mastery: payload.mastery ?? null,
    }
    const i = arr.findIndex((e) => e.studentId === studentId && e.date === date && e.period === period)
    if (i >= 0) arr[i] = entry
    else arr.push(entry)
    return
  }
  await api.put('/admin/journal', { classId, subjectId, quarter, studentId, date, period, ...payload })
}

/** Bitta dars (sana) uchun BARCHA o'quvchiga birdan davomat. absent=false → hammasi keldi; true → hammasi kelmadi
 * (reasonId berilsa shu sabab, aks holda standart "Sababsiz"). */
export async function bulkAttendance(
  classId: string,
  subjectId: string,
  date: string,
  period: number,
  studentIds: string[],
  opts: { absent: boolean; reasonId?: string | null },
): Promise<void> {
  if (USE_MOCK) {
    await delay(120)
    return
  }
  await api.post('/admin/journal/bulk-attendance', {
    classId, subjectId, date, period, studentIds,
    absent: opts.absent, reasonId: opts.reasonId ?? null,
  })
}

export async function clearJournalEntry(
  classId: string,
  subjectId: string,
  quarter: number,
  studentId: string,
  date: string,
  period: number,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = journalMock[k]
    if (arr)
      journalMock[k] = arr.filter(
        (e) => !(e.studentId === studentId && e.date === date && e.period === period),
      )
    return
  }
  await api.delete('/admin/journal', {
    params: { classId, subjectId, quarter, studentId, date, period },
  })
}

/* ---------- Jurnal boshqaruvi (tahrirlash siyosati) ---------- */

/** Jurnal tahrirlash siyosati — admin "Guruhlar → Jurnal boshqaruvi" oynasida belgilanadi. */
export interface JournalPolicy {
  /** free — istalgan o'tgan sanaga; today — faqat bugungi kun; window — oxirgi retroDays kun */
  editMode: 'free' | 'today' | 'window'
  /** window rejimida orqaga necha kungacha ruxsat (1-90) */
  retroDays: number
  /** true — baho/davomat faqat "o'tildi" deb belgilangan darsga (avval davomat, keyin baho) */
  conductedOnly: boolean
  /** true — cheklovlar admin jurnaliga ham qo'llanadi */
  applyToAdmins: boolean
  /** true — o'qituvchi maoshi jurnalga bog'lanadi: belgilanmagan dars = o'tilmagan, maoshdan ushlanadi */
  salaryRequireJournal: boolean
  /** Jurnalni to'ldirish muhlati (kun) — shu kun ichidagi darslar hali ushlanmaydi (0-30) */
  salaryGraceDays: number
  /** true — o'tgan oy(lar) uchun to'lamagan o'quvchi O'QITUVCHI jurnalida ko'rinmaydi
   *  (muzlatish EMAS: hisob davom etadi, to'lovdan keyin qator qaytadi; admin hammani ko'radi) */
  hideUnpaidPrevMonth: boolean
  /** true — joriy oy uchun `unpaidCutoffDay` kunidan keyin to'lamaganlar o'qituvchi jurnalida ko'rinmaydi */
  hideUnpaidAfterDay: boolean
  /** Joriy oy uchun to'lov muddati — oyning kuni (1-28; 28 dan katta kun har oyda mavjud emas) */
  unpaidCutoffDay: number
}

const DEFAULT_POLICY: JournalPolicy = {
  editMode: 'free', retroDays: 3, conductedOnly: false, applyToAdmins: false,
  salaryRequireJournal: false, salaryGraceDays: 0,
  hideUnpaidPrevMonth: false, hideUnpaidAfterDay: false, unpaidCutoffDay: 10,
}

export async function getJournalPolicy(): Promise<JournalPolicy> {
  if (USE_MOCK) {
    await delay()
    return { ...DEFAULT_POLICY }
  }
  const { data } = await api.get<JournalPolicy>('/admin/journal/policy')
  return data
}

export async function saveJournalPolicy(p: JournalPolicy): Promise<JournalPolicy> {
  if (USE_MOCK) {
    await delay(100)
    return { ...p }
  }
  const { data } = await api.put<JournalPolicy>('/admin/journal/policy', p)
  return data
}

/* ---------- Mavzu va uyga vazifa ---------- */

export async function getLessonNotes(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<JournalTopic[]> {
  if (USE_MOCK) {
    await delay()
    return journalTopicsMock[gkey(classId, subjectId, quarter)] ?? []
  }
  const { data } = await api.get<JournalTopic[]>('/admin/journal/notes', {
    params: { classId, subjectId, quarter },
  })
  return data
}

export async function setLessonNote(
  classId: string,
  subjectId: string,
  quarter: number,
  date: string,
  period: number,
  topic: string,
  homework: string,
  conducted: boolean,
): Promise<void> {
  if (USE_MOCK) {
    await delay(100)
    const k = gkey(classId, subjectId, quarter)
    const arr = (journalTopicsMock[k] ??= [])
    const i = arr.findIndex((t) => t.date === date && t.period === period)
    if (topic.trim() === '' && homework.trim() === '' && !conducted) {
      if (i >= 0) arr.splice(i, 1)
    } else if (i >= 0) {
      arr[i] = { date, period, topic, homework, conducted }
    } else {
      arr.push({ date, period, topic, homework, conducted })
    }
    return
  }
  await api.put('/admin/journal/notes', {
    classId,
    subjectId,
    quarter,
    date,
    period,
    topic,
    homework,
    conducted,
  })
}

/* ---------- Mavzularni Excel'dan ommaviy yuklash ---------- */

export interface TopicImportRowError {
  row: number
  reason: string
}
export interface TopicImportResult {
  imported: number
  skipped: number
  errors: number
  rowErrors: TopicImportRowError[]
}

/** Tanlangan guruh+fan+chorak uchun mavzular shabloni (.xlsx) — jadval kunlari oldindan to'ldirilgan. */
export async function downloadTopicsTemplate(
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<void> {
  if (USE_MOCK) {
    alert('Shablon faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get('/admin/journal/topics-template', {
    params: { classId, subjectId, quarter },
    responseType: 'blob',
  })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'mavzular_shablon.xlsx'
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

/** To'ldirilgan Excel'dan mavzu+uy vazifani import qiladi (darsni "o'tilgan" qilmaydi). */
export async function importTopics(
  file: File,
  classId: string,
  subjectId: string,
  quarter: number,
): Promise<TopicImportResult> {
  const fd = new FormData()
  fd.append('file', file)
  fd.append('classId', classId)
  fd.append('subjectId', subjectId)
  fd.append('quarter', String(quarter))
  const { data } = await api.post<TopicImportResult>('/admin/journal/topics-import', fd, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}
