import { describe, expect, it } from 'vitest'
import { conflictLine, scheduleConflictOf } from '@/lib/scheduleConflicts'
import { canApproveTask, stateOf } from '@/pages/admin/tasks/model'
import type { WorkTask } from '@/api/services/workTasks'

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

describe('topshiriq tasdiqlash (klient darvozasi server bilan bir xil)', () => {
  const t = (over: Partial<WorkTask>): WorkTask => ({
    id: 't', boardId: 'b', columnId: 'c', title: 'X', description: '', assigneeId: 'u-a', assigneeName: '',
    assigneeAvatarUrl: null, createdById: 'u-c', createdByName: '', priority: 1, dueDate: null, dueTime: null,
    order: 0, tags: [], isDone: true, isOverdue: false, stepsTotal: 0, stepsDone: 0, commentCount: 0,
    createdAt: '', updatedAt: '', completedAt: null, repeat: 'none', isArchived: false,
    approvalRequired: true, approvedAt: null, ...over,
  })

  it('holat', () => {
    expect(stateOf(t({}))).toBe('awaiting_approval')
    expect(stateOf(t({ approvedAt: '2026-10-12T10:00:00' }))).toBe('approved')
    expect(stateOf(t({ approvalRequired: false }))).toBe('done')
    expect(stateOf(t({ isDone: false, isOverdue: true }))).toBe('overdue')
    expect(stateOf(t({ state: 'open' }))).toBe('open')   // server qiymati ustun
  })

  it("tugmalar faqat bergan xodim yoki admin uchun", () => {
    expect(canApproveTask(t({}), { id: 'u-c', role: 'staff' })).toEqual({ approve: true, ret: true })
    expect(canApproveTask(t({}), { id: 'x', role: 'admin' })).toEqual({ approve: true, ret: true })
    expect(canApproveTask(t({}), { id: 'u-a', role: 'staff' })).toEqual({ approve: false, ret: false })
    expect(canApproveTask(t({ approvedAt: 'x' }), { id: 'u-c', role: 'staff' })).toEqual({ approve: false, ret: true })
    expect(canApproveTask(t({ isDone: false }), { id: 'u-c', role: 'staff' })).toEqual({ approve: false, ret: false })
  })
})
