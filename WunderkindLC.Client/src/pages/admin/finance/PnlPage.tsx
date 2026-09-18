import { useState } from 'react'
import { getFinanceYearReport } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { financeCategoryLabel } from '@/config/constants'
import { YearTable, recentYears, type YearRow } from './YearTable'

/**
 * Moliya → «Moliya hisobotlari (P&L)» (edutizim `/finance/pnl-reports`): kategoriya × oy jadvali.
 * Qatorlar: Jami daromad → (dars bo'yicha / boshqa daromad), Jami xarajat → (kategoriyalar),
 * Sof foyda. Ma'lumot — `GET /admin/finance/year-report` (mavjud tranzaksiyalar yig'indisi).
 */
export function PnlPage() {
  const [year, setYear] = useState(() => new Date().getFullYear())
  const { data, loading, error } = useAsync(() => getFinanceYearReport(year), [year])

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  const rows: YearRow[] = []
  if (data) {
    rows.push({ label: 'Jami daromad', months: data.incomeTotal, tone: 'income' })
    for (const l of data.income) rows.push({ label: financeCategoryLabel(l.category, 'income'), months: l.months, indent: true })
    rows.push({ label: 'Jami xarajat', months: data.expenseTotal, tone: 'expense' })
    for (const l of data.expense) rows.push({ label: financeCategoryLabel(l.category, 'expense'), months: l.months, indent: true })
    rows.push({ label: 'Sof foyda', months: data.net, tone: 'net' })
  }

  return (
    <YearTable
      title="Moliya hisobotlari (P&L)"
      year={year}
      years={recentYears()}
      onYear={setYear}
      rows={rows}
    />
  )
}
