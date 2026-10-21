/**
 * Day view of "Mening topshiriqlarim" — the LMS Mini App's main screen (`screens/hr/TasksScreen`,
 * `lib/hrFormat.js`), pure logic only. Dates are ISO `yyyy-MM-dd` in Tashkent; "today" comes from
 * the server (`GET /api/hr/me` → `today`), day arithmetic runs on `Date.UTC` so it never drifts.
 */
const MONTHS_GENITIVE = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

/** `Date.getUTCDay()` order (0 = Sunday). */
const WEEKDAYS_SHORT = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']
const WEEKDAYS_LONG = ['yakshanba', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba']

/** The strip shows a week; by default it reaches one day ahead ("Ertaga" one tap away). */
export const DAY_STRIP_VISIBLE = 7
export const DAY_STRIP_AHEAD = 1
/** Forward paging is capped; backwards is not. */
export const DAY_STRIP_MAX_WEEKS_AHEAD = 4

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

function toUtc(iso: string): Date {
  const match = ISO_DATE.exec(String(iso ?? '').slice(0, 10))
  if (!match) return new Date(Number.NaN)
  return new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])))
}

function fromUtc(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${String(date.getUTCFullYear()).padStart(4, '0')}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}`
}

export function addDays(iso: string, days: number): string {
  const date = toUtc(iso)
  date.setUTCDate(date.getUTCDate() + days)
  return fromUtc(date)
}

/** Whole days `a - b`. */
export function diffDays(a: string, b: string): number {
  return Math.round((toUtc(a).getTime() - toUtc(b).getTime()) / 86_400_000)
}

export function dayOfMonth(iso: string): number {
  return toUtc(iso).getUTCDate()
}

export function shortWeekday(iso: string): string {
  return WEEKDAYS_SHORT[toUtc(iso).getUTCDay()] ?? ''
}

/** `1-sentabr` */
export function dayMonthLabel(iso: string): string {
  const date = toUtc(iso)
  return `${date.getUTCDate()}-${MONTHS_GENITIVE[date.getUTCMonth()] ?? ''}`
}

/** «Bugun» · «Kecha» · «Ertaga», otherwise «1-sentabr, dushanba». */
export function dayHeadline(iso: string, today: string): string {
  const delta = diffDays(iso, today)
  if (delta === 0) return 'Bugun'
  if (delta === -1) return 'Kecha'
  if (delta === 1) return 'Ertaga'
  return `${dayMonthLabel(iso)}, ${WEEKDAYS_LONG[toUtc(iso).getUTCDay()] ?? ''}`
}

/** Which week the strip must show so `date` is visible. */
export function weekShiftFor(date: string, today: string): number {
  // `|| 0` — `Math.ceil(-1 / 7)` is `-0`.
  return Math.ceil((diffDays(date, today) - DAY_STRIP_AHEAD) / DAY_STRIP_VISIBLE) || 0
}

/** The seven days of the strip at `shift` weeks from the default position. */
export function stripDays(today: string, shift: number): string[] {
  const days: string[] = []
  for (let i = 0; i < DAY_STRIP_VISIBLE; i += 1) {
    days.push(addDays(today, i - (DAY_STRIP_VISIBLE - 1) + DAY_STRIP_AHEAD + shift * DAY_STRIP_VISIBLE))
  }
  return days
}

type DayTask = { status: string; isOverdue: boolean | null }

const isOpenStatus = (task: DayTask) => task.status === 'open' || task.status === 'returned'

/** Overdue open tasks first, the rest in the server's order (stable). */
export function sortDayTasks<T extends DayTask>(items: readonly T[]): T[] {
  return items
    .map((task, index) => ({ task, index }))
    .sort((a, b) => Number(b.task.isOverdue === true && isOpenStatus(b.task)) - Number(a.task.isOverdue === true && isOpenStatus(a.task)) || a.index - b.index)
    .map((x) => x.task)
}

/**
 * The line under the day headline. Cancelled tasks are not counted (they are not "unfinished");
 * for a future day no "done" count is shown — the employee would look behind.
 */
export function daySummary(items: readonly DayTask[], date: string, today: string): string {
  const counted = items.filter((task) => task.status !== 'cancelled')
  const done = counted.filter((task) => task.status === 'done').length
  const delta = diffDays(date, today)
  if (delta === 0) return `Bugun: ${counted.length} dan ${done} tasi bajarildi`
  if (delta > 0) return `${dayMonthLabel(date)}: ${counted.length} ta topshiriq rejalashtirilgan`
  return `${dayMonthLabel(date)}: ${counted.length} dan ${done} tasi bajarildi`
}

export function dayEmptyText(date: string, today: string): string {
  const delta = diffDays(date, today)
  if (delta === 0) return 'Bugunga topshiriq yo‘q. Xohlasangiz o‘zingizga qo‘shing.'
  return delta > 0 ? 'Bu kunga hozircha topshiriq yo‘q.' : 'Bu kunda topshiriq bo‘lmagan.'
}
