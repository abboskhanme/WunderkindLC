/** Xodim faolligi (Xodimlar ro'yxati, SPEC-backlog F4 S1) — sof yordamchilar. */

export interface StaffActivity {
  kind: 'staff' | 'teacher'
  id: string
  lastLoginAt: string | null
  /** Oxirgi kirish yoki sessiya yangilanishi (kechrog'i). */
  lastSeenAt: string | null
  auditActions: number
  tasksClosed: number
  leadsHandled: number
  attendanceLessons: number
}

/** Ro'yxat kaliti — o'qituvchi va xodim id'lari to'qnashmasin. */
export const activityKey = (kind: 'staff' | 'teacher', id: string) => `${kind}:${id}`

/** "2026-10-17T09:30:00" → necha kun oldin (markaz vaqti bo'yicha, `today` "yyyy-MM-dd"). */
export function daysAgo(iso: string, today: string): number {
  const a = Date.UTC(+iso.slice(0, 4), +iso.slice(5, 7) - 1, +iso.slice(8, 10))
  const b = Date.UTC(+today.slice(0, 4), +today.slice(5, 7) - 1, +today.slice(8, 10))
  return Math.round((b - a) / 86_400_000)
}

/** Qisqa yorliq: «bugun 09:30» · «kecha 18:05» · «5 kun oldin» · «hech qachon». */
export function lastSeenLabel(iso: string | null | undefined, today: string): string {
  if (!iso) return 'hech qachon'
  const d = daysAgo(iso, today)
  const time = iso.length >= 16 ? iso.slice(11, 16) : ''
  if (d <= 0) return `bugun ${time}`.trim()
  if (d === 1) return `kecha ${time}`.trim()
  return `${d} kun oldin`
}

/** 7 kundan ko'p kirmagan — sust (rangli ogohlantirish). */
export const isStale = (iso: string | null | undefined, today: string) => !iso || daysAgo(iso, today) > 7

/** Faollik qisqa matni — faqat nolga teng bo'lmaganlar: «12 amal · 3 topshiriq · 5 lid · 8 dars». */
export function activitySummary(a: Pick<StaffActivity, 'auditActions' | 'tasksClosed' | 'leadsHandled' | 'attendanceLessons'>): string {
  const parts = [
    a.auditActions ? `${a.auditActions} amal` : '',
    a.tasksClosed ? `${a.tasksClosed} topshiriq` : '',
    a.leadsHandled ? `${a.leadsHandled} lid` : '',
    a.attendanceLessons ? `${a.attendanceLessons} dars` : '',
  ].filter(Boolean)
  return parts.length ? parts.join(' · ') : '—'
}
