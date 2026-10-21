import { describe, expect, it } from 'vitest'
import { conflictLine, scheduleConflictOf } from '@/lib/scheduleConflicts'

describe("jadval to'qnashuvi javobi (409)", () => {
  const err = (status: number, data: unknown) => ({ response: { status, data } })

  it("faqat 409 + roomConflict to'qnashuv deb o'qiladi", () => {
    const info = scheduleConflictOf(err(409, {
      roomConflict: true, message: "Jadvalda to'qnashuv bor — saqlanmadi", canForce: true,
      conflicts: [{ groupId: 'g', groupName: 'A1', sharedDays: 'Du, Ch', existingSlot: '09:00–10:00', reason: 'teacher' }],
    }))
    expect(info?.canForce).toBe(true)
    expect(conflictLine(info!.conflicts[0])).toBe("O'qituvchi band: A1 (Du, Ch, 09:00–10:00)")
    expect(scheduleConflictOf(err(400, { roomConflict: true }))).toBeNull()
    expect(scheduleConflictOf(err(409, { message: 'boshqa' }))).toBeNull()
    expect(scheduleConflictOf(new Error('tarmoq'))).toBeNull()
    // Admin/xodim — canForce yo'q, standart matn.
    expect(scheduleConflictOf(err(409, { roomConflict: true }))).toEqual({
      message: "Jadvalda to'qnashuv bor — saqlanmadi", conflicts: [], canForce: false,
    })
  })
})
