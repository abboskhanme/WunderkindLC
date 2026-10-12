import type { TeacherReportOverview } from '@/types'

/**
 * O'qituvchilar hisobotining yuqori kartochkalari (2026-10-12). Server har O'QUVCHINI bir marta sanaydi
 * (`totals`) — qatorlarni qo'shish ikki guruhdagi o'quvchini ikki marta sanardi va «Faol» ustuni hozirgi holat
 * emas, AKTIVLASHGANLAR oqimi edi («Faol o'quvchilar 730», haqiqatda 681 aktiv + 20 sinov).
 * Eski server `totals` bermasa — avvalgidek qatorlar yig'indisi (taxminiy).
 */
export function teacherReportTotals(data: Pick<TeacherReportOverview, 'rows' | 'totals'>) {
  const t = data.totals
  if (t) {
    return { came: t.came, active: t.activeNow, trial: t.trialNow, studying: t.activeNow + t.trialNow, left: t.left }
  }
  const sum = (f: (r: TeacherReportOverview['rows'][number]) => number) => data.rows.reduce((s, r) => s + f(r), 0)
  const active = sum((r) => r.remaining)
  return { came: sum((r) => r.came), active, trial: 0, studying: active, left: sum((r) => r.left) }
}
