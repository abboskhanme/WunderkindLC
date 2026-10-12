/**
 * SANALI JADVAL («Kun» / «Hafta», CRM Phase 5) — sof funksiyalar (`__tests__/scheduleDated.test.ts`).
 *
 * Server haqiqiy darslarni beradi (`GET /admin/schedule/dated`): ko'chirilgan dars YANGI sana/vaqtida,
 * o'rinbosar nomi bilan, guruh umri hisobga olingan. Bu yerda faqat FILTR, USTUN/QATOR va manzil holati.
 *
 * ⚠️ Kun ko'rinishi mavjud to'r komponentlarini (`ScheduleGridView` / `ScheduleListView`) qayta ishlatadi —
 * shuning uchun dars `toGridLesson` bilan to'r shakliga o'giriladi (o'sha chizish qoidalari, o'sha ranglar).
 */
import type { DatedLesson, DatedRoom, DatedTeacher, ScheduleGridGroup } from '@/api/services/schedule'
import { localIsoDate, shiftLocalDay } from '@/lib/localDate'
import { DEFAULT_FROM, DEFAULT_TO, naturalCompare, roomKey, toMinutes, type GridColumn, type GroupBy, type ViewMode } from '@/lib/scheduleGrid'

export type DatedView = 'day' | 'week'

/** Hafta kunlari — server tartibida (0=Dushanba). */
export const WEEKDAY_SHORT = ['Du', 'Se', 'Chor', 'Pa', 'Ju', 'Sha', 'Yak'] as const
export const WEEKDAY_FULL = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba'] as const

/** Qavat filtri: '' — hammasi, `none` — qavat ko'rsatilmagan xonalar, aks holda raqam. */
export const FLOOR_NONE = 'none'

/* ------------------------------ Sana ------------------------------ */

const ISO = /^(\d{4})-(\d{2})-(\d{2})$/

/** To'g'ri "yyyy-MM-dd" mi (haqiqiy sana — "2026-02-30" emas). */
export function isIsoDate(s: string | null | undefined): s is string {
  const m = ISO.exec(s ?? '')
  if (!m) return false
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  return d.getFullYear() === Number(m[1]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[3])
}

/** Server kuni (0=Dushanba … 6=Yakshanba) "yyyy-MM-dd" dan. */
export function serverDayOf(iso: string): number {
  return (new Date(`${iso}T00:00:00`).getDay() + 6) % 7
}

/** Sana yotgan haftaning DUSHANBASI. */
export function mondayOf(iso: string): string {
  return shiftLocalDay(iso, -serverDayOf(iso))
}

/** Dushanbadan Yakshanbagacha 7 sana. */
export function weekDates(iso: string): string[] {
  const mon = mondayOf(iso)
  return Array.from({ length: 7 }, (_, i) => shiftLocalDay(mon, i))
}

/** So'raladigan oraliq: kun — o'sha sana; hafta — Dushanba..Yakshanba. */
export function rangeOf(view: DatedView, iso: string): { from: string; to: string } {
  if (view === 'day') return { from: iso, to: iso }
  const days = weekDates(iso)
  return { from: days[0], to: days[6] }
}

/** ‹ / › bosilganda: kunda ±1 kun, haftada ±7 kun. */
export function stepDate(view: DatedView, iso: string, dir: -1 | 1): string {
  return shiftLocalDay(iso, view === 'week' ? 7 * dir : dir)
}

/** "dd.MM.yyyy" */
export function dotted(iso: string): string {
  return isIsoDate(iso) ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : iso
}

/** Sarlavha: «Dushanba, 12.10.2026» yoki «12.10 – 18.10.2026». */
export function rangeTitle(view: DatedView, iso: string): string {
  if (view === 'day') return `${WEEKDAY_FULL[serverDayOf(iso)]}, ${dotted(iso)}`
  const days = weekDates(iso)
  return `${dotted(days[0]).slice(0, 5)} – ${dotted(days[6])}`
}

/* ------------------------------ Manzil holati ------------------------------ */

/** `/admin/jadval` va bosh sahifa jadvali holati — manzilda (`?date=&view=&groupBy=`), `replace` bilan. */
export interface DatedParams {
  date: string
  view: DatedView
  groupBy: GroupBy
  /** Kun ko'rinishining chizilishi: to'r yoki ro'yxat (`?layout=list`). */
  layout: ViewMode
  teacher: string
  group: string
  room: string
  course: string
  floor: string
  status: string
}

const FILTER_KEYS = ['teacher', 'group', 'room', 'course', 'floor', 'status'] as const

/** Manzildan holat: noma'lum/buzuq qiymat JIM standartga tushadi (eskirgan xatcho'p sahifani buzmasin). */
export function parseDatedParams(sp: URLSearchParams, today: string): DatedParams {
  const date = sp.get('date')
  return {
    date: isIsoDate(date) ? date : today,
    view: sp.get('view') === 'week' ? 'week' : 'day',
    groupBy: sp.get('groupBy') === 'teacher' ? 'teacher' : 'room',
    layout: sp.get('layout') === 'list' ? 'list' : 'grid',
    teacher: sp.get('teacher') ?? '',
    group: sp.get('group') ?? '',
    room: sp.get('room') ?? '',
    course: sp.get('course') ?? '',
    floor: sp.get('floor') ?? '',
    status: sp.get('status') ?? '',
  }
}

/** Holat → manzil (bo'sh filtrlar va standart qiymatlar yozilmaydi). */
export function datedParamsToSearch(p: DatedParams, today: string): URLSearchParams {
  const sp = new URLSearchParams()
  if (p.date !== today) sp.set('date', p.date)
  if (p.view !== 'day') sp.set('view', p.view)
  if (p.groupBy !== 'room') sp.set('groupBy', p.groupBy)
  if (p.layout !== 'grid') sp.set('layout', p.layout)
  for (const k of FILTER_KEYS) if (p[k]) sp.set(k, p[k])
  return sp
}

export function todayIsoLocal(): string {
  return localIsoDate(new Date())
}

/* ------------------------------ Filtr ------------------------------ */

/** Shu kuni darsni O'TADIGAN o'qituvchi: o'rinbosar bo'lsa u, aks holda guruh egasi. */
export function teachingTeacher(l: DatedLesson): { id: string; name: string } {
  return l.substituteTeacherId
    ? { id: l.substituteTeacherId, name: l.substituteTeacherName }
    : { id: l.teacherId, name: l.teacherName }
}

/** Qavat filtri bo'yicha mosmi. */
export function floorMatches(floor: number | null, f: string): boolean {
  if (!f) return true
  if (f === FLOOR_NONE) return floor === null
  return floor !== null && String(floor) === f
}

/**
 * Filtrlar. ⚠️ O'qituvchi filtri EGASI ham, O'RINBOSARI ham bo'yicha: o'qituvchi tanlansa uning o'z
 * guruhlari (o'rinbosar o'tgan kunda ham — dars yo'qolmasin) va u o'rinbosar bo'lgan darslar ko'rinadi.
 */
export function filterDatedLessons(lessons: DatedLesson[], p: Pick<DatedParams, (typeof FILTER_KEYS)[number]>): DatedLesson[] {
  return lessons
    .filter((l) => !p.teacher || l.teacherId === p.teacher || l.substituteTeacherId === p.teacher)
    .filter((l) => !p.group || l.groupId === p.group)
    .filter((l) => !p.room || roomKey(l.roomId, l.roomName) === p.room)
    .filter((l) => !p.course || l.courseId === p.course)
    .filter((l) => floorMatches(l.floor, p.floor))
    .filter((l) => !p.status || l.status === p.status)
}

/** Qavat sarlavhasi: «2-qavat» yoki «Qavat ko'rsatilmagan» (server `RoomFloors.Label` bilan bir xil). */
export function floorLabel(floor: number | null): string {
  return floor === null ? "Qavat ko'rsatilmagan" : `${floor}-qavat`
}

/** Xonalar tartibi: qavat o'sish bo'yicha (qavatsizlar oxirida), qavat ichida — tabiiy nom tartibi. */
export function sortRooms<T extends { floor: number | null; name: string }>(rooms: T[]): T[] {
  return [...rooms].sort(
    (a, b) =>
      (a.floor === null ? 1 : 0) - (b.floor === null ? 1 : 0) ||
      (a.floor ?? 0) - (b.floor ?? 0) ||
      naturalCompare(a.name, b.name),
  )
}

/* ------------------------------ Kun ko'rinishi ------------------------------ */

/**
 * Dars → to'r shakli (`ScheduleGridView` uni o'zgarishsiz chizadi). O'qituvchi — shu kuni dars o'tadigan
 * odam; ko'chirilgan / o'rinbosar belgisi `badge` qatorida.
 */
export function toGridLesson(l: DatedLesson): ScheduleGridGroup {
  const t = teachingTeacher(l)
  const s = toMinutes(l.start) ?? 0
  const e = toMinutes(l.end) ?? s
  const badges = [l.moved ? "ko'chirilgan" : '', l.substituteTeacherId ? `o'rinbosar: ${l.substituteTeacherName}` : '']
    .filter(Boolean)
  return {
    groupId: l.groupId,
    groupName: l.groupName,
    courseId: l.courseId,
    courseName: l.courseName,
    teacherId: t.id,
    teacherName: t.name,
    roomId: l.roomId.startsWith('name:') ? '' : l.roomId,
    roomName: l.roomName,
    days: [serverDayOf(l.date)],
    start: l.start,
    end: l.end,
    minutes: e - s,
    members: l.members,
    capacity: l.capacity,
    status: l.status,
    startDate: '',
    endDate: '',
    lessonsDone: 0,
    lessonsTotal: 0,
    badge: badges.join(' · '),
  }
}

/**
 * Kun ustunlari. Xona rejimida — BARCHA xonalar (darssizlari ham), QAVAT bo'yicha guruhlangan (`group` —
 * sarlavha), qavat o'sish tartibida; o'qituvchi rejimida — barcha o'qituvchilar (dars o'tadigan odam bo'yicha).
 * Xona/qavat/o'qituvchi filtri tanlansa ustunlar ham toraytiriladi.
 */
export function datedColumns(
  groupBy: GroupBy,
  rooms: DatedRoom[],
  teachers: DatedTeacher[],
  lessons: DatedLesson[],
  p: Pick<DatedParams, 'room' | 'floor' | 'teacher'>,
): GridColumn[] {
  if (groupBy === 'teacher') {
    const cols: GridColumn[] = [...teachers]
      .sort((a, b) => naturalCompare(a.name, b.name))
      .map((t) => ({ key: t.id, name: t.name }))
    if (lessons.some((l) => !teachingTeacher(l).id)) cols.push({ key: '', name: "O'qituvchi belgilanmagan" })
    return p.teacher ? cols.filter((c) => c.key === p.teacher) : cols
  }
  let list = sortRooms(rooms).filter((r) => floorMatches(r.floor, p.floor))
  if (p.room) list = list.filter((r) => r.id === p.room)
  const cols: GridColumn[] = list.map((r) => ({ key: r.id, name: r.name, group: floorLabel(r.floor) }))
  if (!p.room && !p.floor && lessons.some((l) => !roomKey(l.roomId, l.roomName)))
    cols.push({ key: '', name: 'Xona belgilanmagan', group: floorLabel(null) })
  return cols
}

/** Kun to'ri chegaralari uchun standart vaqt oralig'i (bosh sahifa bilan bir xil). */
export const DAY_FROM_MIN = toMinutes(DEFAULT_FROM)!
export const DAY_TO_MIN = toMinutes(DEFAULT_TO)!

/* ------------------------------ Hafta ko'rinishi ------------------------------ */

export interface WeekRow {
  key: string
  name: string
  /** Qavat sarlavhasi (xona rejimida). */
  group?: string
  /** sana → shu qatordagi darslar (vaqt bo'yicha). */
  cells: Record<string, DatedLesson[]>
}

/**
 * Hafta jadvali: qatorlar — xonalar (qavat bo'yicha) yoki o'qituvchilar, ustunlar — 7 sana, katakda dars
 * chiplari vaqt bo'yicha. Darsi umuman yo'q qatorlar ham qoladi (kim/qaysi xona bo'shligi ko'rinsin).
 */
export function weekRows(
  groupBy: GroupBy,
  rooms: DatedRoom[],
  teachers: DatedTeacher[],
  lessons: DatedLesson[],
  dates: string[],
  p: Pick<DatedParams, 'room' | 'floor' | 'teacher'>,
): WeekRow[] {
  const cols = datedColumns(groupBy, rooms, teachers, lessons, p)
  const keyOf = (l: DatedLesson) => (groupBy === 'teacher' ? teachingTeacher(l).id : roomKey(l.roomId, l.roomName))
  const rows = cols.map<WeekRow>((c) => ({
    key: c.key,
    name: c.name,
    group: c.group,
    cells: Object.fromEntries(dates.map((d) => [d, [] as DatedLesson[]])),
  }))
  const byKey = new Map(rows.map((r) => [r.key, r]))
  for (const l of lessons) {
    const row = byKey.get(keyOf(l))
    if (row && row.cells[l.date]) row.cells[l.date].push(l)
  }
  for (const r of rows)
    for (const d of dates)
      r.cells[d].sort((a, b) => (toMinutes(a.start) ?? 0) - (toMinutes(b.start) ?? 0) || naturalCompare(a.groupName, b.groupName))
  return rows
}

/** Ketma-ket bir xil `group` li elementlar to'plami (qavat sarlavhasi bir marta chiziladi). */
export function groupRuns<T extends { group?: string }>(items: T[]): { group: string; items: T[] }[] {
  const out: { group: string; items: T[] }[] = []
  for (const it of items) {
    const g = it.group ?? ''
    const last = out[out.length - 1]
    if (last && last.group === g) last.items.push(it)
    else out.push({ group: g, items: [it] })
  }
  return out
}
