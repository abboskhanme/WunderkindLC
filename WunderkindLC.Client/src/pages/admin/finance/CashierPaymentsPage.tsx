import { useEffect, useMemo, useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, Search, Download, Receipt, Wallet, Banknote, TrendingUp, Inbox } from 'lucide-react'
import { getCashierPayments } from '@/api/services/finance'
import type { CashierPayments } from '@/api/services/kassa'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { ReceiptModal } from '@/components/finance/ReceiptModal'
import { formatMoney, formatDate, formatTime, exportToCsv, cn } from '@/lib/utils'
import { paymentMethodLabel, formatMonth } from '@/config/constants'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'

/**
 * BITTA KASSIR kiritgan to'lovlar — alohida SAHIFA (Moliya → "Kassirlar" jadvalidagi qatorni
 * bosganda ochiladi). Modal emas: ro'yxat uzun bo'lishi mumkin, shuning uchun ichida QIDIRUV,
 * davr (sana) filtri, sahifalash va CSV bor.
 *
 * <p>Manzil: <c>/admin/finance/cashiers/:key</c> — `key` = kassir kaliti (akkaunt id'si yoki eski
 * yozuvlar uchun "name:F.I.Sh"). Ism `?name=` da, davr `?from=&to=` da (Moliya sahifasidagi davr
 * shu yerga ko'chib keladi).</p>
 */
export function CashierPaymentsPage() {
  const { key = '' } = useParams()
  const [params, setParams] = useSearchParams()

  // Kalitdan kassirni ajratamiz: "name:F.I.Sh" — eski (id'siz) yozuvlar, aks holda akkaunt id'si.
  const decodedKey = decodeURIComponent(key)
  const byNameOnly = decodedKey.startsWith('name:')
  const cashierId = byNameOnly ? null : decodedKey
  const cashierName = byNameOnly ? decodedKey.slice(5) : (params.get('name') ?? '')

  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const setRange = (nextFrom: string, nextTo: string) => {
    const next = new URLSearchParams(params)
    next.set('from', nextFrom)
    next.set('to', nextTo)
    setParams(next, { replace: true })
  }

  const [data, setData] = useState<CashierPayments | null>(null)
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [receiptTx, setReceiptTx] = useState<string | null>(null)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr/kassir o'zgarganda yuklash (maqsadli)
    setLoading(true)
    let alive = true
    getCashierPayments(from || undefined, to || undefined, cashierId, cashierName)
      .then((d) => alive && setData(d))
      .catch(() => alive && setData(null))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [cashierId, cashierName, from, to])

  /** Ichki qidiruv: o'quvchi, guruh, kurs, o'qituvchi, kvitansiya (kv123 ham, 123 ham), karta oxiri, summa. */
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    const list = data?.payments ?? []
    if (!q) return list
    return list.filter((p) => {
      const receipt = (p.receiptNo ?? '').toLowerCase()
      return (
        p.studentName.toLowerCase().includes(q) ||
        p.groupName.toLowerCase().includes(q) ||
        p.courseName.toLowerCase().includes(q) ||
        p.teacherName.toLowerCase().includes(q) ||
        receipt.includes(q) ||
        receipt.replace(/^kv/, '').includes(q) ||
        (p.cardLast4 ?? '').includes(q) ||
        String(p.amount).includes(q) ||
        (p.month ?? '').includes(q) ||
        p.date.includes(q)
      )
    })
  }, [data, search])

  const pg = usePagination(filtered)
  const shownTotal = filtered.reduce((a, p) => a + p.amount, 0)
  const s = data?.summary

  const exportCsv = () => {
    exportToCsv(
      `kassir-tolovlari.csv`,
      ['Sana', 'Vaqt', "O'quvchi", 'Guruh', 'Kurs', "O'qituvchi", 'Oy', 'Usul', 'Kvitansiya', 'Summa'],
      filtered.map((p) => [
        formatDate(p.date),
        p.paidTime ?? formatTime(p.createdAt) ?? '',
        p.studentName,
        p.groupName,
        p.courseName,
        p.teacherName,
        p.month ?? '',
        paymentMethodLabel(p.method),
        p.receiptNo ?? (p.cardLast4 ? `**** ${p.cardLast4}` : ''),
        String(p.amount),
      ]),
    )
  }

  return (
    <div>
      <PageHeader
        title={cashierName || 'Kassir'}
        sub="Shu xodim qabul qilgan to'lovlar — davrni tanlang, ichidan qidiring"
        actions={
          <Link to="/admin/finance">
            <Button variant="secondary">
              <ArrowLeft className="h-4 w-4" /> Moliya
            </Button>
          </Link>
        }
      />

      {/* Davr */}
      <div className="toolbar mb-4 flex flex-wrap items-center gap-2">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <input type="date" value={from} onChange={(e) => setRange(e.target.value, to)} className={control} />
        <span className="text-slate-400">—</span>
        <input type="date" value={to} onChange={(e) => setRange(from, e.target.value)} className={control} />
      </div>

      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <div className="mb-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
            <StatCard label="To'lovlar soni" value={String(s?.count ?? 0)} icon={Receipt} />
            <StatCard
              label="Jami qabul qilingan"
              value={formatMoney(s?.total ?? 0)}
              icon={TrendingUp}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
            <StatCard
              label="Naqd"
              value={formatMoney(s?.cash ?? 0)}
              icon={Banknote}
              iconBg="bg-teal-50"
              iconColor="text-teal-600"
            />
            <StatCard
              label="Karta"
              value={formatMoney(s?.card ?? 0)}
              icon={Wallet}
              iconBg="bg-blue-50"
              iconColor="text-blue-600"
            />
          </div>

          <Card
            tight
            title="To'lovlar"
            sub={
              search
                ? `Qidiruv natijasi: ${filtered.length} ta · ${formatMoney(shownTotal)}`
                : "Kim to'lagani, qaysi guruh uchun va qanday qabul qilingani"
            }
            actions={
              <div className="flex flex-wrap items-center gap-2">
                <div className="relative">
                  <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                  <input
                    type="text"
                    placeholder="O'quvchi / guruh / kvitansiya..."
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    className="w-56 rounded-lg border border-slate-200 bg-white py-2 pl-8 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400"
                  />
                </div>
                <Button variant="secondary" onClick={exportCsv} disabled={filtered.length === 0}>
                  <Download className="h-4 w-4" /> CSV
                </Button>
              </div>
            }
          >
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Sana</th>
                    <th>O'quvchi</th>
                    <th>Guruh</th>
                    <th>Oy</th>
                    <th>Usul</th>
                    <th>Kvitansiya</th>
                    <th className="num">Summa</th>
                    <th className="num">Chek</th>
                  </tr>
                </thead>
                <tbody>
                  {pg.paged.map((p) => (
                    <tr key={p.id}>
                      <td className="font-mono text-[12.5px] text-slate-500">
                        {formatDate(p.date)}
                        {(p.paidTime || formatTime(p.createdAt)) && (
                          <span className="ml-1 text-slate-400">{p.paidTime || formatTime(p.createdAt)}</span>
                        )}
                      </td>
                      <td className="font-medium text-slate-800">{p.studentName || '—'}</td>
                      <td className="text-slate-600">
                        {p.groupName || '—'}
                        {p.teacherName && <div className="text-[11px] text-slate-400">{p.teacherName}</div>}
                      </td>
                      <td className="text-slate-600">{p.month ? formatMonth(p.month) : '—'}</td>
                      <td className="text-slate-600">{paymentMethodLabel(p.method)}</td>
                      <td className="font-mono text-[12.5px] text-slate-600">
                        {p.receiptNo ?? (p.cardLast4 ? `•••• ${p.cardLast4}` : '—')}
                      </td>
                      <td className={cn('num font-semibold text-emerald-600')}>+{formatMoney(p.amount)}</td>
                      <td className="num">
                        <button
                          type="button"
                          title="Chek (kvitansiya)"
                          onClick={() => setReceiptTx(p.id)}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-brand-50 hover:text-brand-600"
                        >
                          <Receipt className="h-4 w-4" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <TablePagination {...pg} />
            {filtered.length === 0 && (
              <div className="state">
                <div className="state-icon">
                  <Inbox className="h-5 w-5" />
                </div>
                <h4>To'lov yo'q</h4>
                <p>{search ? "Qidiruv bo'yicha to'lov topilmadi." : "Tanlangan davrda to'lov kiritilmagan."}</p>
              </div>
            )}
          </Card>
        </>
      )}

      <ReceiptModal txId={receiptTx} onClose={() => setReceiptTx(null)} />
    </div>
  )
}
