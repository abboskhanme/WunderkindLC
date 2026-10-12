/**
 * «Davomat xabarlari» (CRM U3b) — sof yordamchilar (`__tests__/absentNotices.test.ts`). ⚠️ Komponent YO'Q
 * (react-refresh qoidasi). Holat o'tishlari serverdagi `AbsentNoticeRules.CanApply` ning NUSXASI — faqat
 * tugmalarni yashirish uchun; qaror baribir serverda (ruxsatsiz o'tish 409 bilan qaytadi va ko'rsatiladi).
 */
import type { BadgeTone } from '@/components/ui/Badge'
import type { NoticeAction, NoticeCountKey, NoticeStatus } from '@/api/services/attendanceNotices'

export const NOTICE_STATUS_LABELS: Record<NoticeStatus, string> = {
  pending: 'Rejalashtirilgan',
  awaiting: 'Tasdiq kutmoqda',
  held: "To'xtatilgan",
  approved: 'Tasdiqlangan',
  sending: 'Yuborilmoqda',
  done: 'Yuborildi',
  failed: 'Xato',
  unknown: "Noma'lum",
  expired: "Muddati o'tdi",
  cancelled: 'Bekor qilindi',
}

export const NOTICE_STATUS_TONES: Record<NoticeStatus, BadgeTone> = {
  pending: 'blue',
  awaiting: 'amber',
  held: 'violet',
  approved: 'indigo',
  sending: 'blue',
  done: 'green',
  failed: 'red',
  unknown: 'red',
  expired: 'default',
  cancelled: 'default',
}

/** Holat chiplari — tartib va yorliq (sanoq kalitlari serverdagi bilan bir xil). */
export const NOTICE_CHIPS: { key: NoticeCountKey; label: string }[] = [
  { key: 'pending', label: 'Rejalashtirilgan' },
  { key: 'awaiting', label: 'Tasdiq kutmoqda' },
  { key: 'held', label: "To'xtatilgan" },
  { key: 'approved', label: 'Tasdiqlangan' },
  { key: 'sent', label: 'Yuborildi' },
  { key: 'failed', label: 'Xato' },
  { key: 'cancelled', label: 'Bekor qilindi' },
  { key: 'expired', label: "Muddati o'tdi" },
]

export const NOTICE_ACTION_LABELS: Record<NoticeAction, string> = {
  hold: "To'xtatish",
  approve: 'Tasdiqlash',
  cancel: 'Bekor qilish',
}

/** Server qoidasi nusxasi: hold ← pending/awaiting/approved; approve ← pending/awaiting/held; cancel ← ochiq. */
export function canApplyNotice(status: NoticeStatus, action: NoticeAction): boolean {
  switch (action) {
    case 'hold':
      return status === 'pending' || status === 'awaiting' || status === 'approved'
    case 'approve':
      return status === 'pending' || status === 'awaiting' || status === 'held'
    case 'cancel':
      return status === 'pending' || status === 'awaiting' || status === 'held' || status === 'approved'
    default:
      return false
  }
}

/** Menejer hali ta'sir qila oladigan (ochiq) xabar. */
export function isOpenNotice(status: NoticeStatus): boolean {
  return status === 'pending' || status === 'awaiting' || status === 'held' || status === 'approved'
}

/** ISO "yyyy-MM-ddTHH:mm[:ss]" → "HH:mm" (server markaz vaqtida beradi — Date ga aylantirilmaydi). */
export function hhmm(iso: string | null | undefined): string {
  return iso && iso.length >= 16 ? iso.slice(11, 16) : ''
}

/** Bekor qilish sababi (`cancelReason`) — qisqa izoh. */
export function cancelReasonLabel(reason: string | null): string {
  switch (reason) {
    case 'changed':
      return "jurnal o'zgardi"
    case 'archived':
      return 'o\'quvchi arxivlangan'
    case 'manager':
      return 'menejer'
    default:
      return ''
  }
}

/** Ochiq xabarlar bor — sahifa har 30 soniyada yangilanadi. */
export const NOTICE_POLL_MS = 30_000
