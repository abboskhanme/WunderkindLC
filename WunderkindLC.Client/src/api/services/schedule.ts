import { api } from '../client'

/* ---------- DARS JADVALI ---------- */

/** Jadvaldagi bitta dars (guruh × hafta kuni). `day`: 0=Dushanba … 6=Yakshanba. */
export interface ScheduleLesson {
  groupId: string
  groupName: string
  teacherId: string
  teacherName: string
  roomId: string
  roomName: string
  courseName: string
  day: number
  dayLabel: string
  /** "HH:mm" */
  start: string
  /** "HH:mm" */
  end: string
  /** Dars uzunligi (daqiqa) — katak balandligi shundan chiziladi. */
  minutes: number
}

/** Jadval egasi — xona yoki o'qituvchi. */
export interface ScheduleOwner {
  id: string
  name: string
  groupCount: number
}

/** Tig'izlik katagi: shu kun va shu yarim soatda nechta dars davom etyapti. */
export interface SchedulePeak {
  day: number
  /** "HH:mm" — katak boshi. */
  bucket: string
  count: number
}

/** Bo'sh oraliqni to'ldirish tavsiyasi. */
export interface ScheduleSuggestion {
  /** "teacher" — xona teshigiga bo'sh o'qituvchi; "room" — o'qituvchi teshigiga bo'sh xona. */
  kind: 'teacher' | 'room'
  id: string
  name: string
  /** Nega aynan shu tavsiya etilgani. */
  note: string
  /** Haftalik yuki (daqiqa) — kimda bo'sh quvvat borligi ko'rinsin. */
  weeklyMinutes: number
}

/** Ikki dars orasida qolib ketgan bo'sh oraliq. */
export interface ScheduleGap {
  scope: 'room' | 'teacher'
  ownerId: string
  ownerName: string
  day: number
  dayLabel: string
  start: string
  end: string
  minutes: number
  /** Teshikdan OLDINGI dars (guruh nomi). */
  beforeGroup: string
  /** Teshikdan KEYINGI dars. */
  afterGroup: string
  /** Shu oraliqda markazda nechta dars ketyapti — tig'izlik o'lchovi. */
  peakScore: number
  suggestions: ScheduleSuggestion[]
}

export interface ScheduleBoard {
  lessons: ScheduleLesson[]
  rooms: ScheduleOwner[]
  teachers: ScheduleOwner[]
  peak: SchedulePeak[]
  /** Vaqti/kunlari to'ldirilmagan yoki buzuq guruhlar soni — jadvalga kirmaydi. */
  skippedGroups: number
}

/** Haftalik jadval: darslar, xonalar/o'qituvchilar va tig'izlik — bitta so'rovda. */
export async function getScheduleBoard(): Promise<ScheduleBoard> {
  const { data } = await api.get<ScheduleBoard>('/admin/schedule')
  return data
}

/**
 * Bo'sh oraliqlar va tavsiyalar.
 * `scope='room'` — xona bekor turibdi (asosiy savol); `'teacher'` — o'qituvchi kutyapti.
 */
export async function getScheduleGaps(
  scope: 'room' | 'teacher' = 'room',
  ownerId?: string,
  minMinutes?: number,
): Promise<ScheduleGap[]> {
  const { data } = await api.get<ScheduleGap[]>('/admin/schedule/gaps', {
    params: { scope, ownerId: ownerId || undefined, minMinutes },
  })
  return data
}

/* ---------- BOSH SAHIFA jadval to'ri ---------- */

/** Guruh holati to'r filtri uchun: `blocked` — vaqtincha bloklangan guruh. */
export type ScheduleGroupStatus = 'active' | 'full' | 'blocked'

/** To'rdagi bitta GURUH — barcha dars kunlari bilan (server: `ScheduleGridGroupDto`). */
export interface ScheduleGridGroup {
  groupId: string
  groupName: string
  courseId: string
  courseName: string
  teacherId: string
  teacherName: string
  /** Xona FK; bo'sh bo'lsa `roomName` — eski matnli nom (ustun kaliti: `roomKey`). */
  roomId: string
  roomName: string
  /** 0=Dushanba … 6=Yakshanba (server konvensiyasi). */
  days: number[]
  /** "HH:mm" — serverda `ScheduleRules.MakeSlot` dan o'tgan, ya'ni buzuq emas. */
  start: string
  end: string
  minutes: number
  /** O'rin band qilgan a'zolar (faol + sinov, muzlatilgan emas). */
  members: number
  /** Sig'im; 0 = cheksiz. */
  capacity: number
  status: ScheduleGroupStatus | string
  /** "yyyy-MM-dd" yoki bo'sh. */
  startDate: string
  endDate: string
  /** O'tilgan darslar (jurnalda "dars o'tildi"). */
  lessonsDone: number
  /** Rejadagi jami darslar; 0 = noma'lum (boshlanish/tugash sanasi kiritilmagan). */
  lessonsTotal: number
  /** Sanali jadvalda: «ko'chirilgan» / «o'rinbosar: …» (blok va tooltipda alohida qator). */
  badge?: string
}

export interface ScheduleGrid {
  groups: ScheduleGridGroup[]
  /** BARCHA faol xonalar (darssizlari ham) + darsi bor yopilgan/eski nomli xonalar. */
  rooms: ScheduleOwner[]
  /** BARCHA arxivlanmagan o'qituvchilar. */
  teachers: ScheduleOwner[]
  /** Vaqti/kunlari buzuq guruhlar soni — to'rga kirmaydi. */
  skippedGroups: number
}

/** Bosh sahifa jadval to'ri — bitta so'rovda butun hafta (kun/filtr klientda). */
export async function getScheduleGrid(): Promise<ScheduleGrid> {
  const { data } = await api.get<ScheduleGrid>('/admin/schedule/grid')
  return data
}

/** Eksport parametrlari. `day` — SERVER konvensiyasi (0=Dushanba … 6=Yakshanba). */
export interface ScheduleExportParams {
  day: number
  /** "yyyy-MM-dd" — SANALI kun (haqiqiy darslar: ko'chirilgan, o'rinbosar); berilsa `day` e'tiborsiz. */
  date?: string
  groupBy: 'room' | 'teacher'
  fromHour: string
  toHour: string
  teacherId?: string
  groupId?: string
  roomId?: string
  courseId?: string
  status?: string
}

/** Tanlangan kunning jadvalini Excel (.xlsx) qilib yuklab beradi ("Jadval" + "Ro'yxat" varaqlari). */
export async function exportScheduleDay(params: ScheduleExportParams): Promise<void> {
  const res = await api.get('/admin/schedule/export', {
    params: {
      ...params,
      teacherId: params.teacherId || undefined,
      groupId: params.groupId || undefined,
      roomId: params.roomId || undefined,
      courseId: params.courseId || undefined,
      status: params.status || undefined,
    },
    responseType: 'blob',
  })
  const cd = String(res.headers['content-disposition'] ?? '')
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const href = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = href
  a.download = match?.[1] ?? 'dars_jadvali.xlsx'
  a.click()
  URL.revokeObjectURL(href)
}

/* ---------- SANALI JADVAL («Kun» / «Hafta», CRM Phase 5) ---------- */

/** Xona ustuni. `id` — Room.Id yoki eski matnli xona uchun `name:...` (`roomKey`). `floor` null — qavat ko'rsatilmagan. */
export interface DatedRoom {
  id: string
  name: string
  floor: number | null
  capacity: number
}

export interface DatedTeacher {
  id: string
  name: string
}

/**
 * Sanadagi HAQIQIY dars (server `AttendanceWatch.LessonsOn`): ko'chirilgan dars YANGI sana/vaqtida
 * (`moved`), o'rinbosar nomi bilan, guruh umri hisobga olingan.
 */
export interface DatedLesson {
  /** "yyyy-MM-dd" */
  date: string
  groupId: string
  groupName: string
  courseId: string
  courseName: string
  /** Guruh egasi. */
  teacherId: string
  teacherName: string
  /** Shu kuni darsni o'tadigan o'rinbosar (bo'lmasa bo'sh). */
  substituteTeacherId: string
  substituteTeacherName: string
  roomId: string
  roomName: string
  floor: number | null
  start: string
  end: string
  moved: boolean
  members: number
  capacity: number
  /** Jurnalda «o'tildi» belgilangan. */
  attendanceTaken: boolean
  status: ScheduleGroupStatus | string
}

export interface DatedSchedule {
  from: string
  to: string
  rooms: DatedRoom[]
  teachers: DatedTeacher[]
  lessons: DatedLesson[]
  skippedGroups: number
}

/** `from`..`to` (ikkalasi ham kiradi, ko'pi bilan 7 kun). */
export async function getDatedSchedule(from: string, to: string): Promise<DatedSchedule> {
  const { data } = await api.get<DatedSchedule>('/admin/schedule/dated', { params: { from, to } })
  return data
}

/** Bazadagi mavjud to'qnashuv (ikki faol guruh bir xonada yoki bir o'qituvchida bir vaqtda). */
export interface ScheduleConflictPair {
  kind: 'room' | 'teacher'
  groupA: { id: string; name: string }
  groupB: { id: string; name: string }
  /** Umumiy hafta kunlari (0=Dushanba). */
  days: number[]
  /** Kesishgan vaqt «15:00–15:30». */
  slot: string
}

export async function getScheduleConflicts(): Promise<ScheduleConflictPair[]> {
  const { data } = await api.get<ScheduleConflictPair[]>('/admin/schedule/conflicts')
  return data
}

