import type { StudentGroupItem, StudentPayment, MonthLedger } from '@/api/services/studentPortal'
import { paymentMethodLabel } from '@/config/constants'
import { fmtMoney, fmtMonth } from '@/pages/student/lib'

/* ============================================================
   Student cabinet — pure presentation helpers (tested in __tests__/format.test.ts).
   No React here: components live in ui.tsx, data stays exactly as the API returns it.
   ============================================================ */

export const DAY_NAMES = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']
const MONTHS_GEN = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr']

/** "168 000 so'm" (spaces between thousands, never toLocaleString). */
export function som(n: number): string {
  return `${fmtMoney(n)} so'm`
}

/** Monday-based weekday index (0 = Du … 6 = Ya), as in `Group.Days`. */
export const weekdayIndex = (d: Date) => (d.getDay() + 6) % 7

/** "12-oktabr, Dushanba" */
export function todayLine(d: Date): string {
  return `${d.getDate()}-${MONTHS_GEN[d.getMonth()]}, ${DAY_NAMES[weekdayIndex(d)]}`
}

/** Greeting by local time — neutral for both the student and the parent. */
export function greeting(d: Date): string {
  const h = d.getHours()
  if (h < 5) return 'Xayrli tun'
  if (h < 11) return 'Xayrli tong'
  if (h < 18) return 'Xayrli kun'
  return 'Xayrli kech'
}

const minutes = (hhmm: string): number | null => {
  const m = /^(\d{1,2}):(\d{2})$/.exec((hhmm || '').trim())
  if (!m) return null
  const h = Number(m[1])
  const mi = Number(m[2])
  return h < 24 && mi < 60 ? h * 60 + mi : null
}

/** Groups the student is studying in now (active + trial) — frozen/finished never show in the timetable. */
export function liveGroups(groups: StudentGroupItem[]): StudentGroupItem[] {
  return groups.filter((g) => g.state === 'active' || g.state === 'trial')
}

export interface NextLesson {
  group: StudentGroupItem
  /** 0 = today, 1 = tomorrow … */
  dayOffset: number
  /** Monday-based weekday of the lesson. */
  day: number
  /** 'now' — the lesson is going on right now. */
  status: 'now' | 'today' | 'later'
}

/**
 * The next lesson from the weekly timetable (`days` + `startTime`/`endTime`) relative to `now`.
 * Today's lesson counts until it ends. Groups without a valid start time are skipped.
 */
export function nextLesson(groups: StudentGroupItem[], now: Date): NextLesson | null {
  const live = liveGroups(groups).filter((g) => minutes(g.startTime) !== null && (g.days || []).length > 0)
  if (!live.length) return null
  const today = weekdayIndex(now)
  const nowMin = now.getHours() * 60 + now.getMinutes()
  for (let offset = 0; offset < 8; offset++) {
    const day = (today + offset) % 7
    const todays = live
      .filter((g) => g.days.includes(day))
      .sort((a, b) => (minutes(a.startTime) ?? 0) - (minutes(b.startTime) ?? 0))
    for (const g of todays) {
      if (offset === 0) {
        const start = minutes(g.startTime) ?? 0
        const end = minutes(g.endTime) ?? start + 60
        if (nowMin >= end) continue
        return { group: g, dayOffset: 0, day, status: nowMin >= start ? 'now' : 'today' }
      }
      return { group: g, dayOffset: offset, day, status: 'later' }
    }
  }
  return null
}

/** "Bugun" · "Ertaga" · "Chorshanba" */
export function dayLabel(offset: number, day: number): string {
  if (offset === 0) return 'Bugun'
  if (offset === 1) return 'Ertaga'
  return DAY_NAMES[day]
}

export function timeRange(g: Pick<StudentGroupItem, 'startTime' | 'endTime'>): string {
  return g.endTime ? `${g.startTime}–${g.endTime}` : g.startTime
}

/** Course name is what a parent recognises; the group name often carries the teacher's name. */
export function courseTitle(g: Pick<StudentGroupItem, 'courseName' | 'name'>): string {
  return (g.courseName || '').trim() || g.name
}

/* ---------- payments ---------- */


/** The server's automatic note: "O'quvchi to'lovi (2026-09) [Group] — Name". Never shown raw. */
const AUTO_NOTE = /^O['ʻ’`]?quvchi to['ʻ’`]?lovi\s*\(/i

export function isAutoNote(note?: string | null): boolean {
  return !!note && AUTO_NOTE.test(note.trim())
}

export interface PaymentView {
  /** "O'qish to'lovi" */
  title: string
  /** "Sentabr 2026 · Ingliz tili | Foundation · Naqd" — which month and course, how it was paid. */
  sub: string
  /** A human comment typed by the cashier (if any) — the automatic note is dropped. */
  note: string
}

export function paymentView(p: StudentPayment): PaymentView {
  const month = p.month && /^\d{4}-\d{2}$/.test(p.month) ? `${fmtMonth(p.month)} uchun` : ''
  const method = p.method ? paymentMethodLabel(p.method) : ''
  const what = (p.courseName || p.groupName || '').trim()
  const sub = [month, what, method && method !== '—' ? method : ''].filter(Boolean).join(' · ')
  const comment = (p.comment || '').trim()
  const note = comment || (isAutoNote(p.note) ? '' : (p.note || '').trim())
  return { title: "O'qish to'lovi", sub, note }
}

export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'violet' | 'neutral'

/** Tone → CSS colours (foreground + soft background), theme-aware via tokens. */
export function toneColors(t: Tone): { fg: string; bg: string } {
  switch (t) {
    case 'ok': return { fg: 'var(--green)', bg: 'var(--greenSoft)' }
    case 'warn': return { fg: 'var(--amber)', bg: 'var(--amberSoft)' }
    case 'bad': return { fg: 'var(--red)', bg: 'var(--redSoft)' }
    case 'info': return { fg: 'var(--accent)', bg: 'var(--accentSoft)' }
    case 'violet': return { fg: 'var(--violet)', bg: 'var(--violetSoft)' }
    default: return { fg: 'var(--muted)', bg: 'var(--surface3)' }
  }
}

/** Month ledger status → chip text + tone. */
export function monthStatus(m: Pick<MonthLedger, 'status' | 'remaining'>): { label: string; tone: Tone } {
  if (m.status === 'paid') return { label: "To'langan", tone: 'ok' }
  if (m.status === 'partial') return { label: `${fmtMoney(m.remaining)} qoldi`, tone: 'warn' }
  return { label: "To'lanmagan", tone: 'bad' }
}

/** Balance → what the family should understand at a glance. */
export function balanceState(balance: number): { tone: Tone; label: string; amount: number } {
  if (balance < 0) return { tone: 'bad', label: "To'lanishi kerak", amount: -balance }
  if (balance > 0) return { tone: 'ok', label: "Oldindan to'langan", amount: balance }
  return { tone: 'ok', label: "Qarz yo'q", amount: 0 }
}

/** Test/score percent colour band (85+ · 60+ · 40+ · below). */
export function percentTone(p: number | null | undefined): Tone {
  if (p == null) return 'neutral'
  if (p >= 85) return 'ok'
  if (p >= 60) return 'info'
  if (p >= 40) return 'warn'
  return 'bad'
}
