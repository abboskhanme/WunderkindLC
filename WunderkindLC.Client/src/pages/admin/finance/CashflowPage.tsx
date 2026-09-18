import { useState } from 'react'
import { getFinanceYearReport } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { financeCategoryLabel } from '@/config/constants'
import { YearTable, recentYears, type YearRow } from './YearTable'

/**
 * Moliya → «Pul oqimi hisoboti» (edutizim `/finance/cashflow`): boshlang'ich balans, operatsion
 * faoliyat (Kirim — Jami + turlari, Chiqim — Jami + turlari) va Sof.
 *
 * ⚠️ edutizimdagi "Investitsion/Moliyaviy faoliyat" bo'limlari BIZDA YO'Q: tranzaksiyalarimizda
 * bunday ajratma saqlanmaydi, bo'sh bo'lim esa faqat chalg'itardi (docs/ASSUMPTIONS.md).
 */
export function CashflowPage() {
  const [year, setYear] = useState(() => new Date().getFullYear())
  const { data, loading, error } = useAsync(() => getFinanceYearReport(year), [year])

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  const rows: YearRow[] = []
  if (data) {
    rows.push({ label: "Boshlang'ich balans", months: data.opening, tone: 'income' })
    rows.push({ label: 'Operatsion faoliyat', months: Array(12).fill(0), tone: 'section' })
    rows.push({ label: 'Kirim — Jami', months: data.incomeTotal, tone: 'income' })
    for (const l of data.income) rows.push({ label: financeCategoryLabel(l.category, 'income'), months: l.months, indent: true })
    rows.push({ label: 'Chiqim — Jami', months: data.expenseTotal, tone: 'expense' })
    for (const l of data.expense) rows.push({ label: financeCategoryLabel(l.category, 'expense'), months: l.months, indent: true })
    rows.push({ label: 'Sof', months: data.net, tone: 'net' })
  }

  return (
    <YearTable title="Pul oqimi hisoboti" year={year} years={recentYears()} onYear={setYear} rows={rows} />
  )
}
