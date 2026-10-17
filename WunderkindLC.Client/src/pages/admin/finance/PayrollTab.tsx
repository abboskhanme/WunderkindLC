import { useEffect, useState } from 'react'
import { ChevronLeft, ChevronRight, HandCoins, Pencil, RefreshCw, Users, Wallet } from 'lucide-react'
import { getPayroll, setStaffSalary, type Payroll, type PayrollRow } from '@/api/services/financeOps'
import { createTransaction, isPendingExpense } from '@/api/services/finance'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { MoneyInput } from '@/components/ui/NumberInput'
import { DateInput } from '@/components/ui/DateInput'
import { formatMonth } from '@/config/constants'
import { useCashboxOptions, showCashboxPicker, cashboxOptionLabel } from '@/hooks/useCashboxOptions'
import { currentMonth, shiftMonth, todayIso } from '@/lib/month'
import { newRequestId } from '@/lib/requestId'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'

const SOURCE_LABEL: Record<string, string> = {
  ledger: "O'qituvchi maoshi",
  kpi: 'KPI (tasdiqlangan)',
  'kpi-pending': 'KPI — oklad (oy tasdiqlanmagan)',
  fixed: 'Qat\'iy oylik',
  none: 'Oylik kiritilmagan',
}

const STATUS: Record<string, { tone: 'green' | 'amber' | 'red' | 'default'; label: string }> = {
  paid: { tone: 'green', label: "To'langan" },
  partial: { tone: 'amber', label: 'Qisman' },
  unpaid: { tone: 'red', label: "To'lanmagan" },
  none: { tone: 'default', label: '—' },
}

const money = (n: number) => (n ? formatMoney(n) : '—')

/**
 * Moliya → «Maosh reestri» (SPEC-backlog-2026-10-17 F3 K3; egasining qarori: fiks + KPI, avans / yakuniy). Bir oy
 * uchun o'qituvchilar (maosh hisobidan) va xodimlar (KPI yoki qat'iy oylik): hisoblangan, avans, yakuniy, qoldiq.
 * «To'lash» — odatdagi maosh chiqimi (chegaradan katta bo'lsa tasdiq kutadi).
 */
export function PayrollTab() {
  const cur = currentMonth()
  const [month, setMonth] = useState(cur)
  const [data, setData] = useState<Payroll | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const [paying, setPaying] = useState<PayrollRow | null>(null)
  const [editing, setEditing] = useState<PayrollRow | null>(null)

  useEffect(() => {
    let active = true
    setLoading(true)
    getPayroll(month)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Reestrni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [month, tick])

  const table = (title: string, rows: PayrollRow[], staff: boolean) => (
    <Card title={title} sub={`${rows.length} kishi · ${formatMonth(month)}`} tight>
      {rows.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">Ro'yxat bo'sh</p>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>F.I.Sh</th>
                <th className="num">Hisoblangan</th>
                <th className="num">Avans</th>
                <th className="num">Yakuniy</th>
                <th className="num">Qoldiq</th>
                <th>Holat</th>
                <th className="text-right">Amal</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.id}>
                  <td>
                    <div className="font-medium text-slate-700">{r.name}</div>
                    <div className="text-xs text-slate-400">{r.role} · {SOURCE_LABEL[r.source] ?? r.source}</div>
                  </td>
                  <td className="num font-mono tabular-nums">{money(r.accrued)}</td>
                  <td className="num font-mono tabular-nums">{money(r.advance)}</td>
                  <td className="num font-mono tabular-nums">
                    {money(r.final)}
                    {r.other > 0 && <div className="text-xs text-slate-400" title="Turi belgilanmagan to'lovlar">+{formatMoney(r.other)}</div>}
                  </td>
                  <td className={cn('num font-mono font-semibold tabular-nums', r.remaining > 0 ? 'text-rose-600' : 'text-slate-500')}>
                    {money(Math.max(0, r.remaining))}
                  </td>
                  <td><Badge tone={STATUS[r.status].tone}>{STATUS[r.status].label}</Badge></td>
                  <td className="text-right">
                    <div className="inline-flex gap-1">
                      {staff && r.source !== 'kpi' && r.source !== 'kpi-pending' && (
                        <Button variant="secondary" className="!px-2.5 !py-1 text-xs" onClick={() => setEditing(r)}>
                          <Pencil className="h-3.5 w-3.5" /> Oylik
                        </Button>
                      )}
                      <Button className="!px-2.5 !py-1 text-xs" onClick={() => setPaying(r)}>
                        <HandCoins className="h-3.5 w-3.5" /> To'lash
                      </Button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )

  return (
    <div className="space-y-4">
      <div className="toolbar flex-wrap">
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, -1))} aria-label="Oldingi oy">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <span className="min-w-28 text-center text-sm font-semibold text-slate-700">{formatMonth(month)}</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, 1))} disabled={month >= cur} aria-label="Keyingi oy">
          <ChevronRight className="h-4 w-4" />
        </Button>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {!data ? (error ? null : <Loader />) : (
        <div className={cn('space-y-4', loading && 'opacity-60')}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard label="Hisoblangan maosh" value={formatMoney(data.accrued)} icon={Wallet} />
            <StatCard label="Avans berilgan" value={formatMoney(data.advance)} icon={HandCoins} iconBg="bg-sky-50" iconColor="text-sky-600" />
            <StatCard label="Jami berilgan" value={formatMoney(data.paid)} icon={HandCoins} iconBg="bg-indigo-50" iconColor="text-indigo-600"
              hint={`Yakuniy: ${formatMoney(data.final)}`} />
            <StatCard label="Berilishi kerak" value={formatMoney(data.remaining)} icon={Users} iconBg="bg-rose-50" iconColor="text-rose-600" />
          </div>
          {table("O'qituvchilar", data.teachers, false)}
          {table('Xodimlar (administrator, kassir, operator)', data.staff, true)}
          <p className="text-xs text-slate-400">
            O'qituvchi maoshi — foiz/qat'iy, ushlanma va jarimalar bilan (o'qituvchi profilidagi hisob). Xodim: KPI profili bo'lsa —
            tasdiqlangan KPI oyi (oklad + bonus − jarima), aks holda qat'iy oylik. Chiqim tasdig'i chegarasidan katta to'lov
            mas'ul tasdiqlagach hisobga tushadi.
          </p>
        </div>
      )}

      {paying && (
        <PayModal row={paying} month={month} onClose={() => setPaying(null)} onDone={() => { setPaying(null); setTick((n) => n + 1) }} />
      )}
      {editing && (
        <SalaryModal row={editing} month={month} onClose={() => setEditing(null)} onDone={() => { setEditing(null); setTick((n) => n + 1) }} />
      )}
    </div>
  )
}

/** Maosh to'lovi — avans yoki yakuniy; odatdagi maosh chiqimi (`POST finance/transactions`). */
function PayModal({ row, month, onClose, onDone }: { row: PayrollRow; month: string; onClose: () => void; onDone: () => void }) {
  const [amount, setAmount] = useState<number | null>(row.remaining > 0 ? row.remaining : null)
  const [kind, setKind] = useState<'advance' | 'final'>(row.paid > 0 || row.remaining < row.accrued ? 'final' : 'advance')
  const [date, setDate] = useState(todayIso())
  const [note, setNote] = useState('')
  const [cashbox, setCashbox] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [requestId] = useState(newRequestId)
  const cashboxes = useCashboxOptions(true)
  const cashboxId = cashbox ?? cashboxes?.defaultId ?? ''

  const submit = async () => {
    if (!amount || amount <= 0 || busy) return
    setBusy(true)
    setError('')
    try {
      const res = await createTransaction({
        date, direction: 'expense', category: 'salary', amount, month,
        teacherId: row.kind === 'teacher' ? row.id : undefined,
        staffUserId: row.kind === 'staff' ? row.id : undefined,
        salaryPayKind: kind,
        note: note.trim() || `${row.name} — ${formatMonth(month)} maoshi (${kind === 'advance' ? 'avans' : 'yakuniy'})`,
        cashboxId: cashboxId || undefined,
        requestId,
      })
      if (isPendingExpense(res)) toast.info('Tasdiq kutilmoqda', res.message)
      else toast.success("Maosh berildi", formatMoney(amount))
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "To'lov qilib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const input = 'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm outline-none focus:border-brand-400'
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={`Maosh berish — ${row.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          <Button onClick={() => void submit()} disabled={busy || !amount || amount <= 0}>{busy ? 'Saqlanmoqda...' : 'Berish'}</Button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-500">
          {formatMonth(month)}: hisoblangan {formatMoney(row.accrued)}, berilgan {formatMoney(row.paid)}.
        </p>
        <MoneyInput label="Summa" value={amount} onChange={setAmount} />
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">To'lov turi</span>
          <select value={kind} onChange={(e) => setKind(e.target.value as 'advance' | 'final')} className={input}>
            <option value="advance">Avans</option>
            <option value="final">Yakuniy (oylik)</option>
          </select>
        </label>
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Sana (berilgan kun)</span>
          <DateInput value={date} onChange={(e) => setDate(e.target.value)} />
        </label>
        {showCashboxPicker(cashboxes) && (
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Kassa</span>
            <select value={cashboxId} onChange={(e) => setCashbox(e.target.value)} className={input}>
              {cashboxes.items.map((c) => <option key={c.id} value={c.id}>{cashboxOptionLabel(c)}</option>)}
            </select>
          </label>
        )}
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Izoh</span>
          <input value={note} onChange={(e) => setNote(e.target.value)} className={input} />
        </label>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}

/** Xodimning qat'iy oyligi — tanlangan oydan boshlab (o'tgan oylar o'zgarmaydi). */
function SalaryModal({ row, month, onClose, onDone }: { row: PayrollRow; month: string; onClose: () => void; onDone: () => void }) {
  const [amount, setAmount] = useState<number | null>(row.fixedAmount)
  const [from, setFrom] = useState(month)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const submit = async () => {
    if (amount === null || busy) return
    setBusy(true)
    setError('')
    try {
      await setStaffSalary(row.id, amount, from)
      toast.success('Oylik saqlandi', formatMoney(amount))
      onDone()
    } catch (e) {
      setError(apiErrorMessage(e, "Oylikni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal
      open
      onClose={onClose}
      size="sm"
      title={`Oylik — ${row.name}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>Bekor</Button>
          <Button onClick={() => void submit()} disabled={busy || amount === null}>{busy ? 'Saqlanmoqda...' : 'Saqlash'}</Button>
        </>
      }
    >
      <div className="space-y-3">
        <MoneyInput label="Oylik summa" value={amount} onChange={setAmount} />
        <label className="block text-sm">
          <span className="mb-1 block font-medium text-slate-700">Qaysi oydan boshlab</span>
          <input type="month" value={from} onChange={(e) => e.target.value && setFrom(e.target.value)}
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 font-mono text-sm outline-none focus:border-brand-400" />
        </label>
        <p className="text-xs text-slate-500">O'tgan oylar o'zgarmaydi. KPI profili bor xodimning okladi KPI bo'limida kiritiladi.</p>
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
