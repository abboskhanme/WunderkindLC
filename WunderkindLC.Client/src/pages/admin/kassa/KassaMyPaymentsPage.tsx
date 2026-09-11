import { useEffect, useMemo, useState } from 'react'
import { ReceiptText, Printer, Loader2, Search, ChevronLeft, ChevronRight, X } from 'lucide-react'
import { getMyKassaPayments, type CashierPayments } from '@/api/services/kassa'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { formatMoney, formatDate, formatTime, cn } from '@/lib/utils'
import { paymentMethodLabel, formatMonth } from '@/config/constants'

/** Davr: aniq KUN (kalendardan tanlanadi) yoki tez tanlov — 7 kun / shu oy. */
type Period = 'day' | 'week' | 'month'

/** To'lov usuli filtri. */
type MethodFilter = 'all' | 'cash' | 'card' | 'bank'

const iso = (d: Date) => d.toISOString().slice(0, 10)
const today = () => iso(new Date())

/** Kunni siljitish ("2026-07-28" + 1 kun). */
function shiftDay(day: string, delta: number): string {
  const d = new Date(`${day}T00:00:00`)
  d.setDate(d.getDate() + delta)
  return iso(d)
}

/**
 * "To'lovlarim" — kassir O'ZI kiritgan to'lovlar va jami. Server faqat token egasining
 * yozuvlarini qaytaradi (birovnikini ko'rib bo'lmaydi).
 *
 * Davr: KUNMA-KUN (kalendardan istalgan kunni tanlash yoki ◀ ▶ bilan siljitish) yoki tez tanlov
 * — "7 kun" / "Shu oy". Ichida QIDIRUV (F.I.Sh, kvitansiya "kv123"/"123", guruh, summa) va
 * TO'LOV USULI filtri bor; jami summalar ekrandagi (filtrlangan) ro'yxatga qarab hisoblanadi.
 * Qatorni bosish — chekni qayta chiqarish.
 */
export function KassaMyPaymentsPage() {
  const [period, setPeriod] = useState<Period>('day')
  const [day, setDay] = useState<string>(today())
  const [data, setData] = useState<CashierPayments | null>(null)
  const [loading, setLoading] = useState(true)
  const [receiptTx, setReceiptTx] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [method, setMethod] = useState<MethodFilter>('all')

  // Tanlangan davrning (from, to) chegarasi.
  const { from, to } = useMemo(() => {
    if (period === 'day') return { from: day, to: day }
    const now = today()
    if (period === 'week') return { from: shiftDay(now, -6), to: now } // bugun ham kiradi
    return { from: `${now.slice(0, 7)}-01`, to: now }
  }, [period, day])

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda yuklash (maqsadli)
    setLoading(true)
    let alive = true
    getMyKassaPayments(from, to)
      .then((d) => alive && setData(d))
      .catch(() => alive && setData(null))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [from, to])

  /** Qidiruv + usul filtri (server emas — ro'yxat kichik, darhol ishlaydi). */
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return (data?.payments ?? []).filter((p) => {
      if (method !== 'all' && p.method !== method) return false
      if (!q) return true
      const receipt = (p.receiptNo ?? '').toLowerCase()
      return (
        p.studentName.toLowerCase().includes(q) ||
        p.groupName.toLowerCase().includes(q) ||
        p.courseName.toLowerCase().includes(q) ||
        // Kvitansiya: "kv123" ham, faqat "123" ham topsin. Kartada — oxirgi 4 raqam.
        receipt.includes(q) ||
        receipt.replace(/^kv/, '').includes(q) ||
        (p.cardLast4 ?? '').includes(q) ||
        String(p.amount).includes(q)
      )
    })
  }, [data, search, method])

  // Jami — EKRANDAGI (filtrlangan) ro'yxat bo'yicha, ya'ni ko'rinib turgan qatorlar yig'indisi.
  const totals = useMemo(() => {
    const sum = (m: string) => filtered.filter((p) => p.method === m).reduce((a, p) => a + p.amount, 0)
    return {
      count: filtered.length,
      total: filtered.reduce((a, p) => a + p.amount, 0),
      cash: sum('cash'),
      card: sum('card'),
      bank: sum('bank'),
    }
  }, [filtered])

  const filterOn = !!search.trim() || method !== 'all'

  return (
    <div className="space-y-3">
      {/* KUN tanlash — ◀ kalendar ▶ */}
      <div className="flex items-center gap-2">
        <button
          type="button"
          aria-label="Oldingi kun"
          onClick={() => {
            setPeriod('day')
            setDay((d) => shiftDay(d, -1))
          }}
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-500 active:bg-slate-100"
        >
          <ChevronLeft className="h-5 w-5" />
        </button>
        <input
          type="date"
          value={day}
          max={today()}
          onChange={(e) => {
            setPeriod('day')
            setDay(e.target.value || today())
          }}
          className={cn(
            'h-10 min-w-0 flex-1 rounded-xl border px-3 text-center text-[14px] font-semibold outline-none',
            period === 'day' ? 'border-brand-400 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600',
          )}
        />
        <button
          type="button"
          aria-label="Keyingi kun"
          disabled={day >= today()}
          onClick={() => {
            setPeriod('day')
            setDay((d) => shiftDay(d, 1))
          }}
          className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-slate-200 bg-white text-slate-500 disabled:opacity-40 active:bg-slate-100"
        >
          <ChevronRight className="h-5 w-5" />
        </button>
      </div>

      {/* Tez tanlov */}
      <div className="flex gap-2">
        {(
          [
            { key: 'day' as Period, label: 'Bugun', onClick: () => { setPeriod('day'); setDay(today()) } },
            { key: 'week' as Period, label: '7 kun', onClick: () => setPeriod('week') },
            { key: 'month' as Period, label: 'Shu oy', onClick: () => setPeriod('month') },
          ]
        ).map((p) => {
          const active = period === p.key && (p.key !== 'day' || day === today())
          return (
            <button
              key={p.key}
              type="button"
              onClick={p.onClick}
              className={cn(
                'flex-1 rounded-xl px-3 py-2 text-[13px] font-semibold transition',
                active ? 'bg-brand-600 text-white shadow-sm' : 'bg-white text-slate-600 ring-1 ring-slate-200',
              )}
            >
              {p.label}
            </button>
          )
        })}
      </div>

      {/* Jami */}
      <div className="rounded-xl border border-slate-200 bg-white p-4">
        <p className="text-[12px] font-medium text-slate-400">
          {filterOn ? 'Filtr bo\'yicha qabul qilingan' : 'Qabul qilingan (jami)'}
        </p>
        <p className="mt-0.5 font-mono text-2xl font-bold text-emerald-600">
          {loading ? '...' : formatMoney(totals.total)}
        </p>
        <p className="mt-0.5 text-[12px] text-slate-400">
          {totals.count} ta to'lov
          {period === 'day' ? ` · ${formatDate(day)}` : period === 'week' ? ' · 7 kun' : ' · shu oy'}
        </p>
        <div className="mt-3 grid grid-cols-3 gap-2 border-t border-slate-100 pt-3 text-center">
          {[
            { label: 'Naqd', value: totals.cash },
            { label: 'Karta', value: totals.card },
            { label: 'Bank', value: totals.bank },
          ].map((x) => (
            <div key={x.label}>
              <p className="text-[11px] text-slate-400">{x.label}</p>
              <p className="font-mono text-[13px] font-semibold text-slate-700">{formatMoney(x.value)}</p>
            </div>
          ))}
        </div>
      </div>

      {/* Qidiruv — F.I.Sh, kvitansiya raqami, guruh, summa */}
      <div className="relative">
        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="F.I.Sh, kvitansiya (kv123), guruh, summa..."
          className="w-full rounded-xl border border-slate-200 bg-white py-2.5 pl-9 pr-9 text-[14px] text-slate-700 outline-none focus:border-brand-400"
        />
        {search && (
          <button
            type="button"
            onClick={() => setSearch('')}
            aria-label="Tozalash"
            className="absolute right-2 top-1/2 flex h-7 w-7 -translate-y-1/2 items-center justify-center rounded-lg text-slate-400 hover:bg-slate-100"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>

      {/* To'lov usuli filtri */}
      <div className="flex gap-2">
        {(
          [
            { key: 'all', label: 'Barchasi' },
            { key: 'cash', label: 'Naqd' },
            { key: 'card', label: 'Karta' },
            { key: 'bank', label: 'Bank' },
          ] as { key: MethodFilter; label: string }[]
        ).map((m) => (
          <button
            key={m.key}
            type="button"
            onClick={() => setMethod(m.key)}
            className={cn(
              'flex-1 rounded-lg px-2 py-1.5 text-[12px] font-semibold transition',
              method === m.key ? 'bg-slate-800 text-white' : 'bg-white text-slate-500 ring-1 ring-slate-200',
            )}
          >
            {m.label}
          </button>
        ))}
      </div>

      {/* Ro'yxat */}
      <h2 className="flex items-center gap-2 px-1 text-[13px] font-semibold text-slate-500">
        <ReceiptText className="h-4 w-4" /> To'lovlar ({totals.count})
      </h2>
      <div className="overflow-hidden rounded-xl border border-slate-200 bg-white">
        {loading ? (
          <p className="flex items-center justify-center gap-2 py-10 text-sm text-slate-400">
            <Loader2 className="h-4 w-4 animate-spin" /> Yuklanmoqda...
          </p>
        ) : filtered.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">
            {filterOn ? "Qidiruv bo'yicha to'lov topilmadi." : "Bu kunda to'lov kiritilmagan."}
          </p>
        ) : (
          filtered.map((p) => (
            <button
              key={p.id}
              type="button"
              onClick={() => setReceiptTx(p.id)}
              className="flex w-full items-center gap-3 border-b border-slate-100 px-3 py-2.5 text-left last:border-0 active:bg-slate-50"
            >
              <div className="min-w-0 flex-1">
                <p className="truncate text-[14px] font-semibold text-slate-800">{p.studentName || '—'}</p>
                <p className="truncate text-[12px] text-slate-400">
                  {[p.groupName, p.courseName].filter(Boolean).join(' · ') || 'Guruhsiz'}
                </p>
                <p className="truncate text-[11px] text-slate-400">
                  {formatDate(p.date)}
                  {p.paidTime || formatTime(p.createdAt) ? ` ${p.paidTime || formatTime(p.createdAt)}` : ''}
                  {p.month ? ` · ${formatMonth(p.month)} uchun` : ''}
                  {` · ${paymentMethodLabel(p.method)}`}
                  {p.receiptNo ? ` · ${p.receiptNo}` : p.cardLast4 ? ` · •••• ${p.cardLast4}` : ''}
                </p>
              </div>
              <div className="shrink-0 text-right">
                <p className="font-mono text-[14px] font-bold text-emerald-600">{formatMoney(p.amount)}</p>
                <span className="mt-0.5 inline-flex items-center gap-1 text-[11px] text-slate-400">
                  <Printer className="h-3 w-3" /> chek
                </span>
              </div>
            </button>
          ))
        )}
      </div>

      <ReceiptModal txId={receiptTx} onClose={() => setReceiptTx(null)} />
    </div>
  )
}
