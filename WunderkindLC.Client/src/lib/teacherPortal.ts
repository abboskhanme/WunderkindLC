/**
 * O'qituvchi portali (`/teacher/*`) — sof funksiyalar va NAVIGATSIYA katalogi.
 *
 * ⚠️ Komponentlar bu yerga QO'SHILMAYDI (`components/teacher/TeacherUi.tsx` da): eslint
 * `react-refresh/only-export-components` — komponent va oddiy funksiya bir faylda aralashmasin
 * (`lib/month.ts` bilan bir xil konvensiya).
 */
import {
  CalendarDays,
  ClipboardCheck,
  FileCheck2,
  GraduationCap,
  LayoutDashboard,
  LifeBuoy,
  MessageSquareText,
  Trophy,
  UserRound,
  Users,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import type { TeacherLesson } from '@/api/services/teacher'

/* =========================================================================================
 *  NAVIGATSIYA — yon menyu (desktop), pastki tablar (telefon) va "Menyu" sahifasi BITTA
 *  ro'yxatdan quriladi: bo'lim bir joyda qo'shilsa, uchalasida ham paydo bo'ladi.
 * ========================================================================================= */

export interface TeacherNavItem {
  to: string
  label: string
  /** Pastki tabdagi qisqa nom (joy tor) */
  short?: string
  /** Menyu sahifasidagi qisqa izoh — "bu bo'limda nima bor" */
  hint: string
  icon: LucideIcon
  /** O'qituvchi bo'lim ruxsati (`TeacherPermissions`); yo'q bo'lsa band ko'rinmaydi */
  perm?: 'journal' | 'schedule' | 'salary'
  /** Faqat Support o'qituvchiga (`TeacherProfile.isSupport`) */
  supportOnly?: boolean
  /** Faqat aynan shu manzilda faol (bosh sahifa) */
  end?: boolean
  /** Guruh: menyu sahifasida sarlavha ostida chiqadi */
  section: 'main' | 'work' | 'account'
}

export const TEACHER_NAV: TeacherNavItem[] = [
  { to: '/teacher', label: 'Bosh sahifa', short: 'Bosh', hint: 'Bugungi darslar va jamlanma', icon: LayoutDashboard, end: true, section: 'main' },
  { to: '/teacher/groups', label: 'Guruhlarim', short: 'Guruhlar', hint: 'Jadval, xona va o\'quvchilar soni', icon: Users, section: 'main' },
  { to: '/teacher/students', label: "O'quvchilarim", hint: 'Barcha guruhlar, oylik davomat', icon: GraduationCap, perm: 'journal', section: 'main' },
  { to: '/teacher/attendance', label: 'Davomat', hint: 'Dars bo\'yicha tezkor davomat', icon: ClipboardCheck, perm: 'journal', section: 'main' },
  { to: '/teacher/schedule', label: 'Dars jadvali', short: 'Jadval', hint: 'Haftalik jadval, ko\'chirilgan darslar', icon: CalendarDays, perm: 'schedule', section: 'main' },
  { to: '/teacher/salary', label: 'Maosh', hint: 'Oylik hisob, berilgan va qoldiq', icon: Wallet, perm: 'salary', section: 'work' },
  { to: '/teacher/tests', label: 'Testlar', hint: 'Test natijalari (ball kiritish)', icon: FileCheck2, perm: 'journal', section: 'work' },
  { to: '/teacher/rating', label: 'Reyting', hint: "O'quvchilar ball bo'yicha", icon: Trophy, section: 'work' },
  { to: '/teacher/support', label: 'Support', hint: "Bo'sh vaqt va bron darslari", icon: LifeBuoy, supportOnly: true, section: 'work' },
  { to: '/teacher/feedback', label: 'Taklif va shikoyat', hint: 'Adminga xabar yuborish', icon: MessageSquareText, section: 'account' },
  { to: '/teacher/profile', label: 'Profil', hint: 'Ma\'lumotlar, sozlamalar, parol', icon: UserRound, section: 'account' },
]

/** Telefondagi pastki tablar (qolganlari "Menyu" ichida). Ruxsati yo'q tab tushib qoladi. */
export const TEACHER_TABS = ['/teacher', '/teacher/groups', '/teacher/attendance', '/teacher/schedule']

export const TEACHER_SECTION_LABELS: Record<TeacherNavItem['section'], string> = {
  main: 'Asosiy',
  work: 'Ish',
  account: 'Hisob',
}

/** Band ko'rinadimi: ruxsat (`can`) va Support bayrog'i bo'yicha. */
export function navVisible(item: TeacherNavItem, can: (perm: string) => boolean, isSupport: boolean): boolean {
  if (item.supportOnly && !isSupport) return false
  return !item.perm || can(item.perm)
}

/** Manzil shu bandga tegishlimi (`/teacher/groups/123` → "Guruhlarim"). */
export function navMatches(item: TeacherNavItem, pathname: string): boolean {
  if (item.end) return pathname === item.to || pathname === item.to + '/'
  return pathname === item.to || pathname.startsWith(item.to + '/')
}

/* =========================================================================================
 *  SANA — hammasi "yyyy-MM-dd" satr ustida (brauzer mintaqasida siljimasin)
 * ========================================================================================= */

export const WEEKDAY_SHORT = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']
export const WEEKDAY_LONG = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']
export const MONTHS_GEN = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]
export const MONTHS_TITLE = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

function parts(iso: string): [number, number, number] {
  return [Number(iso.slice(0, 4)), Number(iso.slice(5, 7)), Number(iso.slice(8, 10))]
}

function toIso(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

export function addDays(iso: string, delta: number): string {
  const [y, m, d] = parts(iso)
  return toIso(new Date(y, m - 1, d + delta))
}

/** Hafta kuni: 0=Dushanba … 6=Yakshanba (guruh `days` bilan bir xil). */
export function weekdayIndex(iso: string): number {
  const [y, m, d] = parts(iso)
  return (new Date(y, m - 1, d).getDay() + 6) % 7
}

/** Shu sana tushgan haftaning dushanbasi. */
export function weekStartOf(iso: string): string {
  return addDays(iso, -weekdayIndex(iso))
}

/** "2026-09-27" → "27-sentabr" */
export function dayMonth(iso: string): string {
  const [, m, d] = parts(iso)
  return `${d}-${MONTHS_GEN[m - 1] ?? ''}`
}

/** "2026-09-27" → "27-sentabr, Yakshanba" */
export function dayMonthWeekday(iso: string): string {
  return `${dayMonth(iso)}, ${WEEKDAY_LONG[weekdayIndex(iso)]}`
}

/** Hafta oralig'i: "21–27 sentabr" yoki "29 sentabr – 5 oktabr". */
export function weekRangeLabel(from: string, to: string): string {
  const [, fm, fd] = parts(from)
  const [, tm, td] = parts(to)
  return fm === tm
    ? `${fd}–${td} ${MONTHS_GEN[tm - 1]}`
    : `${fd} ${MONTHS_GEN[fm - 1]} – ${td} ${MONTHS_GEN[tm - 1]}`
}

/** "2026-09" → "Sentabr 2026" */
export function monthTitle(month: string): string {
  const m = Number(month.slice(5, 7))
  return m >= 1 && m <= 12 ? `${MONTHS_TITLE[m - 1]} ${month.slice(0, 4)}` : month
}

/** Nisbiy kun: "Bugun" / "Ertaga" / "Kecha" / "29-sentabr". */
export function relativeDay(iso: string, today: string): string {
  if (!iso) return ''
  if (iso === today) return 'Bugun'
  if (iso === addDays(today, 1)) return 'Ertaga'
  if (iso === addDays(today, -1)) return 'Kecha'
  return dayMonth(iso)
}

/** Guruh kunlari: [0,2,4] → "Du, Cho, Ju" (+ toq/juft kunlar nomi). */
export function daysLabel(days: number[]): string {
  const sorted = [...days].sort((a, b) => a - b)
  if (sorted.join() === '0,2,4') return 'Toq kunlar (Du, Cho, Ju)'
  if (sorted.join() === '1,3,5') return 'Juft kunlar (Se, Pa, Sha)'
  if (sorted.length === 7) return 'Har kuni'
  return sorted.map((d) => WEEKDAY_SHORT[d] ?? d).join(', ')
}

export function timeRange(start: string, end: string): string {
  if (!start && !end) return ''
  return end ? `${start}–${end}` : start
}

/** Hozirgi vaqt "HH:mm" (brauzer soati). */
export function nowHm(): string {
  const d = new Date()
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

/* =========================================================================================
 *  DARS HOLATI — bosh sahifa, jadval va davomat sahifasida BIR XIL rang/matn
 * ========================================================================================= */

export type LessonPhase = 'done' | 'substituted' | 'now' | 'pending' | 'upcoming' | 'future'

/**
 * Darsning holati. Tartib muhim:
 * 1) davomat OLINGAN (`attendanceTaken` — yagona ta'rif; bitta baho bilan "o'tildi" bo'lgan dars EMAS) —
 *    boshqa hech narsa qiziq emas;
 * 2) boshqa odam o'tadi — o'qituvchidan davomat kutilmaydi;
 * 3) vaqt bo'yicha: hozir / tugagan-u belgilanmagan (kutilmoqda) / bugun keyinroq / kelajak.
 * O'tgan kundagi belgilanmagan dars ham "kutilmoqda" — u hali to'ldirilishi kerak.
 */
export function lessonPhase(l: TeacherLesson, today: string, now: string): LessonPhase {
  if (l.attendanceTaken) return 'done'
  if (l.substitutedBy) return 'substituted'
  if (l.date < today) return 'pending'
  if (l.date > today) return 'future'
  if (l.startTime && now < l.startTime) return 'upcoming'
  if (l.endTime && now >= l.endTime) return 'pending'
  return l.startTime ? 'now' : 'pending'
}

export const PHASE_STYLE: Record<LessonPhase, { label: string; cls: string; dot: string }> = {
  done: { label: 'Davomat olingan', cls: 'bg-emerald-50 text-emerald-700 border-emerald-200', dot: 'bg-emerald-500' },
  substituted: { label: "O'rinbosar o'tadi", cls: 'bg-slate-100 text-slate-600 border-slate-200', dot: 'bg-slate-400' },
  now: { label: 'Hozir', cls: 'bg-brand-50 text-brand-700 border-brand-200', dot: 'bg-brand-600' },
  pending: { label: 'Davomat kutilmoqda', cls: 'bg-amber-50 text-amber-700 border-amber-200', dot: 'bg-amber-500' },
  upcoming: { label: 'Keyinroq', cls: 'bg-slate-50 text-slate-600 border-slate-200', dot: 'bg-slate-300' },
  future: { label: 'Rejada', cls: 'bg-slate-50 text-slate-600 border-slate-200', dot: 'bg-slate-300' },
}

/** Davomat qilish mumkinmi (tugma ko'rsatiladimi): kelajak va o'rinbosar o'tadigan dars — yo'q. */
export function canTakeAttendance(l: TeacherLesson, today: string): boolean {
  return !l.substitutedBy && l.date <= today
}

/** Ism → bosh harflar ("Ali Valiyev" → "AV"). */
export function initials(name: string): string {
  const p = name.trim().split(/\s+/).filter(Boolean)
  if (p.length === 0) return '?'
  if (p.length === 1) return p[0].slice(0, 2).toUpperCase()
  return (p[0][0] + p[1][0]).toUpperCase()
}

/** Davomat foizi rangi (sahifalar bo'ylab bir xil chegaralar). */
export function pctTone(pct: number | null): { text: string; bar: string } {
  if (pct === null) return { text: 'text-faint', bar: 'bg-slate-200' }
  if (pct >= 85) return { text: 'text-emerald-600', bar: 'bg-emerald-500' }
  if (pct >= 60) return { text: 'text-amber-600', bar: 'bg-amber-500' }
  return { text: 'text-red-600', bar: 'bg-red-500' }
}

/* =========================================================================================
 *  TUNGI REJIM — tanlov brauzerda (faqat shu qurilma uchun qulaylik)
 * ========================================================================================= */

const THEME_KEY = 'teacher_theme'

/** Joriy o'qituvchi temasi. Xotira yopiq bo'lsa (maxfiy oyna) — yorug'. */
export function getTeacherTheme(): 'light' | 'dark' {
  try {
    return localStorage.getItem(THEME_KEY) === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

/** Temani saqlash + qobiqni xabardor qilish (`teacher-theme` hodisasi). */
export function setTeacherTheme(t: 'light' | 'dark') {
  try {
    localStorage.setItem(THEME_KEY, t)
  } catch {
    /* xotira yopiq — tanlov faqat shu sessiyada */
  }
  window.dispatchEvent(new Event('teacher-theme'))
}
