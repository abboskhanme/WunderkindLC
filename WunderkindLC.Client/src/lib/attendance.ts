/**
 * YAGONA davomat ta'rifi — serverdagi `AttendanceRules` (Application/Services/Attendance) ning AYNAN nusxasi
 * (SPEC P3.1, `.claude/rules/journal.md`): kech = KELGAN; baho/o'zlashtirish/aniq «keldi» = keldi;
 * davomat TO'LIQ olingan + belgisiz = keldi (faqat `presentDefaultFrom` dan); faqat "o'tildi" + belgisiz =
 * BELGILANMAGAN (foizga kirmaydi). Server qoidasi o'zgarsa — shu faylni ham o'zgartiring
 * (`lib/__tests__/attendance.test.ts`).
 */

/** Bitta (o'quvchi, dars) belgisi — serverdagi `AttendanceRules.Classify` ning AYNAN nusxasi. */
export type AttendanceMark = 'present' | 'late' | 'absent' | 'unmarked' | 'notHeld' | 'blocked'

export function attendanceMark(a: {
  blocked: boolean
  /** Sabab bor (kelmadi yoki kech). */
  hasReason: boolean
  reasonIsLate: boolean
  explicitPresent: boolean
  hasGrade: boolean
  hasMastery: boolean
  conducted: boolean
  attendanceTaken: boolean
  date: string
  presentDefaultFrom?: string
}): AttendanceMark {
  if (a.blocked) return 'blocked'
  if (a.hasReason) return a.reasonIsLate ? 'late' : 'absent'
  if (a.explicitPresent || a.hasGrade || a.hasMastery) return 'present'
  if (!a.conducted) return 'notHeld'
  if (a.attendanceTaken && (!a.presentDefaultFrom || a.date >= a.presentDefaultFrom)) return 'present'
  return 'unmarked'
}

/** Jamlanma: o'tilgan = keldi + kech + kelmadi; kech = KELGAN; belgilanmagan/o'tilmagan/bloklangan kirmaydi. */
export function attendanceTally(marks: AttendanceMark[]): {
  held: number
  attended: number
  absent: number
  late: number
  pct: number | null
} {
  let present = 0
  let late = 0
  let absent = 0
  for (const m of marks) {
    if (m === 'present') present++
    else if (m === 'late') late++
    else if (m === 'absent') absent++
  }
  const held = present + late + absent
  const attended = present + late
  return { held, attended, absent, late, pct: held > 0 ? Math.round((attended * 100) / held) : null }
}

/** Profil → «Qoldirilgan darslar» davri. */
export type AbsencePeriod = 'month' | '3m' | '6m' | '1y'

/** Davr → `{from, to}` ("yyyy-MM-dd", MAHALLIY sana). «1 yil» — 365/366 kun (server chegarasi 366). */
export function absencePeriodRange(p: AbsencePeriod, now: Date): { from: string; to: string } {
  const iso = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const to = iso(now)
  if (p === 'month') return { from: `${to.slice(0, 7)}-01`, to }
  const back = p === '3m' ? 3 : p === '6m' ? 6 : 12
  const f = new Date(now.getFullYear(), now.getMonth() - back, now.getDate() + 1)
  return { from: iso(f), to }
}
