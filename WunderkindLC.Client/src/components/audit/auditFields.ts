import type { AuditAction } from '@/types'
import { formatMoney } from '@/lib/utils'

/**
 * AUDIT snapshotlarini o'qiladigan ko'rinishga keltirish — sof funksiyalar.
 *
 * Ikki ko'rinish shu yerdan chizadi: `AuditHistoryList` (ro'yxat, tafsilot ochiladi) va o'quvchi
 * profilidagi «Harakatlar tarixi» (edutizim uslubidagi kartalar, "eski → yangi"). Yorliqlar va
 * qiymat formatlari BITTA joyda — aks holda bir xil yozuv ikki sahifada ikki xil o'qilardi.
 */

export const auditActionConfig: Record<AuditAction, { label: string; cls: string }> = {
  create: { label: "Qo'shildi", cls: 'bg-emerald-50 text-emerald-700' },
  update: { label: 'Tahrirlandi', cls: 'bg-amber-50 text-amber-700' },
  delete: { label: "O'chirildi", cls: 'bg-red-50 text-red-700' },
}

/**
 * Yozuv yorlig'i. ⚠️ Moliya tranzaksiyasi 2026-09-26 dan O'CHIRILMAYDI, BEKOR QILINADI — lekin audit
 * amali (`create|update|delete` katalogi) `delete` bo'lib qoladi. Matni "Bekor qilindi" bilan
 * boshlangan yozuvda "O'chirildi" deyish yolg'on bo'lardi (qator bazada turibdi).
 */
export function auditActionLabel(action: AuditAction, summary?: string | null): { label: string; cls: string } {
  if (action === 'delete' && summary?.startsWith('Bekor qilindi'))
    return { label: 'Bekor qilindi', cls: 'bg-red-50 text-red-700' }
  return auditActionConfig[action] ?? { label: action, cls: 'bg-slate-100 text-slate-600' }
}

const moneyKeys = new Set(['amount', 'salary', 'monthlyFee', 'discountAmount'])
/** Ha/Yo'q ko'rinishida chiziladigan bayroqlar. */
const boolKeys = new Set(['isSupport'])

/**
 * Snapshot maydonlarining ekrandagi yorlig'i. Yorlig'i YO'Q maydon chizilmaydi (faqat o'zgarishni
 * ANIQLASH uchun ishlaydi) — `.claude/rules/audit.md` §3.5.
 */
export const auditFieldLabels: Record<string, string> = {
  amount: 'Summa',
  date: 'Sana',
  category: 'Toifa',
  direction: "Yo'nalish",
  note: 'Izoh',
  month: 'Oy',
  salary: 'Oylik',
  monthlyFee: "Oylik to'lov",
  name: 'Guruh',
  discountPct: 'Chegirma foizi',
  discountAmount: 'Chegirma summasi',
  discountNote: 'Chegirma izohi',
  fullName: 'F.I.SH',
  phone: 'Telefon',
  birthDate: "Tug'ilgan sana",
  gender: 'Jins',
  address: 'Manzil',
  parentFullName: "Ota-ona F.I.SH",
  parentPhone: "Ota-ona telefoni",
  fatherFullName: "Otasi F.I.SH",
  fatherPhone: "Otasi telefoni",
  motherFullName: "Onasi F.I.SH",
  motherPhone: "Onasi telefoni",
  className: 'Guruh',
  enrollmentDate: 'Qabul sanasi',
  grade: 'Daraja',
  language: 'Til',
  room: 'Xona',
  status: 'Holat',
  startTime: 'Boshlanish vaqti',
  endTime: 'Tugash vaqti',
  // O'qituvchi snapshot'i (AuditService.TeacherSnapshot) maydonlari.
  salaryMode: 'Maosh rejimi',
  salaryPercent: 'Maosh foizi',
  bonusPct: 'Ustama',
  salaryStartDate: 'Maosh boshlanish sanasi',
  salaryStartMonth: 'Maosh boshlanish oyi',
  homeroomClass: 'Biriktirilgan guruh',
  isSupport: "Support o'qituvchi",
}
/** Foiz qiymatlari uchun (raqamga "%" qo'shadi). */
const pctKeys = new Set(['discountPct', 'salaryPercent', 'bonusPct'])
const hiddenKeys = new Set(['studentId', 'teacherId'])

export function parseSnapshot(json?: string): Record<string, unknown> | null {
  if (!json) return null
  try {
    return JSON.parse(json) as Record<string, unknown>
  } catch {
    return null
  }
}

export function fmtAuditValue(key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—'
  if (moneyKeys.has(key) && typeof value === 'number') return formatMoney(value)
  if (pctKeys.has(key) && typeof value === 'number') return `${value}%`
  if (boolKeys.has(key)) return value ? 'Ha' : "Yo'q"
  if (key === 'salaryMode') return value === 'percent' ? 'Foizli' : "Qat'iy"
  if (key === 'direction') return value === 'income' ? 'Kirim' : 'Chiqim'
  if (key === 'gender') return value === 'female' ? 'Ayol' : 'Erkak'
  if (key === 'language') return value === 'ru' ? 'Rus' : "O'zbek"
  if (key === 'status') {
    const map: Record<string, string> = { active: 'Aktiv', frozen: 'Muzlatilgan', full: "To'lgan", archived: 'Arxiv', trial: 'Sinov' }
    return map[String(value)] ?? String(value)
  }
  return String(value)
}

export interface AuditFieldChange {
  key: string
  label: string
  /** Oldingi qiymat — faqat `update` da (ikkala snapshot ham bor). */
  before: string | null
  /** Yangi qiymat (create/update) yoki o'chirilgan qiymat (delete). */
  after: string
  /** Ikkala snapshot bor va qiymat haqiqatan o'zgargan. */
  changed: boolean
}

/**
 * before/after snapshotlaridan YORLIG'I BOR maydonlar ro'yxati. `update` da `before` to'ldiriladi
 * va `changed` o'zgarishni bildiradi; create/delete da faqat bitta qiymat (`after`).
 */
export function snapshotFields(before?: string, after?: string): AuditFieldChange[] {
  const b = parseSnapshot(before)
  const a = parseSnapshot(after)
  const keys = [...new Set([...Object.keys(b ?? {}), ...Object.keys(a ?? {})])].filter(
    (k) => !hiddenKeys.has(k) && k in auditFieldLabels,
  )
  return keys.map((k) => {
    const ov = b?.[k]
    const nv = a?.[k]
    if (b && a) {
      return {
        key: k,
        label: auditFieldLabels[k],
        before: fmtAuditValue(k, ov),
        after: fmtAuditValue(k, nv),
        changed: JSON.stringify(ov) !== JSON.stringify(nv),
      }
    }
    return { key: k, label: auditFieldLabels[k], before: null, after: fmtAuditValue(k, a ? nv : ov), changed: false }
  })
}
