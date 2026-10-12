/**
 * JADVAL TO'QNASHUVI javobi (CRM Phase 5) — server 409 `{ roomConflict: true, message, conflicts, canForce }`
 * qaytaradi: guruh yaratish/tahrirlash/arxivdan chiqarish va dars ko'chirish. Faqat SUPERADMIN `canForce` oladi
 * va «Baribir saqlash» uchun SABAB yozadi. Sof funksiyalar (`__tests__/scheduleConflicts.test.ts`).
 */

/** Bitta to'qnashuv (server `RoomConflictDto`). */
export interface RoomConflictItem {
  groupId: string
  groupName: string
  /** "Du, Ch" */
  sharedDays: string
  /** "09:00–10:00" */
  existingSlot: string
  /** room | teacher */
  reason: string
}

export interface ScheduleConflictInfo {
  message: string
  conflicts: RoomConflictItem[]
  canForce: boolean
}

/** Xato jadval to'qnashuvimi (409 + `roomConflict`)? Bo'lmasa `null` — oddiy xato sifatida ko'rsatiladi. */
export function scheduleConflictOf(err: unknown): ScheduleConflictInfo | null {
  const res = (err as { response?: { status?: number; data?: unknown } } | null)?.response
  const data = res?.data as Partial<ScheduleConflictInfo> & { roomConflict?: boolean } | undefined
  if (res?.status !== 409 || !data?.roomConflict) return null
  return {
    message: typeof data.message === 'string' && data.message ? data.message : "Jadvalda to'qnashuv bor — saqlanmadi",
    conflicts: Array.isArray(data.conflicts) ? data.conflicts : [],
    canForce: data.canForce === true,
  }
}

/** «Xona band: Guruh A (Du, Ch, 09:00–10:00)». */
export function conflictLine(c: RoomConflictItem): string {
  const tag = c.reason === 'teacher' ? "O'qituvchi band" : 'Xona band'
  return `${tag}: ${c.groupName} (${c.sharedDays}, ${c.existingSlot})`
}
