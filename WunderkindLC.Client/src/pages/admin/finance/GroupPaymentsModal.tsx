import { useEffect, useState } from 'react'
import { CheckCircle2, XCircle, MinusCircle } from 'lucide-react'
import { getGroupPayments, getTeacherPayments, type GroupPaymentsReport } from '@/api/services/finance'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { Badge } from '@/components/ui/Badge'
import { formatMoney, cn } from '@/lib/utils'

/** A'zolik holati yorlig'i. */
function statusLabel(s: string): { label: string; tone: 'green' | 'amber' | 'blue' | 'default' } {
  if (s === 'active') return { label: 'Faol', tone: 'green' }
  if (s === 'frozen') return { label: 'Muzlatilgan', tone: 'blue' }
  if (s === 'trial') return { label: 'Sinov', tone: 'amber' }
  return { label: '—', tone: 'default' }
}

/** Modal nimani ko'rsatadi: bitta guruhni ('group') yoki o'qituvchining BARCHA guruhlarini ('teacher'). */
export type PaymentsTarget = { kind: 'group' | 'teacher'; id: string; name: string }

/**
 * To'lov holati — kim to'ladi, kim to'lamadi (davr bo'yicha). Bitta guruh (guruh qatori bosilganda)
 * yoki o'qituvchining barcha guruhlari ("Barchasini ko'rish") uchun — jadval ko'rinishi bir xil,
 * o'qituvchi rejimida ko'p guruhli o'quvchi bitta qatorda jamlanadi.
 */
export function GroupPaymentsModal({
  target,
  from,
  to,
  onClose,
}: {
  target: PaymentsTarget | null
  from: string
  to: string
  onClose: () => void
}) {
  const [report, setReport] = useState<GroupPaymentsReport | null>(null)
  const [loading, setLoading] = useState(false)
  const kind = target?.kind
  const id = target?.id

  useEffect(() => {
    if (!id || !kind) return
    setReport(null)
    setLoading(true)
    ;(kind === 'teacher' ? getTeacherPayments(id, from, to) : getGroupPayments(id, from, to))
      .then(setReport)
      .catch(() => setReport(null))
      .finally(() => setLoading(false))
  }, [id, kind, from, to])

  const title = target
    ? target.kind === 'teacher'
      ? `${target.name} — barcha guruhlar to'lov holati`
      : `${target.name} — to'lov holati`
    : ''

  return (
    <Modal open={!!target} onClose={onClose} title={title} size="lg">
      {loading || !report ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {/* KPI */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Kpi label="Hisoblangan" value={formatMoney(report.billed)} />
            <Kpi label="Yig'ilgan" value={formatMoney(report.collected)} tone="green" />
            <Kpi label="To'ladi" value={String(report.paidCount)} tone="green" />
            <Kpi label="To'lamadi" value={String(report.unpaidCount)} tone="red" />
          </div>

          {report.rows.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">
              {target?.kind === 'teacher'
                ? "Bu davrda o'qituvchi guruhlarida hisob-kitob yoki o'quvchi yo'q."
                : "Bu davrda guruhda hisob-kitob yoki o'quvchi yo'q."}
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="px-3 py-2">O'quvchi</th>
                    <th className="px-3 py-2">Holat</th>
                    <th className="px-3 py-2 text-center">To'lov</th>
                    <th className="px-3 py-2 text-right">Hisoblangan</th>
                    <th className="px-3 py-2 text-right">To'langan</th>
                    <th className="px-3 py-2 text-right">Qarz</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-50">
                  {report.rows.map((r) => {
                    const st = statusLabel(r.status)
                    // To'lov ko'rsatkichi: to'liq / qisman / to'lamagan (faqat hisoblangani borlar uchun).
                    const pay = r.billed <= 0
                      ? { icon: MinusCircle, color: 'text-slate-300', label: '—' }
                      : r.fullyPaid
                        ? { icon: CheckCircle2, color: 'text-emerald-600', label: "To'ladi" }
                        : r.hasPaid
                          ? { icon: MinusCircle, color: 'text-amber-600', label: 'Qisman' }
                          : { icon: XCircle, color: 'text-red-600', label: "To'lamadi" }
                    const PayIcon = pay.icon
                    return (
                      <tr key={r.studentId} className="hover:bg-slate-50/60">
                        <td className="px-3 py-2 font-medium text-slate-800">{r.fullName}</td>
                        <td className="px-3 py-2">
                          <Badge tone={st.tone}>{st.label}</Badge>
                        </td>
                        <td className="px-3 py-2">
                          <div className={cn('flex items-center justify-center gap-1.5 font-medium', pay.color)}>
                            <PayIcon className="h-4 w-4" />
                            <span className="text-xs">{pay.label}</span>
                          </div>
                        </td>
                        <td className="px-3 py-2 text-right font-mono text-slate-600">{formatMoney(r.billed)}</td>
                        <td className="px-3 py-2 text-right font-mono font-semibold text-emerald-600">{formatMoney(r.collected)}</td>
                        <td className={cn('px-3 py-2 text-right font-mono font-semibold', r.debt > 0 ? 'text-red-600' : 'text-slate-400')}>
                          {formatMoney(r.debt)}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}

function Kpi({ label, value, tone }: { label: string; value: string; tone?: 'green' | 'red' }) {
  const color = tone === 'green' ? 'text-emerald-700' : tone === 'red' ? 'text-red-700' : 'text-slate-800'
  const bg = tone === 'green' ? 'bg-emerald-50 border-emerald-100' : tone === 'red' ? 'bg-red-50 border-red-100' : 'bg-white border-slate-100'
  return (
    <div className={cn('rounded-xl border px-3 py-3 text-center', bg)}>
      <div className={cn('font-mono text-lg font-bold', color)}>{value}</div>
      <div className="mt-0.5 text-xs text-slate-400">{label}</div>
    </div>
  )
}
