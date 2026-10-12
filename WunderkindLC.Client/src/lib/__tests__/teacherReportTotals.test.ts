import { describe, expect, it } from 'vitest'
import type { TeacherReportRow } from '@/types'
import { teacherReportTotals } from '../teacherReportTotals'

const row = (p: Partial<TeacherReportRow>) => ({ came: 0, active: 0, trial: 0, frozen: 0, left: 0, remaining: 0, ...p }) as TeacherReportRow

describe('teacherReportTotals — kartochkalar har o\'quvchini bir marta sanaydi', () => {
  it('server totals ishlatiladi: Faol = aktiv + sinov, qatorlar yig\'indisi EMAS', () => {
    const rows = [row({ came: 400, active: 390, remaining: 380 }), row({ came: 350, active: 340, remaining: 333 })]
    const t = teacherReportTotals({ rows, totals: { came: 720, activeNow: 681, trialNow: 20, left: 9 } })
    expect(t).toEqual({ came: 720, active: 681, trial: 20, studying: 701, left: 9 })
  })

  it('eski server (totals yo\'q) — qatorlar yig\'indisi, «Faol» = hozirgi qolganlar (aktivlashganlar oqimi emas)', () => {
    const rows = [row({ came: 10, active: 9, remaining: 8, left: 1 }), row({ came: 5, active: 5, remaining: 5 })]
    expect(teacherReportTotals({ rows })).toEqual({ came: 15, active: 13, trial: 0, studying: 13, left: 1 })
  })
})
