import { useEffect, useState } from 'react'
import { getCashboxStatement, type CashboxStatement } from '@/api/services/cashboxes'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'
import { DateInput } from '@/components/ui/DateInput'
import { financeCategoryLabel } from '@/config/constants'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { todayIso } from '@/lib/month'

/** "2026-10-17" → "17.10.2026". */
const dmy = (iso: string) => (/^\d{4}-\d{2}-\d{2}/.test(iso) ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : iso)

const KIND_LABEL: Record<string, string> = {
  income: 'Kirim',
  expense: 'Chiqim',
  transfer_in: 'Kassadan keldi',
  transfer_out: 'Kassaga ketdi',
}

/**
 * KASSA KO'CHIRMASI (SPEC-backlog-2026-10-17 F3 K1): davr boshidagi qoldiq, kirim, chiqim, ko'chirishlar va yakuniy
 * qoldiq + davrning har harakati. Hisob SERVERDA (`CashboxService.StatementAsync`) — boshlang'ich qoldiq sanasi
 * hisobga olinadi.
 */
export function CashboxStatementModal({ cashboxId, onClose }: { cashboxId: string; onClose: () => void }) {
  const today = todayIso()
  const [from, setFrom] = useState(`${today.slice(0, 7)}-01`)
  const [to, setTo] = useState(today)
  const [data, setData] = useState<CashboxStatement | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getCashboxStatement(cashboxId, from, to)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Ko'chirmani yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [cashboxId, from, to])

  return (
    <Modal open onClose={onClose} size="lg" title={data ? `Ko'chirma — ${data.cashboxName}` : "Kassa ko'chirmasi"}>
      <div className="space-y-4">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="font-medium text-slate-600">Davr:</span>
          <DateInput value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)} className="w-36" />
          <span className="text-slate-400">—</span>
          <DateInput value={to} min={from} onChange={(e) => e.target.value && setTo(e.target.value)} className="w-36" />
        </div>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        {!data ? (error ? null : <Loader />) : (
          <div className={cn('space-y-4', loading && 'opacity-60')}>
            {data.from !== from && (
              <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">
                Boshlang'ich qoldiq {dmy(data.from)} dan kiritilgan — ko'chirma shu sanadan boshlanadi.
              </p>
            )}
            <div className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-5">
              {[
                ['Boshidagi qoldiq', data.opening, 'text-slate-800'],
                ['Kirim', data.income, 'text-emerald-600'],
                ['Chiqim', data.expense, 'text-red-600'],
                ["Ko'chirishlar", data.transferIn - data.transferOut, 'text-sky-700'],
                ['Yakuniy qoldiq', data.closing, 'text-slate-900'],
              ].map(([label, value, cls]) => (
                <div key={label as string} className="rounded-lg border border-slate-200 p-2.5">
                  <div className="text-xs text-slate-500">{label}</div>
                  <div className={cn('font-mono font-semibold tabular-nums', cls as string)}>{formatMoney(value as number)}</div>
                </div>
              ))}
            </div>
            {data.rows.length === 0 ? (
              <p className="py-6 text-center text-sm text-slate-400">Bu davrda harakat yo'q</p>
            ) : (
              <div className="table-wrap max-h-[50vh] overflow-y-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Sana</th>
                      <th>Turi</th>
                      <th>Kim / nima</th>
                      <th className="num">Summa</th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.rows.map((r) => {
                      const plus = r.kind === 'income' || r.kind === 'transfer_in'
                      return (
                        <tr key={`${r.kind}:${r.id}`}>
                          <td className="whitespace-nowrap text-slate-600">{dmy(r.date)}</td>
                          <td>
                            <div className="font-medium text-slate-700">{KIND_LABEL[r.kind] ?? r.kind}</div>
                            {r.kind === 'income' || r.kind === 'expense' ? (
                              <div className="text-xs text-slate-400">{r.typeName ?? financeCategoryLabel(r.category, r.kind)}</div>
                            ) : null}
                          </td>
                          <td className="text-sm text-slate-600">
                            {r.party ?? '—'}
                            {r.note && <div className="text-xs text-slate-400">{r.note}</div>}
                          </td>
                          <td className={cn('num font-mono font-semibold tabular-nums', plus ? 'text-emerald-600' : 'text-red-600')}>
                            {plus ? '+' : '−'}{formatMoney(r.amount)}
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
      </div>
    </Modal>
  )
}
