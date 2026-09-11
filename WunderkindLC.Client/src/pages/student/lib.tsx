import type { CSSProperties, ReactNode } from 'react'
import {
  Home, Calendar, ClipboardList, MessageCircle, User, BarChart3, Check, CheckCircle2,
  BookOpen, Wallet, Settings, Bell, ChevronRight, ChevronLeft, ChevronDown, LogOut,
  Eye, EyeOff, Send, Paperclip, Plus, Search, X, Clock, Sun, Moon, Camera, Video,
  FileText, Download, Pencil, Lock, Mail, Phone, Trophy, Flame, Info, AlertTriangle,
  Sparkles, RefreshCw, LayoutGrid, List, ArrowRight, Upload, MessageSquare, Image,
  Images, Trash2, GraduationCap, ShieldCheck, MapPin, LocateFixed, Archive, Volume2, type LucideIcon,
} from 'lucide-react'
import { gradeHex } from '@/lib/utils'

/* ============================================================
   O'quvchi portali — umumiy yordamchilar (student.html'dan).
   Ikonkalar: lucide SVG (font/ligature'ga bog'liq emas — WebView'da ishonchli).
   Ring, ranglar, formatlash.
   ============================================================ */

/** student.html ICONS kalitlari → lucide komponent. */
const ICONS: Record<string, LucideIcon> = {
  home: Home, calendar: Calendar, clipboard: ClipboardList, chat: MessageCircle,
  user: User, chart: BarChart3, check: Check, checkCircle: CheckCircle2, book: BookOpen,
  wallet: Wallet, settings: Settings, bell: Bell, chevR: ChevronRight, chevL: ChevronLeft,
  chevD: ChevronDown, logout: LogOut, eye: Eye, eyeOff: EyeOff, send: Send,
  paperclip: Paperclip, plus: Plus, search: Search, x: X, clock: Clock, sun: Sun,
  moon: Moon, camera: Camera, video: Video, file: FileText, download: Download,
  edit: Pencil, lock: Lock, mail: Mail, phone: Phone, award: Trophy, flame: Flame,
  info: Info, alert: AlertTriangle, sparkle: Sparkles, refresh: RefreshCw, grid: LayoutGrid,
  list: List, arrowR: ArrowRight, upload: Upload, feedback: MessageSquare, image: Image,
  gallery: Images, trash: Trash2, school: GraduationCap, shield: ShieldCheck, telegram: Send,
  pin: MapPin, locate: LocateFixed, archive: Archive, volume: Volume2,
}

export function Icon({
  name,
  size = 20,
  color,
  fill,
  className,
  style,
}: {
  name: string
  size?: number
  color?: string
  fill?: boolean
  className?: string
  style?: CSSProperties
}) {
  const Cmp = ICONS[name] ?? Home
  return (
    <Cmp size={size} color={color} strokeWidth={fill ? 2.6 : 2} className={className} style={style} />
  )
}

/** Progress ring (SVG). value/max -> foiz. */
export function Ring({
  value,
  max = 100,
  size = 72,
  stroke = 7,
  color = 'var(--accent)',
  children,
}: {
  value: number
  max?: number
  size?: number
  stroke?: number
  color?: string
  children?: ReactNode
}) {
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const pct = Math.max(0, Math.min(1, max > 0 ? value / max : 0))
  const off = c * (1 - pct)
  return (
    <div className="ring" style={{ width: size, height: size }}>
      <svg width={size} height={size}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface3)" strokeWidth={stroke} />
        <circle
          cx={size / 2}
          cy={size / 2}
          r={r}
          fill="none"
          stroke={color}
          strokeWidth={stroke}
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={off}
          style={{ transition: 'stroke-dashoffset .8s cubic-bezier(.22,1,.36,1)' }}
        />
      </svg>
      <div className="ring-c">{children}</div>
    </div>
  )
}

export function gradeColor(g: number): string {
  return gradeHex(Number(g) || 0)
}

const SUBJ_PALETTE = ['#2563EB', '#7C3AED', '#0D9488', '#DB2777', '#EA580C', '#B45309', '#16A34A', '#0891B2', '#4F46E5', '#65A30D']
export function subjectColor(key: string): string {
  key = String(key || '')
  if (!key) return '#64708A'
  let hash = 0
  for (const ch of key) hash = (hash * 31 + ch.charCodeAt(0)) & 0x7fffffff
  return SUBJ_PALETTE[hash % SUBJ_PALETTE.length]
}

export function initials(name: string): string {
  return (
    String(name || '')
      .trim()
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0])
      .join('')
      .toUpperCase() || '?'
  )
}
export const subjInitial = (n: string) => {
  n = String(n || '').trim()
  return n ? n[0].toUpperCase() : '?'
}

export function fmtMoney(n: number, withSign = false): string {
  n = Number(n) || 0
  const abs = Math.round(Math.abs(n))
  const s = String(abs)
  let out = ''
  for (let i = 0; i < s.length; i++) {
    if (i > 0 && (s.length - i) % 3 === 0) out += ' '
    out += s[i]
  }
  const sign = n < 0 ? '−' : withSign && n > 0 ? '+' : ''
  return sign + out
}

const MONTHS = ['Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun', 'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr']
const WEEKDAYS = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']

function parseDate(iso?: string | null): Date | null {
  if (!iso) return null
  const s = String(iso).trim()
  if (!s) return null
  const d = new Date(s.length <= 10 ? s + 'T00:00:00' : s)
  return isNaN(d.getTime()) ? null : d
}
export function fmtDate(iso?: string | null, weekday = false): string {
  const d = parseDate(iso)
  if (!d) return iso || ''
  const wd = (d.getDay() + 6) % 7
  let s = `${d.getDate()} ${MONTHS[d.getMonth()]}`
  if (weekday && wd < 7) s += `, ${WEEKDAYS[wd]}`
  return s
}
export function fmtMonth(ym?: string | null): string {
  if (!ym || ym.length < 7) return ym || ''
  return `${MONTHS[Number(ym.slice(5, 7)) - 1] ?? ym} ${ym.slice(0, 4)}`
}
export function fmtTime(iso?: string | null): string {
  const d = parseDate(iso)
  if (!d) return ''
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}
export { MONTHS, WEEKDAYS }
