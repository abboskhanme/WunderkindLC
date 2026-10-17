/**
 * Moliya hisobotlari (qarzdorlik, tariflar, hisob-fakturalar) uchun SOF yordamchilar — yorliq va rang.
 * Hisob-kitob SERVERDA (`DebtLedger`); bu yerda faqat ko'rsatish.
 */

/** Foiz — ortiqcha nollarsiz ("72.5%", "100%"). */
export const pctLabel = (n: number) => `${Number(n.toFixed(1))}%`

/** Oyning to'lov muddati (mahalliy sana) — server `DebtLedger.DueDate` bilan bir xil: kun oy uzunligiga qisiladi. */
export function dueDateOf(month: string, dueDay: number): Date | null {
  if (!/^\d{4}-\d{2}$/.test(month)) return null
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  const last = new Date(y, m, 0).getDate()
  return new Date(y, m - 1, Math.min(Math.max(dueDay, 1), last))
}

/** Muddatdan o'tgan kunlar (manfiy — muddat kelmagan). */
export function ageDaysOf(month: string, dueDay: number, today: Date): number {
  const due = dueDateOf(month, dueDay)
  if (!due) return 0
  const t = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  return Math.round((t.getTime() - due.getTime()) / 86_400_000)
}

/** Qarz yoshiga qarab matn rangi: muddati kelmagan → kulrang, 30 kungacha → amber, 60 gacha → orange, undan ko'p → rose. */
export function ageTone(ageDays: number): string {
  if (ageDays < 0) return 'text-slate-500'
  if (ageDays < 30) return 'text-amber-600'
  if (ageDays < 60) return 'text-orange-600'
  return 'text-rose-600'
}

/** Yosh guruhi kartasining rangi (fon + chegara). */
export function bucketTone(key: string): string {
  switch (key) {
    case 'notdue': return 'border-slate-200 bg-slate-50 text-slate-600'
    case '0-6': return 'border-amber-200 bg-amber-50/60 text-amber-700'
    case '7-29': return 'border-amber-300 bg-amber-50 text-amber-800'
    case '30-59': return 'border-orange-300 bg-orange-50 text-orange-800'
    default: return 'border-rose-300 bg-rose-50 text-rose-800'
  }
}

/** A'zolik holati yorlig'i (faol a'zolikda — bo'sh). */
export function memberStatusLabel(status: string): string {
  switch (status) {
    case 'frozen': return 'Muzlatilgan'
    case 'trial': return 'Sinov'
    case 'left': return 'Chiqgan'
    case 'completed': return 'Tugatgan'
    default: return ''
  }
}

/** Faktura holati yorlig'i va rangi. */
export function invoiceStatusLabel(status: string): string {
  return status === 'paid' ? "To'langan" : status === 'partial' ? 'Qisman' : "To'lanmagan"
}

export function invoiceStatusTone(status: string): 'green' | 'amber' | 'red' {
  return status === 'paid' ? 'green' : status === 'partial' ? 'amber' : 'red'
}

/** Telefon bo'yicha qidiruv uchun faqat raqamlar ("+998 90 123" → "99890123"). */
export const digitsOf = (s: string) => s.replace(/\D/g, '')

/** Matn yoki telefon qidiruvi: raqamli so'rov (≥ 4 raqam) telefonga ham qaraydi. */
export function matchesSearch(q: string, texts: string[], phone = ''): boolean {
  const needle = q.trim().toLowerCase()
  if (!needle) return true
  if (texts.some((t) => t.toLowerCase().includes(needle))) return true
  const d = digitsOf(needle)
  return d.length >= 4 && digitsOf(phone).includes(d)
}
