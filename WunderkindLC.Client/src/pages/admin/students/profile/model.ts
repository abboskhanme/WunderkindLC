/**
 * O'QUVCHI PROFILI — sof mantiq (komponent YO'Q, `react-refresh/only-export-components` buzilmasin).
 *
 * Profil edutizimdagi "O'quvchi profili" ko'rinishida qayta terilgan: chapda profil kartasi,
 * o'ngda TAB "pill"lari. Tablar tartibi va ko'rinish darvozalari SHU yerda — sahifa ham,
 * test ham aynan shu katalogga qaraydi (`__tests__/model.test.ts`).
 */
import type { BadgeTone } from '@/components/ui/Badge'
import type { CallOption } from '@/components/CallPickerModal'
import type { StudentCall } from '@/api/services/students'
import type { ContactRequestItem } from '@/api/services/contacts'
import type { GroupMonth } from '@/types'
import { formatDate, formatTime } from '@/lib/utils'

/* ─────────── TABLAR KATALOGI ─────────── */

export type ProfileTab =
  // edutizimda ham bor — AYNAN edutizim tartibida
  | 'tahrirlash'
  | 'parol'
  | 'qongiroqlar'
  | 'guruh'
  | 'tranzaksiyalar'
  | 'harakatlar'
  | 'ltv'
  | 'sms'
  | 'shartnomalar'
  | 'manzil'
  // faqat BIZDA — edutizim tablaridan KEYIN, xuddi shu "pill" uslubida
  | 'chegirma'
  | 'dastur'
  | 'baholar'
  | 'testlar'
  | 'sertifikatlar'
  | 'fikr'
  | 'izohlar'
  | 'hujjatlar'
  | 'turniket'
  | 'bonus'
  | 'ai'

/** Tab ko'rinishini hal qiladigan ruxsatlar — sahifa `usePerm`/rol bo'yicha to'ldiradi. */
export interface TabAccess {
  /** `students.list:edit` — parol o'rnatish (tahrirlash tabi ko'rish uchun ham ochiq). */
  canEdit: boolean
  /** `audit` — Harakatlar tarixi (server `ReadRequiresPerm`). */
  canAudit: boolean
  /** `contracts` — Shartnomalar (server `ReadRequiresPerm`). */
  canContracts: boolean
  /** `app.locations` — Manzil (ilovadan kelgan GPS joylashuv). */
  canLocations: boolean
  /** `students.turnstile` — Turniket. */
  canTurnstile: boolean
  /** admin/superadmin ROLI — «Bonus» (o'qituvchi haqiga oid; `can()` bu yerda yaramaydi). */
  isAdmin: boolean
}

interface TabDef {
  key: ProfileTab
  label: string
  /** Berilmasa — hammaga ko'rinadi (sahifaning o'zi `students.list` ostida). */
  visible?: (a: TabAccess) => boolean
}

/** edutizim tartibi, keyin bizning qo'shimcha tablarimiz. Hech biri o'chirilmaydi — faqat darvozalanadi. */
export const PROFILE_TABS: readonly TabDef[] = [
  { key: 'tahrirlash', label: 'Tahrirlash' },
  { key: 'parol', label: "Parol o'rnatish", visible: (a) => a.canEdit },
  { key: 'qongiroqlar', label: "Qo'ng'iroqlar tarixi" },
  { key: 'guruh', label: 'Guruh' },
  { key: 'tranzaksiyalar', label: 'Tranzaksiyalar tarixi' },
  { key: 'harakatlar', label: 'Harakatlar tarixi', visible: (a) => a.canAudit },
  { key: 'ltv', label: 'LTV' },
  { key: 'sms', label: 'SMS' },
  { key: 'shartnomalar', label: 'Shartnomalar', visible: (a) => a.canContracts },
  { key: 'manzil', label: 'Manzil', visible: (a) => a.canLocations },
  { key: 'chegirma', label: 'Chegirma' },
  { key: 'dastur', label: "O'quv dasturi" },
  { key: 'baholar', label: 'Baholar va davomat' },
  { key: 'testlar', label: 'Testlar' },
  { key: 'sertifikatlar', label: 'Sertifikatlar' },
  { key: 'fikr', label: 'Fikr-mulohaza' },
  { key: 'izohlar', label: 'Izohlar' },
  { key: 'hujjatlar', label: 'Hujjatlar' },
  { key: 'turniket', label: 'Turniket', visible: (a) => a.canTurnstile },
  { key: 'bonus', label: 'Bonus', visible: (a) => a.isAdmin },
  { key: 'ai', label: 'AI Tahlil' },
]

/** Standart tab — edutizimda profil «Tahrirlash» bilan ochiladi. */
export const DEFAULT_PROFILE_TAB: ProfileTab = 'tahrirlash'

/** Shu foydalanuvchiga ko'rinadigan tablar — katalog TARTIBIDA. */
export function visibleTabs(access: TabAccess): TabDef[] {
  return PROFILE_TABS.filter((t) => !t.visible || t.visible(access))
}

/* ─────────── FORMATLAR ─────────── */

/** edutizim jadvallaridagi sana+vaqt: "dd.mm.yyyy | HH:mm" (vaqt bo'lmasa faqat sana). */
export function formatDateTimeBar(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = formatDate(iso)
  const time = formatTime(iso)
  return time ? `${date} | ${time}` : date
}

/** Sana + alohida vaqt satri ("HH:mm") — to'lovda karta vaqti alohida saqlanadi. */
export function joinDateTimeBar(date: string, time?: string | null): string {
  if (!date) return '—'
  const d = formatDate(date)
  if (time && /^\d{2}:\d{2}/.test(time)) return `${d} | ${time.slice(0, 5)}`
  const t = formatTime(date)
  return t ? `${d} | ${t}` : d
}

const UZ_MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]

/** "yyyy-MM" → "Sentabr 2026". */
export function monthLabel(m: string): string {
  return m && m.length >= 7 ? `${UZ_MONTHS[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(0, 4)}` : m
}

export const WEEKDAY_SHORT = ['Du', 'Se', 'Cho', 'Pa', 'Ju', 'Sha', 'Ya']

/** "yyyy-MM" dan "yyyy-MM" gacha (inklyuziv) uzluksiz oylar ro'yxati. */
export function monthRangeList(from: string, to: string): string[] {
  if (!from || !to || from > to) return from ? [from] : []
  const out: string[] = []
  let y = Number(from.slice(0, 4))
  let m = Number(from.slice(5, 7))
  const ty = Number(to.slice(0, 4))
  const tm = Number(to.slice(5, 7))
  while (y < ty || (y === ty && m <= tm)) {
    out.push(`${y}-${String(m).padStart(2, '0')}`)
    m++
    if (m > 12) {
      m = 1
      y++
    }
  }
  return out
}

export function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase() || '?'
}

/** Soniyani "m:ss" ko'rinishiga aylantiradi. */
export function fmtDur(sec: number): string {
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return `${m}:${String(s).padStart(2, '0')}`
}

/** Sekundni "3 daq 20 s" ko'rinishida. */
export function humanSec(sec: number): string {
  if (sec <= 0) return '—'
  if (sec < 60) return `${sec} s`
  const m = Math.floor(sec / 60)
  const s = sec % 60
  return s ? `${m} daq ${s} s` : `${m} daq`
}

/** Ball nisbatiga qarab rang (baholar jadvalidagi mantiq bilan bir xil). */
export function scoreTone(pct: number): BadgeTone {
  if (pct >= 85) return 'green'
  if (pct >= 50) return 'amber'
  return 'red'
}

/**
 * A'zolik holati belgisi. `yearFreeze` — «Aktiv muzlatish» (yangi o'quv yili): holat baribir
 * "frozen", shuning uchun bu ALOHIDA status emas, o'sha belgining aniqroq yozuvi
 * (`.claude/rules/year-freeze.md` §5).
 */
export function groupStatusBadge(status: string, yearFreeze?: boolean): { label: string; tone: BadgeTone } {
  switch (status) {
    case 'active':
      return { label: 'Aktiv', tone: 'green' }
    case 'frozen':
      return yearFreeze ? { label: 'Aktiv muzlatilgan', tone: 'indigo' } : { label: 'Muzlatilgan', tone: 'blue' }
    case 'completed':
      return { label: 'Tugatilgan', tone: 'teal' }
    case 'left':
      return { label: 'Chiqgan', tone: 'default' }
    default:
      return { label: 'Sinov', tone: 'amber' }
  }
}

/** A'zolikning ko'rinadigan holati: tugatgan → "completed", faol emas → "left", aks holda o'zi. */
export function membershipState(m: { status: string; isActive: boolean }): string {
  return m.status === 'completed' ? 'completed' : m.isActive ? m.status : 'left'
}

/** SMS holatini guruhlaydi (HistoryTab.tsx dagi bilan bir xil mantiq). */
export function smsStatusInfo(status: string): { label: string; tone: BadgeTone } {
  const s = (status || '').toUpperCase()
  if (s === 'DELIVRD' || s === 'DELIVERED' || s === 'YUBORILDI') return { label: 'Yetkazildi', tone: 'green' }
  if (s === 'WAITING' || s === 'NEW' || s === 'ACCEPTED' || s === 'STORED') return { label: 'Kutilmoqda', tone: 'amber' }
  return { label: 'Yetkazilmadi', tone: 'red' }
}

/** Bo'sh raqamlarni tashlab, bir xil raqamni faqat birinchi label bilan qoldiradi (CallPickerModal uchun). */
export function dedupeCallOptions(options: CallOption[]): CallOption[] {
  const seen = new Set<string>()
  return options.filter((o) => {
    if (!o.number || seen.has(o.number)) return false
    seen.add(o.number)
    return true
  })
}

/* ─────────── CHAP KARTA: "To'lash kerak" ─────────── */

/**
 * «To'lash kerak» — balansdan KELTIRIB chiqariladi: manfiy balans = qarz (`Student.Balance`
 * qoidasi: manfiy = qarzdor, musbat = avans). Alohida hisob-kitob YO'Q — raqam balans bilan
 * hech qachon zid bo'lib qolmasin.
 */
export function amountDue(balance: number): number {
  return balance < 0 ? -balance : 0
}

/* ─────────── QO'NG'IROQLAR TARIXI — aralash lenta ─────────── */

export interface ContactFeedEvent {
  id: string
  at: string
  resultLabel: string
  nextStatusLabel: string
  reasonLabel: string
  response: string
  actorName: string
  isNote: boolean
}

export interface CallFeedItem {
  key: string
  at: string
  call?: StudentCall
  note?: ContactFeedEvent
}

/**
 * "Qo'ng'iroqlar tarixi" ARALASH lentasi: haqiqiy qo'ng'iroqlar (Local Call) va bog'lanish
 * urinishlarida yozilgan javoblar BITTA vaqt o'qida, eng yangisi tepada
 * (`.claude/rules/contacts.md` §7.6).
 *
 * <p>Sabab: operator qo'ng'iroq qiladi, keyin navbatda "javobi nima dedi" ni yozadi — bular
 * bitta hodisaning ikki tomoni. Faqat MATN yozilgan urinishlar qo'shiladi — bo'sh javob
 * lentani suyultirardi.</p>
 */
export function buildCallFeed(calls: StudentCall[], contacts: ContactRequestItem[]): CallFeedItem[] {
  const events: ContactFeedEvent[] = contacts.flatMap((c) =>
    (c.history ?? [])
      .filter((h) => (h.type === 'contact' || h.type === 'note') && h.response)
      .map((h) => ({
        id: h.id,
        at: h.createdAt,
        resultLabel: h.resultLabel,
        nextStatusLabel: h.nextStatusLabel,
        reasonLabel: c.reasonLabel,
        response: h.response,
        actorName: h.actorName,
        isNote: h.type === 'note',
      })),
  )
  const items: CallFeedItem[] = [
    ...calls.map((c) => ({ key: `call-${c.id}`, at: c.startedAt, call: c })),
    ...events.map((e) => ({ key: `contact-${e.id}`, at: e.at, note: e })),
  ]
  // ISO satrlar — leksikografik solishtirish vaqt tartibini beradi (formatlar bir xil).
  return items.sort((a, b) => (a.at < b.at ? 1 : a.at > b.at ? -1 : 0))
}

/* ─────────── LTV — guruh bo'yicha jami ─────────── */

export interface LtvTotals {
  /** Umumiy to'lashi kerak bo'lgan summa (joriy oygacha, chegirma ayirilgan). */
  due: number
  /** Umumiy to'lagan summa (shu guruhga TEGLANGAN to'lovlar, avans oylari ham). */
  paid: number
  /** Qarz — har oy MUSTAQIL (keyingi oyga to'langan avans o'tgan oy qarzini yopmaydi). */
  debt: number
}

/**
 * Bitta guruhning LTV'si `StudentGroupLedger` oylaridan (to'lov oynasidagi AYNAN o'sha raqamlar).
 *
 * ⚠️ Ledger faol a'zolikda joriy oydan keyingi 3 ta AVANS oyini ham PREVIEW sifatida qaytaradi —
 * ular hali "to'lashi kerak" emas, shuning uchun `due`/`debt` ga faqat `month <= currentMonth`
 * kiradi. `paid` esa BARCHA oylardan: oldindan to'langan pul ham o'quvchi bergan pul.
 */
export function ltvTotals(months: GroupMonth[], currentMonth: string): LtvTotals {
  let due = 0
  let paid = 0
  let debt = 0
  for (const m of months) {
    paid += m.paid
    if (m.month.slice(0, 7) > currentMonth) continue
    due += m.fee
    debt += m.remaining
  }
  return { due, paid, debt }
}
