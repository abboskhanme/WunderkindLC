import { TrendingUp, Users, CheckCircle2, Flag } from 'lucide-react'
import type { GradingBoardCriterion } from '@/api/services/grading'
import { cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import type { RatingMonthData } from './shared'

// ============================ Reyting bo'limi ============================

export function RatingsTab({ data, loading }: { data: RatingMonthData[]; loading: boolean }) {
  if (loading) {
    return <Loader label="Reyting yuklanmoqda..." />
  }

  // Faqat baholash ma'lumoti bor va o'quvchisi bor oylar hisobga olinadi.
  const validEntries = data.filter((d) => d.grading && d.grading.students.length > 0)

  if (validEntries.length === 0) {
    return (
      <Card className="rounded-lg border border-slate-200 bg-white py-12 px-4 text-center text-slate-400">
        <TrendingUp className="mx-auto mb-3 h-8 w-8 opacity-40" />
        <p>Baholash mezonlari topilmadi yoki o'quvchi yo'q.</p>
      </Card>
    )
  }

  // O'quvchilar — tanlangan oylardagi grading.students UNIONi (fullName oxirgi uchragan qiymat).
  const studentNames = new Map<string, string>()
  for (const { grading } of validEntries) {
    for (const s of grading!.students) studentNames.set(s.studentId, s.fullName)
  }

  // Mezonlar — tanlangan oylar criteria UNIONi (order bo'yicha saralab).
  const criteriaById = new Map<string, GradingBoardCriterion>()
  for (const { grading } of validEntries) {
    for (const c of grading!.criteria) if (!criteriaById.has(c.id)) criteriaById.set(c.id, c)
  }
  const criteria = Array.from(criteriaById.values()).sort((a, b) => a.order - b.order)

  // Umumiy imkoniyat — barcha o'quvchiga bir xil, tanlangan oylar (dates × criteria) yig'indisi.
  let totalPossible = 0
  for (const { grading } of validEntries) {
    if (grading) totalPossible += grading.dates.length * grading.criteria.length
  }

  // Har o'quvchi uchun statistikani hisoblash — tanlangan OYLAR bo'yicha yig'indi (agregat).
  const studentStats = Array.from(studentNames.entries())
    .map(([studentId, fullName]) => {
      let journalTotal = 0
      let done = 0
      const criteriaDoneMap = new Map<string, number>()

      for (const { journal, grading } of validEntries) {
        if (journal) {
          const entryMap = new Map((journal.entries ?? []).map((e) => [`${e.studentId}|${e.date}`, e]))
          for (const col of journal.columns) {
            const e = entryMap.get(`${studentId}|${col.date}`)
            if (e?.grade) journalTotal += e.grade
          }
        }
        if (grading) {
          const gs = grading.students.find((s) => s.studentId === studentId)
          const doneKeys = gs?.doneKeys ?? []
          done += doneKeys.length
          for (const key of doneKeys) {
            const [critId] = key.split('|')
            criteriaDoneMap.set(critId, (criteriaDoneMap.get(critId) ?? 0) + 1)
          }
        }
      }

      const percentage = totalPossible > 0 ? Math.round((done / totalPossible) * 100) : 0
      const criteriaStats = criteria.map((crit) => ({
        criterion: crit,
        done: criteriaDoneMap.get(crit.id) ?? 0,
        total: totalCriterionDates(validEntries, crit.id),
      }))

      // Kombindan reyting = jurnal baho + baholash mezonlari yig'indisi
      const combinedRating = journalTotal + done

      return { studentId, fullName, done, totalPossible, percentage, criteriaStats, journalTotal, combinedRating }
    })
    // Kombindan reyting bo'yicha saralash (katta → kichik)
    .sort((a, b) => b.combinedRating - a.combinedRating)

  // O'rtacha bahoni hisoblash
  const avgPercentage =
    studentStats.length > 0
      ? Math.round(studentStats.reduce((s, st) => s + st.percentage, 0) / studentStats.length)
      : 0

  return (
    <div className="space-y-4">
      {/* KPI kartalar */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <RatingKpi label="O'rtacha" value={`${avgPercentage}%`} icon={TrendingUp} />
        <RatingKpi
          label="Jami o'quvchi"
          value={String(studentStats.length)}
          icon={Users}
        />
        <RatingKpi
          label="To'liq bajarildi"
          value={String(studentStats.filter((s) => s.percentage === 100).length)}
          icon={CheckCircle2}
        />
        <RatingKpi
          label="Bo'sh"
          value={String(studentStats.filter((s) => s.percentage === 0).length)}
          icon={Flag}
        />
      </div>

      {/* O'quvchilar jadvali */}
      <Card className="rounded-lg border border-slate-200 bg-white p-0">
        <div className="overflow-x-auto">
          <table className="min-w-full border-collapse">
            <thead>
              <tr className="bg-slate-100">
                <th className="border-b border-slate-200 px-4 py-3 text-left text-xs font-semibold uppercase text-slate-500">
                  O'quvchi
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Jurnal
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Bajarildi
                </th>
                <th className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500">
                  Jami
                </th>
                {criteria.slice(0, 3).map((crit) => (
                  <th
                    key={crit.id}
                    className="border-b border-slate-200 px-3 py-3 text-center text-xs font-semibold uppercase text-slate-500"
                    title={crit.name}
                  >
                    <span className="block max-w-[60px] truncate">{crit.name}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {studentStats.map((stat) => (
                <tr key={stat.studentId} className="border-b border-slate-200 hover:bg-slate-50">
                  <td className="px-4 py-3 text-sm font-medium text-slate-800">
                    {stat.fullName}
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-slate-800">
                    <span className="font-mono">{stat.journalTotal}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-semibold text-slate-800">
                    <span className="font-mono">{stat.done}</span>
                    <span className="text-slate-400">/{stat.totalPossible}</span>
                  </td>
                  <td className="px-3 py-3 text-center text-sm font-bold text-slate-800">
                    <span className={cn('inline-flex h-8 w-8 items-center justify-center rounded-md font-semibold', stat.combinedRating > 0 ? 'bg-violet-100 text-violet-700' : 'text-slate-400')}>
                      {stat.combinedRating || '—'}
                    </span>
                  </td>
                  {stat.criteriaStats.slice(0, 3).map((cs) => (
                    <td
                      key={cs.criterion.id}
                      className="px-3 py-3 text-center text-sm font-semibold text-slate-800"
                    >
                      <span className="font-mono text-brand-600">{cs.done}</span>
                      <span className="text-slate-400">/{cs.total}</span>
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>

      {/* Mezonlar bo'yicha detallar (agar ko'p bo'lsa) — tanlangan oylar bo'yicha agregat */}
      {criteria.length > 3 && (
        <Card className="rounded-lg border border-slate-200 bg-white p-4">
          <h3 className="mb-4 font-semibold text-slate-800">Mezonlar bo'yicha tahlil</h3>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {criteria.map((crit) => {
              let totalDone = 0
              let denom = 0
              for (const { grading } of validEntries) {
                if (!grading || !grading.criteria.some((c) => c.id === crit.id)) continue
                for (const student of grading.students) {
                  for (const key of student.doneKeys) {
                    const [critId] = key.split('|')
                    if (critId === crit.id) totalDone++
                  }
                }
                denom += grading.students.length * grading.dates.length
              }
              const pct = denom > 0 ? Math.round((totalDone / denom) * 100) : 0
              return (
                <div key={crit.id} className="rounded-lg bg-slate-50 p-3">
                  <p className="mb-2 text-sm font-semibold text-slate-800">{crit.name}</p>
                  <div className="flex items-center gap-2">
                    <div className="flex-1">
                      <div className="h-2 overflow-hidden rounded-full bg-slate-200">
                        <div
                          className="h-full rounded-full bg-brand-500 transition-all"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                    <span className="font-mono text-xs font-semibold text-slate-800 w-10 text-right">{pct}%</span>
                  </div>
                  <p className="mt-1 text-xs text-slate-400">
                    <span className="font-mono font-semibold">{totalDone}</span> /{" "}
                    {denom}
                  </p>
                </div>
              )
            })}
          </div>
        </Card>
      )}

    </div>
  )
}

/** Bitta mezon bo'yicha — tanlangan oylardagi jami dars sanalari (mezon mavjud bo'lgan oylar bo'yicha). */
function totalCriterionDates(entries: RatingMonthData[], criterionId: string): number {
  let total = 0
  for (const { grading } of entries) {
    if (grading && grading.criteria.some((c) => c.id === criterionId)) total += grading.dates.length
  }
  return total
}

function RatingKpi({
  label,
  value,
  icon: Icon,
}: {
  label: string
  value: string
  icon: typeof Users
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-3">
      <div className="mb-1 flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-400">
        <Icon className="h-3.5 w-3.5" />
        {label}
      </div>
      <div className="text-lg font-semibold text-slate-800">{value}</div>
    </div>
  )
}
