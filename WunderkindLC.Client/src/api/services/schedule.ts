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
