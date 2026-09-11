/**
 * OY/KUN bilan ishlaydigan sof funksiyalar — oylik kalendar chizig'i (`MonthDayStrip`) va undan
 * foydalanadigan sahifalar uchun yagona manba.
 *
 * ⚠️ Komponent fayliga QO'SHILMAYDI: eslint `react-refresh/only-export-components` — komponent
 * va oddiy funksiyalar bir faylda aralashmasin (`lib/ai.ts` bilan bir xil konvensiya).
 */

/** Bugungi kun "yyyy-MM-dd" (brauzer mintaqasida — server ham markaz mintaqasida ishlaydi). */
export function todayIso(): string {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

/** Joriy oy "yyyy-MM". */
export const currentMonth = () => todayIso().slice(0, 7)

/** "yyyy-MM" ni `delta` oyga suradi. */
export function shiftMonth(month: string, delta: number): string {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  if (!y || !m) return month
  const d = new Date(y, m - 1 + delta, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/** Oydagi barcha kunlar ("yyyy-MM-dd"). */
export function daysOfMonth(month: string): string[] {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  if (!y || !m) return []
  const count = new Date(y, m, 0).getDate()
  return Array.from({ length: count }, (_, i) => `${month}-${String(i + 1).padStart(2, '0')}`)
}

/** Oyning birinchi va oxirgi kuni ("yyyy-MM" → "yyyy-MM-dd"). */
export function monthRange(month: string): { from: string; to: string } {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  const last = new Date(y, m, 0).getDate()
  return { from: `${month}-01`, to: `${month}-${String(last).padStart(2, '0')}` }
}
