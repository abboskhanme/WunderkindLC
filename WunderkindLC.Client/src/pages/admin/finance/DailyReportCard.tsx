import { useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight, TrendingUp, TrendingDown, Wallet, Inbox } from 'lucide-react'
import type { FinanceTransaction } from '@/types'
import { getTransactions } from '@/api/services/finance'
import { financeCategoryLabel, formatMonth, paymentMethodLabel } from '@/config/constants'
import { formatMoney, formatTime, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Badge } from '@/components/ui/Badge'

const WD = ['Yak', 'Du', 'Se', 'Chor', 'Pay', 'Jum', 'Shan'] // getDay 0=Yak..6=Shan

const pad = (n: number) => String(n).padStart(2, '0')

/** Qisqa pul (kalendar katakchasi tor): 1.5M / 250k / 900. */
function compactMoney(v: number): string {
  const a = Math.abs(v)
  if (a >= 1_000_000) return `${(v / 1_000_000).toFixed(a >= 10_000_000 ? 0 : 1)}M`
  if (a >= 1_000) return `${Math.round(v / 1_000)}k`
  return String(v)
}

/**
 * Moliya — KUNLIK hisobot. Yuqorida oy kalendar qatori (uzun, gorizontal). Kunni bossa — faqat
 * o'sha kundagi kirim/chiqim ko'rinadi. Davr (from/to) filtridan MUSTAQIL — o'z oyini o'zi yuklaydi.
 */
export function DailyReportCard({ initialMonth }: { initialMonth: string }) {
  const [month, setMonth] = useState(initialMonth) // "YYYY-MM"
  const [tx, setTx] = useState<FinanceTransaction[]>([])
  const [loading, setLoading] = useState(true)
  const [day, setDay] = useState<string | null>(null) // tanlangan kun "YYYY-MM-DD" | null

  const [y, m] = month.split('-').map(Number)
  const daysInMonth = new Date(y, m, 0).getDate()

  useEffect(() => {
    setLoading(true)
    setDay(null)
    getTransactions({ from: `${month}-01`, to: `${month}-${pad(daysInMonth)}` })
      .then(setTx)
      .catch(() => setTx([]))
      .finally(() => setLoading(false))
  }, [month, daysInMonth])

  // Kun bo'yicha kirim/chiqim yig'indisi.
  const perDay = useMemo(() => {
    const map = new Map<string, { income: number; expense: number }>()
    for (const t of tx) {
      const d = t.date.slice(0, 10)
      const cur = map.get(d) ?? { income: 0, expense: 0 }
      if (t.direction === 'income') cur.income += t.amount
      else cur.expense += t.amount
      map.set(d, cur)
    }
    return map
  }, [tx])

  const shiftMonth = (delta: number) => {
    const nm = new Date(y, m - 1 + delta, 1)
    setMonth(`${nm.getFullYear()}-${pad(nm.getMonth() + 1)}`)
  }

  // Tanlangan kun (yoki butun oy) — kirim/chiqim + tranzaksiyalar.
  const dayTx = day ? tx.filter((t) => t.date.slice(0, 10) === day) : []
  const sel = day ? perDay.get(day) ?? { income: 0, expense: 0 } : null
  const monthTotals = useMemo(() => {
    let income = 0
    let expense = 0
    for (const t of tx) {
      if (t.direction === 'income') income += t.amount
      else expense += t.amount
    }
    return { income, expense }
  }, [tx])

  const shown = sel ?? monthTotals
  const net = shown.income - shown.expense

  // Kirim usuli bo'yicha (naqt/karta/bank) — tanlangan kun bo'lsa shu kun, aks holda butun oy.
  const methodTotals = useMemo(() => {
    const src = day ? tx.filter((t) => t.date.slice(0, 10) === day) : tx
    const acc = { cash: 0, card: 0, bank: 0 }
    for (const t of src) {
      if (t.direction === 'income' && (t.method === 'cash' || t.method === 'card' || t.method === 'bank')) {
        acc[t.method] += t.amount
      }
    }
    return acc
  }, [tx, day])

  return (
    <Card
      tight
      title="Kunlik hisobot"
      sub={day ? 'Tanlangan kun kirim/chiqimi' : 'Kunni bosing — faqat shu kunlik chiqadi'}
      actions={
        <div className="flex items-center gap-1">
          <button
            onClick={() => shiftMonth(-1)}
            className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
            title="Oldingi oy"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="min-w-[90px] text-center text-sm font-semibold text-slate-700">{formatMonth(month)}</span>
          <button
            onClick={() => shiftMonth(1)}
            className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
            title="Keyingi oy"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {/* Kalendar qatori (uzun, gorizontal) */}
          <div className="flex gap-1.5 overflow-x-auto pb-2">
            {Array.from({ length: daysInMonth }, (_, i) => i + 1).map((d) => {
              const dateStr = `${month}-${pad(d)}`
              const dd = perDay.get(dateStr)
              const isSel = day === dateStr
              const dNet = (dd?.income ?? 0) - (dd?.expense ?? 0)
              const wd = WD[new Date(y, m - 1, d).getDay()]
              return (
                <button
                  key={d}
                  onClick={() => setDay(isSel ? null : dateStr)}
                  className={cn(
                    'flex min-w-[54px] flex-col items-center rounded-lg border px-1.5 py-1.5 transition-colors',
                    isSel
                      ? 'border-brand-400 bg-brand-50 ring-1 ring-brand-300'
                      : 'border-slate-200 hover:bg-slate-50',
                  )}
                >
                  <span className="text-[10px] text-slate-400">{wd}</span>
                  <span className={cn('text-sm font-bold', isSel ? 'text-brand-700' : 'text-slate-700')}>{d}</span>
                  {dd ? (
                    <span className={cn('font-mono text-[10px] font-semibold', dNet >= 0 ? 'text-emerald-600' : 'text-red-600')}>
                      {dNet >= 0 ? '+' : '−'}{compactMoney(Math.abs(dNet))}
                    </span>
                  ) : (
                    <span className="text-[10px] text-slate-300">—</span>
                  )}
                </button>
              )
            })}
          </div>

          {/* Tanlangan kun (yoki oy) — KPI */}
          <div className="grid grid-cols-3 gap-3">
            <Mini label="Kirim" value={shown.income} icon={TrendingUp} tone="green" />
            <Mini label="Chiqim" value={shown.expense} icon={TrendingDown} tone="red" />
            <Mini label="Sof" value={net} icon={Wallet} tone={net >= 0 ? 'green' : 'red'} signed />
          </div>

          {/* Kirim usuli bo'yicha (naqt / karta / bank) — tanlangan kun yoki oy */}
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-medium text-slate-400">
              {day ? 'Kunlik kirim usuli:' : 'Oylik kirim usuli:'}
            </span>
            <MethodPill label="Naqd" value={methodTotals.cash} color="emerald" />
            <MethodPill label="Karta" value={methodTotals.card} color="blue" />
            <MethodPill label="Bank" value={methodTotals.bank} color="violet" />
          </div>

          {/* Tanlangan kun tranzaksiyalari */}
          {day && (
            dayTx.length === 0 ? (
              <div className="state">
                <div className="state-icon"><Inbox className="h-5 w-5" /></div>
                <h4>Bu kunda amal yo'q</h4>
                <p>Tanlangan kunda kirim/chiqim yozuvi topilmadi.</p>
              </div>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Vaqt</th>
                      <th>Yo'nalish</th>
                      <th>Toifa</th>
                      <th>To'lov usuli</th>
                      <th>Kvitansiya</th>
                      <th>Kiritgan</th>
                      <th>Izoh</th>
                      <th className="num">Summa</th>
                    </tr>
                  </thead>
                  <tbody>
                    {dayTx.map((t) => (
                      <tr key={t.id}>
                        {/* Kiritilgan (yoki kartada — pul o'tkazilgan) vaqt. */}
                        <td className="font-mono text-[12.5px] text-slate-500">
                          {t.paidTime || formatTime(t.createdAt) || '—'}
                        </td>
                        <td>
                          <Badge tone={t.direction === 'income' ? 'green' : 'red'}>
                            {t.direction === 'income' ? 'Kirim' : 'Chiqim'}
                          </Badge>
                        </td>
                        <td className="text-slate-600">{financeCategoryLabel(t.category)}</td>
                        <td>
                          {t.direction === 'income' && t.method ? (
                            <Badge tone="blue">{paymentMethodLabel(t.method)}</Badge>
                          ) : (
                            <span className="text-slate-300">—</span>
                          )}
                        </td>
                        {/* Kvitansiya: naqdda qog'oz raqami ("KV000123"), kartada karta oxiri. */}
                        <td className="font-mono text-[12.5px] text-slate-600">
                          {t.receiptNo ?? (t.cardLast4 ? `•••• ${t.cardLast4}` : <span className="text-slate-300">—</span>)}
                        </td>
                        {/* KIM KIRITGAN — kassir/admin. */}
                        <td className="text-[12.5px] text-slate-600">
                          {t.createdBy || <span className="text-slate-300">—</span>}
                        </td>
                        <td className="text-slate-500">{t.studentName ? `${t.studentName}` : (t.note ?? '—')}</td>
                        <td className={cn('num font-semibold', t.direction === 'income' ? 'text-emerald-600' : 'text-red-600')}>
                          {t.direction === 'income' ? '+' : '−'}{formatMoney(t.amount)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )
          )}
        </div>
      )}
    </Card>
  )
}

/** Kirim usuli chipi — nom + summa (rangli). */
function MethodPill({ label, value, color }: { label: string; value: number; color: 'emerald' | 'blue' | 'violet' }) {
  const cls = {
    emerald: 'border-emerald-100 bg-emerald-50 text-emerald-700',
    blue: 'border-blue-100 bg-blue-50 text-blue-700',
    violet: 'border-violet-100 bg-violet-50 text-violet-700',
  }[color]
  return (
    <span className={cn('inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs', cls)}>
      <span className="font-medium opacity-80">{label}:</span>
      <span className="font-mono font-bold">{formatMoney(value)}</span>
    </span>
  )
}

function Mini({
  label,
  value,
  icon: Icon,
  tone,
  signed,
}: {
  label: string
  value: number
  icon: typeof TrendingUp
  tone: 'green' | 'red'
  signed?: boolean
}) {
  const color = tone === 'green' ? 'text-emerald-600' : 'text-red-600'
  const bg = tone === 'green' ? 'bg-emerald-50' : 'bg-red-50'
  return (
    <div className="flex items-center gap-2.5 rounded-xl border border-slate-100 bg-white px-3 py-2.5">
      <span className={cn('grid h-9 w-9 shrink-0 place-items-center rounded-lg', bg, color)}>
        <Icon className="h-4 w-4" />
      </span>
      <div className="min-w-0">
        <div className="text-xs text-slate-400">{label}</div>
        <div className={cn('truncate font-mono text-sm font-bold', color)}>
          {signed && value >= 0 ? '+' : signed && value < 0 ? '−' : ''}
          {formatMoney(Math.abs(value))}
        </div>
      </div>
    </div>
  )
}
