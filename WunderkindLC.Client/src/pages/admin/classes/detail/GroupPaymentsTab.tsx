import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Wallet } from 'lucide-react'
import { getGroupPayments, type GroupPaymentsReport } from '@/api/services/finance'
import { cn, formatMoney, apiErrorMessage } from '@/lib/utils'
import type { BackState } from '@/lib/nav'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { monthLabel } from './shared'

// ============================ Oylik to'lov yig'ilishi (guruh ichida) ============================

/** Oyning oxirgi kuni ("YYYY-MM" → "YYYY-MM-DD"). */
function monthEnd(month: string): string {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  if (!y || !m) return month
  return `${month}-${String(new Date(y, m, 0).getDate()).padStart(2, '0')}`
}

/**
 * Guruh ichidagi "Oylik to'lov yig'ilishi" — tanlangan oy uchun kim to'ladi, kim to'lamadi:
 * hisoblangan/yig'ilgan summa, har o'quvchi bo'yicha to'langan va qarz. Ma'lumot moliya
 * hisobotidan (`/admin/finance/group-payments/{id}`) keladi — shu sabab FAQAT moliya ruxsati
 * bor xodimga ko'rsatiladi (o'qituvchi bu tabni umuman ko'rmaydi).
 */
export function GroupPaymentsTab({
  groupId,
  back,
  months,
  defaultMonth,
}: {
  groupId: string
  /** Profilga o'tilganda "Orqaga" shu guruhga qaytarsin. */
  back: BackState
  months: string[]
  defaultMonth: string
}) {
  const fallbackMonth = new Date().toISOString().slice(0, 7)
  const [month, setMonth] = useState(defaultMonth || months[months.length - 1] || fallbackMonth)
  const [report, setReport] = useState<GroupPaymentsReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- oy o'zgarganda hisobotni qayta yuklash (maqsadli)
    setLoading(true)
    setError(null)
    getGroupPayments(groupId, `${month}-01`, monthEnd(month))
      .then((r) => alive && setReport(r))
      .catch((e) => alive && setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [groupId, month])

  const monthOptions = months.length > 0 ? months : [month]
  const collectionPct = report && report.billed > 0
    ? Math.round((report.collected / report.billed) * 100)
    : 0

  return (
    <Card className="p-0">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
        <div className="flex items-center gap-2">
          <Wallet className="h-5 w-5 text-brand-600" />
          <h2 className="font-semibold text-slate-800">Oylik to'lov yig'ilishi</h2>
          <span className="text-sm text-slate-400">kim to'ladi / kim to'lamadi</span>
        </div>
        <select
          value={month}
          onChange={(e) => setMonth(e.target.value)}
          className="rounded-lg border border-slate-200 bg-white px-3 py-1.5 text-sm text-slate-700 outline-none focus:border-brand-400"
        >
          {monthOptions.map((m) => (
            <option key={m} value={m}>
              {monthLabel(m)}
            </option>
          ))}
        </select>
      </div>

      {loading ? (
        <div className="p-6">
          <Loader label="Yuklanmoqda..." />
        </div>
      ) : error ? (
        <p className="p-6 text-center text-sm text-red-600">{error}</p>
      ) : !report ? null : (
        <div className="space-y-4 p-4">
          {/* Umumiy ko'rsatkichlar */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <PayKpi label="Hisoblangan" value={formatMoney(report.billed)} />
            <PayKpi label="Yig'ilgan" value={formatMoney(report.collected)} tone="green" sub={`${collectionPct}%`} />
            <PayKpi label="To'ladi" value={`${report.paidCount} ta`} tone="green" />
            <PayKpi label="To'lamadi" value={`${report.unpaidCount} ta`} tone="red" />
          </div>

          {report.rows.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">
              Bu oyda guruhda hisob-kitob yoki o'quvchi yo'q.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="py-2 pr-3 font-medium">O'quvchi</th>
                    <th className="py-2 pr-3 font-medium">Holat</th>
                    <th className="py-2 pr-3 text-right font-medium">Hisoblangan</th>
                    <th className="py-2 pr-3 text-right font-medium">To'langan</th>
                    <th className="py-2 text-right font-medium">Qarz</th>
                  </tr>
                </thead>
                <tbody>
                  {report.rows.map((r) => (
                    <tr key={r.studentId} className="border-b border-slate-50 last:border-0">
                      <td className="py-2 pr-3">
                        <Link
                          to={`/admin/students/${r.studentId}`}
                          state={back}
                          className="font-medium text-slate-700 hover:text-brand-600 hover:underline"
                        >
                          {r.fullName}
                        </Link>
                      </td>
                      <td className="py-2 pr-3">
                        {r.fullyPaid ? (
                          <span className="rounded bg-emerald-50 px-1.5 py-0.5 text-[11px] font-medium text-emerald-700">
                            To'liq to'lagan
                          </span>
                        ) : r.hasPaid ? (
                          <span className="rounded bg-amber-50 px-1.5 py-0.5 text-[11px] font-medium text-amber-700">
                            Qisman
                          </span>
                        ) : (
                          <span className="rounded bg-red-50 px-1.5 py-0.5 text-[11px] font-medium text-red-600">
                            To'lamagan
                          </span>
                        )}
                      </td>
                      <td className="py-2 pr-3 text-right font-mono text-slate-600">{formatMoney(r.billed)}</td>
                      <td className="py-2 pr-3 text-right font-mono text-emerald-600">{formatMoney(r.collected)}</td>
                      <td className="py-2 text-right font-mono font-semibold text-red-600">
                        {r.debt > 0 ? formatMoney(r.debt) : <span className="text-slate-300">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </Card>
  )
}

/** "Oylik to'lov yig'ilishi" bo'limidagi kichik ko'rsatkich kartasi. */
function PayKpi({
  label,
  value,
  sub,
  tone,
}: {
  label: string
  value: string
  sub?: string
  tone?: 'green' | 'red'
}) {
  return (
    <div className="rounded-xl border border-slate-200 px-3 py-2.5">
      <p className="text-xs text-slate-400">{label}</p>
      <p
        className={cn(
          'mt-0.5 font-mono text-sm font-semibold',
          tone === 'green' ? 'text-emerald-600' : tone === 'red' ? 'text-red-600' : 'text-slate-700',
        )}
      >
        {value}
        {sub && <span className="ml-1 text-xs font-normal text-slate-400">{sub}</span>}
      </p>
    </div>
  )
}
