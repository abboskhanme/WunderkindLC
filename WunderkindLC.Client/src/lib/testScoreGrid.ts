/**
 * TEST NATIJALARI JADVALI (Excel'dek kiritish) — sof funksiyalar (CRM U4a, docs/SPEC-crm-upgrade.md P4.2).
 *
 * Bitta katak = o'quvchi YECHGAN savollar soni (eski testda — ball). Foiz = yechgan ÷ savollar × 100.
 * Server noto'g'ri qiymatni RAD etadi (qismaydi) — klient ham xuddi shu matn bilan qizil qiladi.
 * Komponent bu yerda YO'Q (`react-refresh/only-export-components`) — `pages/admin/tests/TestScoreGrid.tsx`.
 */

export type TestKind = 'monthly' | 'interim'

/** Test turlari — «Oylik» / «Oraliq» (formada radio, ro'yxatda chip). */
export const TEST_KINDS: { kind: TestKind; label: string }[] = [
  { kind: 'monthly', label: 'Oylik' },
  { kind: 'interim', label: 'Oraliq' },
]

export const MIN_QUESTIONS = 1
export const MAX_QUESTIONS = 200

/** Avtosaqlash — oxirgi tahrirdan keyin shuncha ms (SPEC: 800 ms) yoki katakdan chiqilganda. */
export const AUTOSAVE_MS = 800

/** Katakning yuqori chegarasi: «Savollar soni», eski testda — maksimal ballning butun qismi. */
export function maxSolved(questionCount: number, maxScore: number): number {
  return questionCount > 0 ? questionCount : Math.max(0, Math.floor(maxScore))
}

/** Server bilan BIR XIL matn: «Natija 0 dan {n} gacha butun son bo'lsin». */
export function rangeMessage(max: number): string {
  return `Natija 0 dan ${max} gacha butun son bo'lsin`
}

/** Katak xatosi (null — to'g'ri). Bo'sh katak — to'g'ri (natijani tozalaydi). */
export function cellError(value: number | null, max: number): string | null {
  if (value === null) return null
  if (!Number.isInteger(value) || value < 0 || value > max) return rangeMessage(max)
  return null
}

/** Jonli foiz — bir kasr; bo'sh yoki noto'g'ri qiymatda null. */
export function livePercent(value: number | null, max: number, denominator: number): number | null {
  if (value === null || cellError(value, max) !== null || denominator <= 0) return null
  return Math.round((value / denominator) * 1000) / 10
}

/** Excel'dan qo'yilgan bitta qiymat: son, bo'sh (tozalash) yoki tanib bo'lmagan matn. */
export type PastedValue = number | null | { invalid: string }

/**
 * Excel ustunini (ko'p qatorli matn) qatorlarga ajratadi — har qatorning BIRINCHI ustuni olinadi:
 * `"25/30"` → 25, `"25"` → 25, bo'sh → null (tozalash), boshqasi (kasr ham) → `{ invalid }`.
 * Excel nusxasining oxiridagi bitta bo'sh qator tashlanadi.
 */
export function parsePastedColumn(text: string): PastedValue[] {
  const lines = text.replace(/\r\n?/g, '\n').split('\n')
  if (lines.length > 1 && lines[lines.length - 1].trim() === '') lines.pop()
  return lines.map((line) => {
    const cell = line.split('\t')[0].trim()
    if (cell === '') return null
    const m = /^(\d+)(?:\s*\/\s*\d+(?:[.,]\d+)?)?$/.exec(cell)
    return m ? Number(m[1]) : { invalid: cell }
  })
}

/** Qo'yilgan qiymatning xatosi (tanib bo'lmagani — «Raqam emas: …»). */
export function pastedError(v: PastedValue, max: number): string | null {
  if (v !== null && typeof v === 'object') return `Raqam emas: ${v.invalid}`
  return cellError(v, max)
}

/**
 * Klaviatura harakati: Enter/↓ — keyingi qator, Shift+Enter/↑ — oldingi qator. Qaytadi: yangi indeks
 * yoki null (bu tugma navigatsiya emas). Chekkada joyida qoladi.
 */
export function nextRowIndex(key: string, shift: boolean, index: number, count: number): number | null {
  let next: number
  if ((key === 'Enter' && !shift) || key === 'ArrowDown') next = index + 1
  else if ((key === 'Enter' && shift) || key === 'ArrowUp') next = index - 1
  else return null
  return Math.max(0, Math.min(count - 1, next))
}

/** "2026-10-12T15:40:12" → "12.10 15:40" («Yuborilgan: …» yorlig'i). */
export function formatSentAt(iso: string | null | undefined): string {
  if (!iso || iso.length < 16) return ''
  return `${iso.slice(8, 10)}.${iso.slice(5, 7)} ${iso.slice(11, 16)}`
}

/** «Ota-onalarga yuborish» natijasi — bir qatorli xulosa. */
export function sendSummary(r: { queued: number; noTelegram: number; skippedUnchanged: number }): string {
  const parts = [`${r.queued} ta xabar navbatga qo'yildi`]
  if (r.skippedUnchanged > 0) parts.push(`o'zgarmagan: ${r.skippedUnchanged}`)
  if (r.noTelegram > 0) parts.push(`Telegram'ga ulanmagan: ${r.noTelegram}`)
  return parts.join(' · ')
}
