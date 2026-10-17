import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, CheckCircle2, FileText, RefreshCw, Search, Wallet } from 'lucide-react'
import { getInvoices, type InvoiceList, type InvoiceStatus } from '@/api/services/financeReports'
import { getClasses } from '@/api/services/classes'
import type { Group } from '@/types'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { formatMonth } from '@/config/constants'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { invoiceStatusLabel, invoiceStatusTone, pctLabel } from '@/lib/financeReports'

/**
 * HISOB-FAKTURALAR (TZ 1.3) — markaz bo'yicha barcha oylik hisoblar: sana, o'quvchi, toifa (kurs), summa,
 * chegirma, to'langan, qoldiq, holat, muddati o'tganlar.
 *
 * Faktura = oylik hisob qatori. To'lov qaysi kuni kiritilganidan qat'i nazar shu OYNING fakturasiga tushadi.
 * Filtr va sahifalash SERVERDA (`GET /admin/reports/invoices`); jamlar butun filtr bo'yicha.
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'
const chip = (on: boolean) =>
  cn(
    'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
    on ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
  )

const STATUSES: Array<[InvoiceStatus | '', string]> = [
  ['', 'Hammasi'],
  ['unpaid', "To'lanmagan"],
  ['partial', 'Qisman'],
  ['paid', "To'langan"],
]

const PAGE_SIZE = 50

/** "2026-10-10" → "10.10.2026". */
const dmy = (iso: string) => (/^\d{4}-\d{2}-\d{2}/.test(iso) ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '—')
const money = (n: number) => (n ? formatMoney(n) : '—')

export function InvoicesReportPage() {
  const cur = currentMonth()
  const [from, setFrom] = useState(cur)
  const [to, setTo] = useState(cur)
  const [status, setStatus] = useState<InvoiceStatus | ''>('')
  const [overdue, setOverdue] = useState(false)
  const [groupId, setGroupId] = useState('')
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [tick, setTick] = useState(0)
  const [data, setData] = useState<InvoiceList | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [groups, setGroups] = useState<Group[]>([])

  useEffect(() => {
    // Filtr ro'yxati — ikkilamchi o'qish: xato bo'lsa filtr bo'sh qoladi, ro'yxat ishlayveradi.
    getClasses(true).then(setGroups).catch(() => {})
  }, [])

  // Qidiruv — server tomonida; har harfda so'rov yubormaslik uchun yozish to'xtagach.
  useEffect(() => {
    const id = setTimeout(() => { setQuery(search); setPage(1) }, 350)
    return () => clearTimeout(id)
  }, [search])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getInvoices({ from, to, status, overdue, q: query, groupId: groupId || undefined, page, pageSize: PAGE_SIZE })
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [from, to, status, overdue, query, groupId, page, tick])

  /** Filtr o'zgarganda birinchi sahifaga qaytadi. */
  const reset = <T,>(set: (v: T) => void) => (v: T) => { set(v); setPage(1) }

  const t = data?.totals
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div>
      <PageHeader
        title="Hisob-fakturalar"
        sub="Har oylik hisob: summa, chegirma, to'langan, qoldiq va to'lov muddati"
      />

      <div className="toolbar mb-3 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Oylar:</span>
        <input type="month" value={from} max={to} onChange={(e) => e.target.value && reset(setFrom)(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr boshi" />
        <span className="text-slate-400">—</span>
        <input type="month" value={to} min={from} onChange={(e) => e.target.value && reset(setTo)(e.target.value)}
          className={cn(control, 'font-mono')} aria-label="Davr oxiri" />
        <div className="flex gap-1">
          <button type="button" onClick={() => { setFrom(cur); setTo(cur); setPage(1) }} className={chip(from === cur && to === cur)}>Joriy oy</button>
          <button type="button" onClick={() => { setFrom(shiftMonth(cur, -2)); setTo(cur); setPage(1) }}
            className={chip(from === shiftMonth(cur, -2) && to === cur)}>3 oy</button>
        </div>
        <select value={groupId} onChange={(e) => reset(setGroupId)(e.target.value)} className={control} aria-label="Guruh">
          <option value="">Barcha guruhlar</option>
          {groups.map((g) => <option key={g.id} value={g.id}>{g.name}{g.isArchived ? ' (arxiv)' : ''}</option>)}
        </select>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>
      <div className="toolbar mb-4 flex-wrap">
        <div className="flex gap-1">
          {STATUSES.map(([k, label]) => (
            <button key={k || 'all'} type="button" onClick={() => reset(setStatus)(k)} className={chip(status === k)}>{label}</button>
          ))}
        </div>
        <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-600">
          <input type="checkbox" checked={overdue} onChange={(e) => reset(setOverdue)(e.target.checked)}
            className="h-4 w-4 accent-brand-600" />
          Faqat muddati o'tganlar
        </label>
        <div className="relative ml-auto w-full max-w-xs">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
          <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="O'quvchi, guruh, kurs..."
            className={cn(control, 'w-full pl-9')} />
        </div>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {!data || !t ? (
        error ? null : <Loader label="Yuklanmoqda..." />
      ) : (
        <div className={cn('space-y-4', loading && 'opacity-60')}>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <StatCard label="Fakturalar" value={data.totalCount} icon={FileText}
              hint={`${data.paidCount} to'langan · ${data.partialCount} qisman · ${data.unpaidCount} to'lanmagan`} />
            <StatCard label="Summa (sof)" value={formatMoney(t.net)} icon={Wallet}
              iconBg="bg-sky-50" iconColor="text-sky-600" hint={`Hisoblangan ${formatMoney(t.charged)} · chegirma ${formatMoney(t.discount)}`} />
            <StatCard label="To'langan" value={formatMoney(t.paid)} icon={CheckCircle2}
              iconBg="bg-indigo-50" iconColor="text-indigo-600" hint={`Yig'ilish ${pctLabel(t.collectionPct)}`} />
            <StatCard label="Qoldiq" value={formatMoney(t.debt)} icon={AlertTriangle}
              iconBg="bg-rose-50" iconColor="text-rose-600" hint={`${data.overdueCount} ta faktura muddati o'tgan`} />
          </div>

          {data.unmatched.count > 0 && (
            <p className="rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
              {formatMoney(data.unmatched.amount)} to'lov ({data.unmatched.count} ta oy) hech bir fakturani yopmagan: hisobi
              yo'q oyga yoki faktura summasidan ortiqcha to'langan.
            </p>
          )}
          <Card title="Fakturalar" sub={`${formatMonth(data.from)} — ${formatMonth(data.to)} · to'lov muddati har oyning ${data.dueDay}-sanasi`} tight>
            {data.items.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">Filtrga mos faktura yo'q</p>
            ) : (
              <>
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Oy</th>
                        <th>O'quvchi</th>
                        <th>Toifa</th>
                        <th className="num">Summa</th>
                        <th className="num">Chegirma</th>
                        <th className="num">To'langan</th>
                        <th className="num">Qoldiq</th>
                        <th>Holat</th>
                        <th>Muddat</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.items.map((r) => (
                        <tr key={r.id || `${r.studentId}:${r.groupId}:${r.month}`}>
                          <td className="whitespace-nowrap">
                            <div className="font-medium text-slate-700">{formatMonth(r.month)}</div>
                            <div className="text-xs text-slate-400">yozildi {dmy(r.date)}</div>
                          </td>
                          <td>
                            <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-700 hover:text-brand-600">{r.studentName}</Link>
                            <div className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
                              <span>{r.groupName}</span>
                              {r.isArchived && <Badge tone="default">Arxiv</Badge>}
                            </div>
                          </td>
                          <td className="text-slate-600">{r.courseName || '—'}</td>
                          <td className="num tabular-nums">{money(r.amount)}</td>
                          <td className="num tabular-nums text-slate-500">{money(r.discount)}</td>
                          <td className="num tabular-nums">{money(r.paid)}</td>
                          <td className="num font-semibold tabular-nums text-slate-800">{money(r.remaining)}</td>
                          <td><Badge tone={invoiceStatusTone(r.status)}>{invoiceStatusLabel(r.status)}</Badge></td>
                          <td className="whitespace-nowrap text-sm">
                            <div className="text-slate-600">{dmy(r.dueDate)}</div>
                            {r.overdueDays > 0 && <div className="text-xs font-semibold text-rose-600">{r.overdueDays} kun o'tdi</div>}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className="flex items-center justify-between border-t border-slate-100 px-4 py-3 text-sm text-slate-500">
                  <span>{data.totalCount} tadan {(data.page - 1) * data.pageSize + 1}–{Math.min(data.page * data.pageSize, data.totalCount)}</span>
                  <div className="flex items-center gap-2">
                    <Button variant="secondary" disabled={data.page <= 1 || loading} onClick={() => setPage(data.page - 1)}>Oldingi</Button>
                    <span className="tabular-nums">{data.page} / {pages}</span>
                    <Button variant="secondary" disabled={data.page >= pages || loading} onClick={() => setPage(data.page + 1)}>Keyingi</Button>
                  </div>
                </div>
              </>
            )}
          </Card>
        </div>
      )}
    </div>
  )
}
