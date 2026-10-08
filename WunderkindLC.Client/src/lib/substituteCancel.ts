/**
 * O'RINBOSARLIK TAYINLOVINI BEKOR QILISH — qaysi kunlar bekor bo'ladi, qaysilari o'z kuchida qoladi.
 *
 * ⚠️ Serverdagi `SubstituteTeacherService.CancelAssignmentAsync` qoidasining NUSXASI (faqat tugmani
 * ko'rsatish/yashirish va tasdiq matni uchun — qaror baribir serverda): yaratishdagi o'tmish oynasi
 * (`MaxBackdateDays` = 14) bekor qilishga ham qo'llanadi. Bugundan 14 kundan ESKI kunlar yopilgan oy
 * maoshiga kirgan (o'rinbosarga to'langan, asosiydan ushlangan) — ular o'zgartirilmaydi.
 * Server konstantasi o'zgarsa shu yerni ham o'zgartiring.
 */

/** Server bilan bir xil: `SubstituteTeacherService.MaxBackdateDays`. */
export const SUBSTITUTE_MAX_BACKDATE_DAYS = 14

/** "yyyy-MM-dd" dan `days` kun oldingi sana (mahalliy kalendar, UTC emas). */
export function isoMinusDays(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(y, (m || 1) - 1, (d || 1) - days)
  const p = (n: number) => String(n).padStart(2, '0')
  return `${dt.getFullYear()}-${p(dt.getMonth() + 1)}-${p(dt.getDate())}`
}

export interface CancelPlan {
  /** Bekor qilinadigan kunlar (oyna ichida va kelajakda). */
  removable: string[]
  /** O'z kuchida qoladigan (14 kundan eski) kunlar. */
  locked: string[]
}

/**
 * Tayinlovni bekor qilganda nima bo'ladi. `dates` bo'sh bo'lsa (eski, oraliqli yozuv) faqat
 * `date`..`endDate` chegaralari bo'yicha baholanadi.
 */
export function cancelPlan(
  a: { dates?: string[] | null; date: string; endDate?: string | null },
  today: string,
): CancelPlan {
  const cutoff = isoMinusDays(today, SUBSTITUTE_MAX_BACKDATE_DAYS)
  const dates =
    a.dates && a.dates.length > 0
      ? [...new Set(a.dates.map((d) => d.trim()).filter(Boolean))].sort()
      : [a.date, a.endDate || a.date].filter(Boolean).sort()
  return {
    removable: dates.filter((d) => d >= cutoff),
    locked: dates.filter((d) => d < cutoff),
  }
}

/** "Bekor qilish" tugmasi ko'rinadimi: bekor qilinmagan va kamida bitta kun hali o'zgartirilishi mumkin. */
export function canCancelAssignment(
  a: { isActive: boolean; dates?: string[] | null; date: string; endDate?: string | null },
  today: string,
): boolean {
  return a.isActive && cancelPlan(a, today).removable.length > 0
}
