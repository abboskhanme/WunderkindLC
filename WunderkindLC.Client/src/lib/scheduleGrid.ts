/**
 * BOSH SAHIFA jadval to'ri ("Dars jadvali") — sof funksiyalar (`__tests__/scheduleGrid.test.ts`).
 *
 * ⚠️ Serverdagi `ScheduleGridExport` (Excel eksporti) AYNAN shu qoidalar bilan ishlaydi:
 * ustun kaliti (`roomKey`), kunlar yorlig'i (`daysLabel`), vaqt oralig'i filtri va to'r
 * chegaralari (`gridRange`). Birida o'zgarsa ikkinchisini ham o'zgartiring — aks holda eksport
 * ekrandagidan boshqa narsani yozib berardi.
 *
 * ⚠️ IKKI XIL kun raqami bor:
 * - MANZIL (`?day=`) va kun tugmalari — JS `Date.getDay()`: 0=Yakshanba … 6=Shanba (edutizim
 *   tugmalari Yakshanbadan boshlanadi, indeks tugma tartibiga to'g'ri keladi);
 * - SERVER (`Group.Days`, eksport) — 0=Dushanba … 6=Yakshanba.
 * O'girish faqat `jsDayToServer` / `serverDayToJs` orqali.
 */
import type { ScheduleGridGroup, ScheduleOwner } from '@/api/services/schedule'

/** To'rning bitta qatori (daqiqa). */
export const SLOT_MINUTES = 30
/** Standart vaqt oralig'i (edutizim manzili: `fromHour=08:00&toHour=22:00`). */
export const DEFAULT_FROM = '08:00'
export const DEFAULT_TO = '22:00'

/** Kun tugmalari — Yakshanbadan (JS indeksi = massiv indeksi). */
export const DAY_TABS = ['Yak', 'Du', 'Se', 'Chor', 'Pa', 'Ju', 'Sha'] as const

/** Server tartibidagi qisqa nomlar (0=Dushanba). */
const SERVER_DAY_ABBR = ['Du', 'Se', 'Chor', 'Pa', 'Ju', 'Sha', 'Yak']

export type GroupBy = 'room' | 'teacher'
export type ViewMode = 'grid' | 'list'

export function jsDayToServer(jsDay: number): number {
  return (jsDay + 6) % 7
}

export function serverDayToJs(serverDay: number): number {
  return (serverDay + 1) % 7
}

/** "HH:mm" → daqiqa; buzuq qiymat — `null` (server bilan bir xil: 0..23 soat, 0..59 daqiqa). */
export function toMinutes(hhmm: string | null | undefined): number | null {
  const m = /^\s*(\d{1,2}):(\d{2})\s*$/.exec(hhmm ?? '')
  if (!m) return null
  const h = Number(m[1])
  const min = Number(m[2])
  if (h > 23 || min > 59) return null
  return h * 60 + min
}

/** Daqiqa → "HH:mm" (24:00 ham "24:00" bo'lib qoladi — to'r oxirgi chegarasi uchun). */
export function formatMinutes(total: number): string {
  const h = Math.floor(total / 60)
  const m = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`
}

/** Xona USTUNI kaliti: FK id, bo'lmasa eski matnli nom (`name:...`), umuman bo'lmasa `''`. */
export function roomKey(roomId: string, roomName: string): string {
  if (roomId) return roomId
  if (roomName) return `name:${roomName}`
  return ''
}

/**
 * Kunlar yorlig'i: {Du, Chor, Ju} → "Toq kunlar", {Se, Pa, Sha} → "Juft kunlar",
 * Dushanba–Shanba hammasi → "Har kuni", qolgani — qisqa nomlar ("Du, Sha, Yak").
 */
export function daysLabel(days: number[]): string {
  const set = [...new Set(days.filter((d) => d >= 0 && d <= 6))].sort((a, b) => a - b)
  if (set.length === 0) return ''
  const key = set.join(',')
  if (key === '0,2,4') return 'Toq kunlar'
  if (key === '1,3,5') return 'Juft kunlar'
  if ([0, 1, 2, 3, 4, 5].every((d) => set.includes(d))) return 'Har kuni'
  return set.map((d) => SERVER_DAY_ABBR[d]).join(', ')
}

/** To'r filtri — manzildan o'qilgan qiymatlar (bo'sh satr = filtr yo'q). */
export interface GridFilter {
  /** SERVER kuni (0=Dushanba). */
  day: number
  fromMin: number
  toMin: number
  teacherId: string
  groupId: string
  roomId: string
  courseId: string
  status: string
}

/** Tanlangan kundagi darslar (filtrlar bilan), boshlanishi bo'yicha. Oraliqqa TEGADIGAN dars kiradi. */
export function lessonsOfDay(groups: ScheduleGridGroup[], f: GridFilter): ScheduleGridGroup[] {
  return groups
    .filter((g) => g.days.includes(f.day))
    .filter((g) => !f.teacherId || g.teacherId === f.teacherId)
    .filter((g) => !f.groupId || g.groupId === f.groupId)
    .filter((g) => !f.roomId || roomKey(g.roomId, g.roomName) === f.roomId)
    .filter((g) => !f.courseId || g.courseId === f.courseId)
    .filter((g) => !f.status || g.status === f.status)
    .filter((g) => {
      const s = toMinutes(g.start)
      const e = toMinutes(g.end)
      return s !== null && e !== null && s < f.toMin && e > f.fromMin
    })
    .sort((a, b) => a.start.localeCompare(b.start) || a.groupName.localeCompare(b.groupName))
}

/**
 * To'r chegaralari: tanlangan oraliq, lekin undan chiqib ketgan dars bo'lsa u TO'LIQ sig'ishi
 * uchun kengaytiriladi (07:00 dagi dars 08:00 filtrida qirqilmaydi). Yarim soatga yaxlitlanadi.
 */
export function gridRange(lessons: ScheduleGridGroup[], f: GridFilter): { from: number; to: number } {
  let from = f.fromMin
  let to = f.toMin
  for (const l of lessons) {
    const s = toMinutes(l.start)
    const e = toMinutes(l.end)
    if (s !== null && s < from) from = s
    if (e !== null && e > to) to = e
  }
  return {
    from: Math.floor(from / SLOT_MINUTES) * SLOT_MINUTES,
    to: Math.ceil(to / SLOT_MINUTES) * SLOT_MINUTES,
  }
}

/** Yarim soatlik qatorlar: [{ start, label: "07:00 - 07:30" }, ...]. */
export function timeSlots(from: number, to: number): { start: number; label: string }[] {
  const out: { start: number; label: string }[] = []
  for (let t = from; t < to; t += SLOT_MINUTES)
    out.push({ start: t, label: `${formatMinutes(t)} - ${formatMinutes(t + SLOT_MINUTES)}` })
  return out
}

export interface GridColumn {
  key: string
  name: string
}

/** Tabiiy tartib: "1-bino 2-xona" < "1-bino 10-xona". */
export function naturalCompare(a: string, b: string): number {
  return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })
}

/**
 * Ustunlar: BARCHA xonalar/o'qituvchilar (darsi yo'qlari ham — kim bo'shligi ko'rinsin) va egasiz
 * darslar uchun bitta qo'shimcha ustun. Xona/o'qituvchi filtri tanlansa — faqat o'sha ustun.
 */
export function gridColumns(
  groupBy: GroupBy,
  rooms: ScheduleOwner[],
  teachers: ScheduleOwner[],
  lessons: ScheduleGridGroup[],
  f: GridFilter,
): GridColumn[] {
  if (groupBy === 'teacher') {
    const cols: GridColumn[] = teachers.map((t) => ({ key: t.id, name: t.name }))
    if (lessons.some((l) => !l.teacherId)) cols.push({ key: '', name: "O'qituvchi belgilanmagan" })
    return f.teacherId ? cols.filter((c) => c.key === f.teacherId) : cols
  }
  const cols: GridColumn[] = rooms.map((r) => ({ key: r.id, name: r.name }))
  if (lessons.some((l) => !roomKey(l.roomId, l.roomName))) cols.push({ key: '', name: 'Xona belgilanmagan' })
  return f.roomId ? cols.filter((c) => c.key === f.roomId) : cols
}

/** Dars qaysi ustunga tushadi. */
export function columnOf(l: ScheduleGridGroup, groupBy: GroupBy): string {
  return groupBy === 'teacher' ? l.teacherId : roomKey(l.roomId, l.roomName)
}

/**
 * Bitta ustundagi ustma-ust darslarni YO'LAKLARGA ajratadi (xona ikki marta band qilingan bo'lishi
 * mumkin — to'qnashuv saqlashda faqat OGOHLANTIRILADI). Har blok: `lane` (0..) va o'z to'plamidagi
 * yo'laklar soni `lanes` — blok kengligi shunga bo'linadi.
 */
export function assignLanes<T extends { start: string; end: string }>(
  items: T[],
): { item: T; lane: number; lanes: number }[] {
  const sorted = [...items].sort(
    (a, b) => (toMinutes(a.start) ?? 0) - (toMinutes(b.start) ?? 0) || (toMinutes(b.end) ?? 0) - (toMinutes(a.end) ?? 0),
  )
  const out: { item: T; lane: number; lanes: number }[] = []
  let cluster: { item: T; lane: number; lanes: number }[] = []
  let laneEnds: number[] = []
  let clusterEnd = -1

  const flush = () => {
    for (const c of cluster) c.lanes = laneEnds.length
    out.push(...cluster)
    cluster = []
    laneEnds = []
  }

  for (const item of sorted) {
    const s = toMinutes(item.start) ?? 0
    const e = toMinutes(item.end) ?? s
    if (cluster.length > 0 && s >= clusterEnd) flush()
    let lane = laneEnds.findIndex((end) => end <= s)
    if (lane === -1) {
      lane = laneEnds.length
      laneEnds.push(e)
    } else laneEnds[lane] = e
    cluster.push({ item, lane, lanes: 0 })
    clusterEnd = Math.max(cluster.length === 1 ? e : clusterEnd, e)
  }
  if (cluster.length > 0) flush()
  return out
}

/* ------------------------------ Ranglar ------------------------------ */

/** Dars bloklari palitrasi (edutizim: to'q sariq, yalpiz, qizil, ko'k, binafsha, yashil ...). */
export const LESSON_PALETTE = [
  '#F4A259',
  '#8CF2D2',
  '#C9474B',
  '#5B8DEF',
  '#B38CF5',
  '#5FD08B',
  '#F7C948',
  '#F28DB2',
  '#3FB5C8',
  '#7C83F2',
  '#E9804F',
  '#9AD35B',
] as const

/** Barqaror xesh (djb2) — bir kurs har doim bir xil rangda. */
function hash(s: string): number {
  let h = 5381
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) >>> 0
  return h
}

/** Blok rangi: KURS bo'yicha (kurssiz guruh — o'z id'si bo'yicha). */
export function lessonColor(l: Pick<ScheduleGridGroup, 'courseId' | 'groupId'>): string {
  return LESSON_PALETTE[hash(l.courseId || l.groupId) % LESSON_PALETTE.length]
}

function luminance(hex: string): number {
  const n = hex.replace('#', '')
  const ch = [0, 2, 4].map((i) => parseInt(n.slice(i, i + 2), 16) / 255)
  const [r, g, b] = ch.map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4))
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** Fonga qarab matn rangi — kontrasti yuqorirog'i (och fonda to'q, to'q fonda oq matn). */
export function textOn(hex: string): string {
  const l = luminance(hex)
  const white = 1.05 / (l + 0.05)
  const dark = (l + 0.05) / (luminance('#111827') + 0.05)
  return white >= dark ? '#FFFFFF' : '#111827'
}

/* ------------------------------ Blok maydonlari (sozlamalar) ------------------------------ */

/** Blok ichida chiqadigan maydon. `none` — qator ko'rsatilmaydi. */
export type LessonField =
  | 'time'
  | 'course'
  | 'teacher'
  | 'room'
  | 'days'
  | 'group'
  | 'lessons'
  | 'members'
  | 'none'

export const LESSON_FIELD_LABELS: Record<LessonField, string> = {
  time: 'Dars vaqti',
  course: 'Kurs nomi',
  teacher: "O'qituvchi",
  room: 'Xona',
  days: 'Dars kunlari',
  group: 'Guruh nomi',
  lessons: 'Darslar soni',
  members: "O'quvchilar soni",
  none: "Ko'rsatilmasin",
}

/** Drawer'dagi qatorlar (edutizim: 1–3-sarlavha, 1–3-qator, pastki qator). */
export const FIELD_SLOTS = [
  { key: 'title1', label: '1-sarlavha' },
  { key: 'title2', label: '2-sarlavha' },
  { key: 'title3', label: '3-sarlavha' },
  { key: 'row1', label: '1-qator' },
  { key: 'row2', label: '2-qator' },
  { key: 'row3', label: '3-qator' },
  { key: 'footer', label: 'Pastki qator' },
] as const

export type FieldSlot = (typeof FIELD_SLOTS)[number]['key']
export type FieldSettings = Record<FieldSlot, LessonField>

export const DEFAULT_FIELD_SETTINGS: FieldSettings = {
  title1: 'time',
  title2: 'course',
  title3: 'teacher',
  row1: 'room',
  row2: 'days',
  row3: 'group',
  footer: 'lessons',
}

export const FIELD_SETTINGS_KEY = 'home.schedule.fields'

/**
 * Saqlangan sozlamani o'qish: noma'lum kalit/qiymat JIM standartga tushadi (eski yoki qo'lda
 * buzilgan qiymat to'rni buzmasin). Xom JSON matnni oladi — localStorage o'qish chaqiruvchida.
 */
export function parseFieldSettings(raw: string | null): FieldSettings {
  if (!raw) return { ...DEFAULT_FIELD_SETTINGS }
  try {
    const obj = JSON.parse(raw) as Record<string, unknown>
    const out = { ...DEFAULT_FIELD_SETTINGS }
    for (const { key } of FIELD_SLOTS) {
      const v = obj?.[key]
      if (typeof v === 'string' && v in LESSON_FIELD_LABELS) out[key] = v as LessonField
    }
    return out
  } catch {
    return { ...DEFAULT_FIELD_SETTINGS }
  }
}

/**
 * Maydon qiymati (matn). `lessons` — "o'tilgan/jami"; jami noma'lum bo'lsa (guruhning
 * boshlanish/tugash sanasi kiritilmagan) O'QUVCHILAR soniga tushadi — `icon` shuni bildiradi.
 */
export function fieldValue(
  l: ScheduleGridGroup,
  field: LessonField,
): { text: string; icon?: 'book' | 'users' } | null {
  switch (field) {
    case 'time':
      return { text: `${l.start} - ${l.end}` }
    case 'course':
      return { text: l.courseName || l.groupName }
    case 'teacher':
      return l.teacherName ? { text: l.teacherName } : null
    case 'room':
      return { text: `Xona: ${l.roomName || '—'}` }
    case 'days':
      return { text: daysLabel(l.days) }
    case 'group':
      return { text: l.groupName }
    case 'lessons':
      return l.lessonsTotal > 0
        ? { text: `${l.lessonsDone}/${l.lessonsTotal}`, icon: 'book' }
        : membersValue(l)
    case 'members':
      return membersValue(l)
    default:
      return null
  }
}

function membersValue(l: ScheduleGridGroup): { text: string; icon: 'users' } {
  return { text: l.capacity > 0 ? `${l.members}/${l.capacity}` : String(l.members), icon: 'users' }
}

/** "yyyy-MM-dd" → "dd.MM.yyyy"; bo'sh/buzuq — bo'sh. */
export function formatIsoDate(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '')
  return m ? `${m[3]}.${m[2]}.${m[1]}` : ''
}

/** Tanlangan kunning JORIY haftadagi sanasi (hafta Dushanbadan): server kuni bo'yicha. */
export function dateOfServerDay(serverDay: number, today: Date): Date {
  const d = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  d.setDate(d.getDate() + (serverDay - jsDayToServer(today.getDay())))
  return d
}

/* ------------------------------ Manzil (URL) holati ------------------------------ */

/** Kunning to'liq nomi (JS indeksi) — edutizim manzilidagi `dayName` uchun. */
export const DAY_NAMES = ['Yakshanba', 'Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba'] as const

/** Bosh sahifa holati — edutizim kabi manzilda saqlanadi (`?fromHour=08:00&toHour=22:00&day=5&groupBy=room`). */
export interface HomeParams {
  /** JS kuni: 0=Yakshanba … 6=Shanba. */
  day: number
  groupBy: GroupBy
  view: ViewMode
  fromHour: string
  toHour: string
  teacher: string
  group: string
  room: string
  course: string
  status: string
}

/**
 * Manzildan holat: noma'lum/buzuq qiymat JIM standartga tushadi (eskirgan xatcho'p sahifani
 * buzmasin). `todayJs` — bugungi JS kuni (standart kun).
 */
export function parseHomeParams(sp: URLSearchParams, todayJs: number): HomeParams {
  const rawDay = sp.get('day')
  const day = rawDay !== null && /^[0-6]$/.test(rawDay) ? Number(rawDay) : todayJs
  let fromHour = toMinutes(sp.get('fromHour')) !== null ? sp.get('fromHour')!.trim() : DEFAULT_FROM
  let toHour = toMinutes(sp.get('toHour')) !== null ? sp.get('toHour')!.trim() : DEFAULT_TO
  if ((toMinutes(toHour) ?? 0) <= (toMinutes(fromHour) ?? 0)) {
    fromHour = DEFAULT_FROM
    toHour = DEFAULT_TO
  }
  return {
    day,
    groupBy: sp.get('groupBy') === 'teacher' ? 'teacher' : 'room',
    view: sp.get('view') === 'list' ? 'list' : 'grid',
    fromHour,
    toHour,
    teacher: sp.get('teacher') ?? '',
    group: sp.get('group') ?? '',
    room: sp.get('room') ?? '',
    course: sp.get('course') ?? '',
    status: sp.get('status') ?? '',
  }
}

/** Holat → manzil parametrlari (bo'sh filtrlar yozilmaydi; `dayName` — edutizim kabi, faqat o'qish uchun). */
export function homeParamsToSearch(p: HomeParams): URLSearchParams {
  const sp = new URLSearchParams()
  sp.set('fromHour', p.fromHour)
  sp.set('toHour', p.toHour)
  sp.set('day', String(p.day))
  sp.set('dayName', DAY_NAMES[p.day])
  sp.set('groupBy', p.groupBy)
  if (p.view !== 'grid') sp.set('view', p.view)
  for (const k of ['teacher', 'group', 'room', 'course', 'status'] as const) if (p[k]) sp.set(k, p[k])
  return sp
}

/** Holatdan to'r filtri (server kuni + daqiqalar). */
export function gridFilterOf(p: HomeParams): GridFilter {
  return {
    day: jsDayToServer(p.day),
    fromMin: toMinutes(p.fromHour) ?? toMinutes(DEFAULT_FROM)!,
    toMin: toMinutes(p.toHour) ?? toMinutes(DEFAULT_TO)!,
    teacherId: p.teacher,
    groupId: p.group,
    roomId: p.room,
    courseId: p.course,
    status: p.status,
  }
}

/** Guruh holati filtri ("Holati"). */
export const STATUS_OPTIONS = [
  { value: 'active', label: 'Faol' },
  { value: 'full', label: "To'lgan" },
  { value: 'blocked', label: 'Bloklangan' },
] as const
