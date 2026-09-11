import { Fragment, useEffect, useState } from 'react'
import { ChevronRight, Info } from 'lucide-react'
import type { MonthStatus, SalaryLedger, SalaryReportRow } from '@/types'
import { getSalaryLedger } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { Badge } from '@/components/ui/Badge'
import type { BadgeTone } from '@/components/ui/Badge'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { usePerm } from '@/lib/permissions'
import { formatDate, formatMoney, cn } from '@/lib/utils'
import { formatMonth, monthStatusLabels } from '@/config/constants'

interface Props {
  teacher: SalaryReportRow | null
  from: string
  to: string
  onClose: () => void
}

const statusTones: Record<MonthStatus, BadgeTone> = {
  paid: 'green',
  partial: 'amber',
  unpaid: 'red',
}

export function TeacherSalaryDetailModal({ teacher, from, to, onClose }: Props) {
  // O'zgarishlar tarixi — alohida `audit` ruxsati (admin/superadmin uchun har doim true).
  const canSeeAudit = usePerm().can('audit', 'view')
  const [ledger, setLedger] = useState<SalaryLedger | null>(null)
  const [loading, setLoading] = useState(false)
  /** Ushlanma sababi ochilgan oy ("YYYY-MM") */
  const [expanded, setExpanded] = useState<string | null>(null)

  useEffect(() => {
    if (!teacher) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda hisobni yuklash (maqsadli)
    setLoading(true)
    setLedger(null)
    setExpanded(null)
    getSalaryLedger(teacher.teacherId, from, to)
      .then(setLedger)
      .finally(() => setLoading(false))
  }, [teacher, from, to])

  // Jurnalga bog'langan maosh — ushlanma ustunlari faqat shunda ko'rsatiladi.
  const journal = !!ledger?.journalLinked

  return (
    <Modal open={!!teacher} onClose={onClose} size="lg" title="O'qituvchi oyligi">
      {loading || !ledger ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          {/* Sarlavha */}
          <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 px-4 py-3">
            <div>
              <p className="font-semibold text-slate-800">{ledger.fullName}</p>
              <p className="text-sm text-slate-500">
                {ledger.salaryMode === 'percent'
                  ? `Foizli maosh: guruh to'lovining ${ledger.salaryPercent ?? 0}%i`
                  : `Belgilangan oylik: ${formatMoney(ledger.salary)}`}
              </p>
            </div>
            <div className="text-right">
              <p className="text-xs text-slate-400">Qoldiq</p>
              <p
                className={cn(
                  'font-mono text-lg font-semibold',
                  ledger.remaining > 0
                    ? 'text-red-600'
                    : ledger.remaining < 0
                      ? 'text-emerald-600'
                      : 'text-slate-600',
                )}
              >
                {ledger.remaining < 0
                  ? `+${formatMoney(-ledger.remaining)}`
                  : formatMoney(ledger.remaining)}
              </p>
            </div>
          </div>

          {/* Qisqa ko'rsatkichlar */}
          <div className={cn('grid gap-3', journal ? 'grid-cols-4' : 'grid-cols-3')}>
            <SummaryCell label="Hisoblangan" value={formatMoney(ledger.totalExpected)} />
            {journal && (
              <SummaryCell
                label="Jurnal ushlanmasi"
                value={`−${formatMoney(ledger.totalDeduction ?? 0)}`}
                valueClass="text-red-600"
              />
            )}
            <SummaryCell label="Berilgan" value={formatMoney(ledger.totalPaid)} valueClass="text-emerald-600" />
            <SummaryCell
              label="Qoldiq"
              value={ledger.remaining < 0 ? `+${formatMoney(-ledger.remaining)}` : formatMoney(ledger.remaining)}
              valueClass={ledger.remaining > 0 ? 'text-red-600' : 'text-slate-600'}
            />
          </div>

          {journal && (
            <div className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
              <Info className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                Maosh jurnalga bog'langan: jurnalda "o'tildi" deb belgilanmagan dars o'tilmagan
                hisoblanadi va oylikdan ushlanadi. Sababini ko'rish uchun oy qatorini bosing.
              </p>
            </div>
          )}

          {/* Oylar bo'yicha */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Qaysi oyda qancha berilgani</p>
            <div className="overflow-hidden rounded-xl border border-slate-200">
              <table className="table">
                <thead>
                  <tr>
                    <th>Oy</th>
                    {journal && <th className="num">Hisoblandi</th>}
                    {journal && <th className="num">Ushlandi</th>}
                    <th className="num">Belgilangan</th>
                    <th className="num">Berilgan</th>
                    <th className="num">Qoldiq</th>
                    <th className="num">Holat</th>
                  </tr>
                </thead>
                <tbody>
                  {ledger.months.map((m) => {
                    const missed = m.missedLessons ?? 0
                    const open = expanded === m.month
                    return (
                      <Fragment key={m.month}>
                        <tr
                          className={cn(journal && missed > 0 && 'cursor-pointer')}
                          onClick={() =>
                            journal && missed > 0 && setExpanded(open ? null : m.month)
                          }
                        >
                          <td className="font-medium text-slate-700">
                            <span className="flex items-center gap-1.5">
                              {journal && missed > 0 && (
                                <ChevronRight
                                  className={cn(
                                    'h-3.5 w-3.5 text-slate-400 transition-transform',
                                    open && 'rotate-90',
                                  )}
                                />
                              )}
                              {formatMonth(m.month)}
                            </span>
                          </td>
                          {journal && (
                            <td className="num text-slate-400">{formatMoney(m.baseExpected ?? m.expected)}</td>
                          )}
                          {journal && (
                            <td className="num">
                              {(m.deduction ?? 0) > 0 ? (
                                <span className="text-red-600">
                                  −{formatMoney(m.deduction ?? 0)}
                                  <span className="ml-1 text-xs text-slate-400">
                                    ({missed} dars)
                                  </span>
                                </span>
                              ) : (
                                <span className="text-slate-300">—</span>
                              )}
                            </td>
                          )}
                          <td className="num text-slate-600">
                            {formatMoney(m.expected)}
                            {/* Foizli maoshda pul hali yig'ilmagan oy (masalan guruh yopilgan oy)
                                0 bo'lib ko'rinardi — yonida "hisob bo'yicha" turadi. */}
                            {(m.potentialExpected ?? 0) > m.expected && (
                              <div
                                className="text-[11px] text-amber-600"
                                title="O'quvchilarga hisoblangan (qarz bilan) summadan chiqadigan maosh"
                              >
                                hisob bo'yicha {formatMoney(m.potentialExpected ?? 0)}
                              </div>
                            )}
                          </td>
                          <td className="num text-emerald-600">{formatMoney(m.paid)}</td>
                          <td
                            className={cn(
                              'num',
                              m.remaining > 0 ? 'text-red-600' : 'text-slate-400',
                            )}
                          >
                            {m.remaining < 0 ? `+${formatMoney(-m.remaining)}` : formatMoney(m.remaining)}
                          </td>
                          <td className="num">
                            <Badge tone={statusTones[m.status]}>{monthStatusLabels[m.status]}</Badge>
                          </td>
                        </tr>
                        {open && (
                          <tr>
                            <td colSpan={7} className="bg-slate-50 px-4 py-3">
                              <p className="mb-2 text-xs font-semibold text-slate-500">
                                Nima uchun ushlandi — jurnalda belgilanmagan darslar
                              </p>
                              <div className="space-y-2">
                                {(m.lessons ?? [])
                                  .filter((l) => l.missed > 0)
                                  .map((l) => (
                                    <div
                                      key={l.groupId}
                                      className="rounded-lg border border-slate-200 bg-white px-3 py-2"
                                    >
                                      <div className="flex flex-wrap items-center justify-between gap-2">
                                        <span className="text-sm font-semibold text-slate-700">
                                          {l.groupName}
                                        </span>
                                        <span className="text-xs text-slate-500">
                                          {l.conducted}/{l.planned} dars belgilangan ·{' '}
                                          <span className="font-mono font-semibold text-red-600">
                                            −{formatMoney(l.deduction)}
                                          </span>
                                        </span>
                                      </div>
                                      <div className="mt-1.5 flex flex-wrap gap-1">
                                        {l.missedDates.map((d) => (
                                          <span
                                            key={d}
                                            className="rounded-md bg-red-50 px-1.5 py-0.5 font-mono text-[11px] text-red-700"
                                          >
                                            {formatDate(d)}
                                          </span>
                                        ))}
                                      </div>
                                    </div>
                                  ))}
                              </div>
                            </td>
                          </tr>
                        )}
                      </Fragment>
                    )
                  })}
                </tbody>
                <tfoot className="border-t border-slate-200 bg-slate-50 text-sm font-semibold text-slate-700">
                  <tr>
                    <td className="px-4 py-2.5">Jami</td>
                    {journal && <td className="px-4 py-2.5 text-right font-mono text-slate-400" />}
                    {journal && (
                      <td className="px-4 py-2.5 text-right font-mono text-red-600">
                        −{formatMoney(ledger.totalDeduction ?? 0)}
                      </td>
                    )}
                    <td className="px-4 py-2.5 text-right font-mono">{formatMoney(ledger.totalExpected)}</td>
                    <td className="px-4 py-2.5 text-right font-mono text-emerald-700">{formatMoney(ledger.totalPaid)}</td>
                    <td className="px-4 py-2.5 text-right" colSpan={2} />
                  </tr>
                </tfoot>
              </table>
            </div>
          </div>

          {/* Berilgan maoshlar ro'yxati */}
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Berilgan maoshlar</p>
            {ledger.payments.length === 0 ? (
              <p className="text-sm text-slate-400">Bu davrda maosh berilmagan</p>
            ) : (
              <ul className="space-y-1.5">
                {ledger.payments.map((p, i) => (
                  <li
                    key={i}
                    className="flex items-center justify-between rounded-lg border border-slate-100 px-3 py-2 text-sm"
                  >
                    <span className="font-mono text-slate-500">{formatDate(p.date)}</span>
                    <span className="font-mono font-semibold text-slate-700">{formatMoney(p.amount)}</span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* O'zgarishlar tarixi — `audit` ruxsati bilan */}
          {canSeeAudit && (
            <div>
              <p className="mb-2 text-sm font-medium text-slate-600">O'zgarishlar tarixi</p>
              <AuditHistoryList
                filters={{ teacherId: ledger.teacherId }}
                emptyLabel="Maosh bo'yicha o'zgarishlar yo'q"
              />
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}

function SummaryCell({
  label,
  value,
  valueClass = 'text-slate-700',
}: {
  label: string
  value: string
  valueClass?: string
}) {
  return (
    <div className="rounded-xl border border-slate-100 px-3 py-2.5 text-center">
      <p className="text-xs text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-mono font-semibold', valueClass)}>{value}</p>
    </div>
  )
}
