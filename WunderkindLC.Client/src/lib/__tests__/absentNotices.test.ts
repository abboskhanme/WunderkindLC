import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import type { NoticeAction, NoticeStatus } from '@/api/services/attendanceNotices'
import { NOTICE_CHIPS, NOTICE_STATUS_LABELS, canApplyNotice, hhmm, isOpenNotice } from '@/lib/absentNotices'

const STATUSES: NoticeStatus[] = [
  'pending', 'awaiting', 'held', 'approved', 'sending', 'done', 'failed', 'unknown', 'expired', 'cancelled',
]
const allowed = (a: NoticeAction) => STATUSES.filter((s) => canApplyNotice(s, a))

const serverRules = readFileSync(
  fileURLToPath(new URL('../../../../WunderkindLC.Application/Services/Attendance/AbsentNoticeRules.cs', import.meta.url)),
  'utf8',
)

describe('«Davomat xabarlari» — server qoidasi nusxasi', () => {
  it("o'tishlar jadvali serverdagi CanApply bilan bir xil", () => {
    expect(allowed('hold')).toEqual(['pending', 'awaiting', 'approved'])
    expect(allowed('approve')).toEqual(['pending', 'awaiting', 'held'])
    expect(allowed('cancel')).toEqual(['pending', 'awaiting', 'held', 'approved'])
    // Server manbasidagi jadval satrlari o'zgarsa — bu test ham yangilanishi kerak.
    expect(serverRules).toContain('ActionHold => status is Pending or Awaiting or Approved')
    expect(serverRules).toContain('ActionApprove => status is Pending or Awaiting or Held')
    expect(serverRules).toContain('ActionCancel => status is Pending or Awaiting or Held or Approved')
  })

  it('ochiq holatlar — menejer ta\'sir qila oladiganlar', () => {
    expect(STATUSES.filter(isOpenNotice)).toEqual(['pending', 'awaiting', 'held', 'approved'])
  })

  it('yorliqlar serverdagi Label bilan bir xil', () => {
    for (const s of STATUSES) expect(serverRules).toContain(`=> "${NOTICE_STATUS_LABELS[s]}"`)
  })

  it('chip kalitlari serverdagi CountKeys bilan bir xil', () => {
    const block = serverRules.slice(serverRules.indexOf('CountKeys ='), serverRules.indexOf('];', serverRules.indexOf('CountKeys =')))
    expect([...block.matchAll(/"(\w+)"/g)].map((m) => m[1])).toEqual(NOTICE_CHIPS.map((c) => c.key))
  })

  it('vaqt server satridan (TZ siljishisiz)', () => {
    expect(hhmm('2026-10-12T15:31:00')).toBe('15:31')
    expect(hhmm(null)).toBe('')
  })
})
