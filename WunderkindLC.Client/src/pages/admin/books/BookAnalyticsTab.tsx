import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import {
  Banknote, CreditCard, Wallet, Package, BookOpen, FileDown, TrendingUp, AlertTriangle,
  PackagePlus, HandCoins, ChevronDown, ChevronRight, CalendarDays, Undo2,
} from 'lucide-react'
import type { BookAnalytics, BookDayBookSales, BookSaleRow } from '@/api/services/books'
import { exportBookAnalytics, getBookAnalytics } from '@/api/services/books'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Input } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { paymentLabel, paymentPillCls } from './bookLabels'
import type { BookReturnTarget } from './BookReturnModal'
import { BookReturnModal } from './BookReturnModal'

interface Props {
  /** «Qaytarish» tugmasi sotuvlar lentasida ko'rinadimi (books:edit ruxsati) */
  canReturn: boolean
  /** Qaytarishdan keyin — sahifadagi tab belgilarini (qarz soni) yangilash */
  onReturned?: () => void
}

/** Joriy oyning 1-kuni (default davr boshi). */
function monthStart(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}
const today = () => new Date().toISOString().slice(0, 10)

const WEEKDAYS = ['yakshanba', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba']

/** "2026-08-07" → "07.08 · payshanba" (kun tanlashda o'qishga qulay). */
function dayLabel(date: string): string {
  const d = new Date(`${date}T00:00:00`)
  if (Number.isNaN(d.getTime())) return date
  return `${date.slice(8, 10)}.${date.slice(5, 7)} · ${WEEKDAYS[d.getDay()]}`
}

/** Kunlik grafik ustuni bosilganda ko'rinadigan izoh (dona + to'lov turlari). */
interface DayPoint {
  name: string
  date: string
  qty: number
  Naqd: number
  Karta: number
  Nasiya: number
  total: number
}

/**
 * ANALITIKA: davr bo'yicha qancha kitob sotilgani, sotuv summasi to'lov turlari kesimida
 * (naqd · karta · NASIYA), kunlik grafik, **har kuni qaysi kitob sotilgani** (kun bo'yicha
 * ochiladigan ro'yxat — ichida aniq soati bilan sotuvlar), kitob kesimi va qoldiq ogohlantirishi.
 *
 * ⚠️ Sotuv taqsimoti SOTUV paytidagi to'lov turi bo'yicha: nasiya keyin to'lansa ham o'sha kunda
 * "Nasiya" bo'lib qoladi — aks holda o'tgan kunlarning grafigi orqaga qarab o'zgarib turardi.
 * "Nasiyadan yig'ildi" esa alohida raqam (to'lov sanasi bo'yicha).
 */
export function BookAnalyticsTab({ canReturn, onReturned }: Props) {
  const [from, setFrom] = useState(monthStart)
  const [to, setTo] = useState(today)
  const [data, setData] = useState<BookAnalytics | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  /** Kunlik ro'yxatda ochilgan kun (sotuvlar soati bilan ko'rinadi). */
  const [openDay, setOpenDay] = useState<string | null>(null)
  /** Qaytarish oynasi — lentadagi sotuv qatoridan ochiladi. */
  const [returning, setReturning] = useState<BookReturnTarget | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getBookAnalytics(from, to)
      .then(setData)
      .catch((err) => setError(apiErrorMessage(err, "Hisobotni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [from, to])

  useEffect(load, [load])

  const chartData = useMemo<DayPoint[]>(
    () =>
      (data?.byDay ?? []).map((d) => ({
        name: d.date.slice(5), // MM-DD
        date: d.date,
        qty: d.qty,
        Naqd: d.cash,
        Karta: d.card,
        Nasiya: d.credit,
        total: d.total,
      })),
    [data],
  )

  /** Kunlar ro'yxati (eng yangisi tepada): kun → sotilgan kitoblar + o'sha kungi sotuvlar. */
  const days = useMemo(() => {
    const booksByDate = new Map<string, BookDayBookSales[]>()
    for (const r of data?.byDayBook ?? []) {
      const list = booksByDate.get(r.date)
      if (list) list.push(r)
      else booksByDate.set(r.date, [r])
    }
    const salesByDate = new Map<string, BookSaleRow[]>()
    for (const s of data?.sales ?? []) {
      const key = s.soldAt.slice(0, 10)
      const list = salesByDate.get(key)
      if (list) list.push(s)
      else salesByDate.set(key, [s])
    }
    return [...(data?.byDay ?? [])]
      .reverse()
      .map((d) => ({ ...d, books: booksByDate.get(d.date) ?? [], sales: salesByDate.get(d.date) ?? [] }))
  }, [data])

  const quick = (days: number) => {
    const end = new Date()
    const start = new Date()
    start.setDate(start.getDate() - days + 1)
    setFrom(start.toISOString().slice(0, 10))
    setTo(end.toISOString().slice(0, 10))
  }

  return (
    <div className="space-y-4">
      {/* ---- Davr ---- */}
      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <Input label="Sanadan" type="date" className="w-auto" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input label="Sanagacha" type="date" className="w-auto" value={to} onChange={(e) => setTo(e.target.value)} />
          <div className="tabs">
            <button type="button" className="tab" onClick={() => quick(7)}>
              7 kun
            </button>
            <button type="button" className="tab" onClick={() => quick(30)}>
              30 kun
            </button>
            <button
              type="button"
              className="tab"
              onClick={() => {
                setFrom(monthStart())
                setTo(today())
              }}
            >
              Bu oy
            </button>
            <button
              type="button"
              className="tab"
              onClick={() => {
                setFrom('')
                setTo('')
              }}
            >
              Butun davr
            </button>
          </div>
          <div className="ml-auto">
            <Button variant="secondary" onClick={() => exportBookAnalytics(from, to)}>
              <FileDown className="h-4 w-4" /> Excel
            </Button>
          </div>
        </div>
      </Card>

      {error && (
        <div className="flex items-center gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {loading || !data ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        <>
          {/* ---- SOTUV (davr ichida) ---- */}
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard
              label="Sotilgan kitob"
              value={`${data.soldQty} dona`}
              icon={BookOpen}
              iconBg="bg-violet-50"
              iconColor="text-violet-600"
              hint={
                `${data.ordersApproved} ta sotuv · ${data.ordersPending} kutilmoqda` +
                // Sonlar SOF — qaytarilgani ayirilgan. Farq ko'rinib tursin.
                (data.returnedQty > 0 ? ` · ${data.returnedQty} dona qaytarilgan (ayirilgan)` : '')
              }
            />
            <StatCard
              label="Sotuv — jami"
              value={`${formatMoney(data.revenueTotal)} so'm`}
              icon={Wallet}
              hint="Naqd + karta + nasiya (tasdiqlangan sotuvlar)"
            />
            <StatCard
              label="Naqd"
              value={`${formatMoney(data.revenueCash)} so'm`}
              icon={Banknote}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint={pct(data.revenueCash, data.revenueTotal)}
            />
            <StatCard
              label="Karta"
              value={`${formatMoney(data.revenueCard)} so'm`}
              icon={CreditCard}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint={pct(data.revenueCard, data.revenueTotal)}
            />
          </div>

          {/* ---- NASIYA, QAYTARISH va OMBOR ---- */}
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
            <StatCard
              label="Nasiyaga sotildi"
              value={`${formatMoney(data.creditSold)} so'm`}
              icon={HandCoins}
              iconBg="bg-orange-50"
              iconColor="text-orange-600"
              hint={
                data.creditSoldCount === 0
                  ? 'Bu davrda nasiya yo\'q'
                  : `${data.creditSoldCount} ta · shundan to'landi ${formatMoney(data.creditSoldPaid)}`
              }
            />
            <StatCard
              label="Joriy qarz"
              value={`${formatMoney(data.creditOutstanding)} so'm`}
              icon={HandCoins}
              iconBg={data.creditOverdue > 0 ? 'bg-red-50' : 'bg-orange-50'}
              iconColor={data.creditOverdue > 0 ? 'text-red-600' : 'text-orange-600'}
              hint={
                `${data.creditOutstandingCount} ta to'lanmagan` +
                (data.creditOverdueCount > 0
                  ? ` · ${data.creditOverdueCount} tasi muddati o'tgan (${formatMoney(data.creditOverdue)})`
                  : '') +
                ' — davrga bog\'liq emas'
              }
            />
            <StatCard
              label="Nasiyadan yig'ildi"
              value={`${formatMoney(data.creditCollected)} so'm`}
              icon={PackagePlus}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
              hint={`Davr ichida ${data.creditCollectedCount} ta nasiya to'landi (to'lov sanasi bo'yicha)`}
            />
            {/* QAYTARISH: yuqoridagi sotuv raqamlari SHU QADAR kamaygan (qaytarish sotilgan
                kunga yoziladi). "Kassadan chiqdi" esa qaytarish sanasi bo'yicha — boshqa raqam. */}
            <StatCard
              label="Qaytarilgan"
              value={`${data.returnedQty} dona`}
              icon={Undo2}
              iconBg={data.returnedQty > 0 ? 'bg-amber-50' : undefined}
              iconColor={data.returnedQty > 0 ? 'text-amber-600' : undefined}
              hint={
                data.returnedQty === 0
                  ? "Bu davr sotuvlaridan qaytarilgani yo'q"
                  : `${formatMoney(data.returnedTotal)} so'm sotuvdan ayirildi` +
                    (data.refundedInPeriod > 0
                      ? ` · davrda kassadan ${formatMoney(data.refundedInPeriod)} qaytarildi`
                      : '')
              }
            />
            <StatCard
              label="Ombordagi qoldiq"
              value={`${data.stockTotal} dona`}
              icon={Package}
              iconBg="bg-slate-100"
              iconColor="text-slate-600"
              hint={`Davr ichida kirim: ${data.stockInQty} dona`}
            />
          </div>

          {/* ---- Kunlik grafik ---- */}
          <Card title="Kunlik sotuv" sub="Naqd · karta · nasiya (sotuv paytidagi to'lov turi bo'yicha)">
            {chartData.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">
                Bu davrda tasdiqlangan sotuv yo'q.
              </p>
            ) : (
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={chartData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                  <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={{ fontSize: 12, fill: '#94a3b8' }} />
                  <YAxis
                    tickLine={false}
                    axisLine={false}
                    tick={{ fontSize: 12, fill: '#94a3b8' }}
                    tickFormatter={(v: number) => (v >= 1_000_000 ? `${v / 1_000_000}M` : `${v / 1000}k`)}
                  />
                  <Tooltip cursor={{ fill: 'rgba(0,0,0,0.03)' }} content={<DayTooltip />} />
                  <Legend wrapperStyle={{ fontSize: 13 }} />
                  <Bar dataKey="Naqd" stackId="a" fill="#16a34a" maxBarSize={28} />
                  <Bar dataKey="Karta" stackId="a" fill="#0284c7" maxBarSize={28} />
                  <Bar dataKey="Nasiya" stackId="a" fill="#f97316" radius={[6, 6, 0, 0]} maxBarSize={28} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </Card>

          {/* ---- HAR KUNI SOTILGAN KITOBLAR ---- */}
          <Card
            tight
            title="Har kuni sotilgan kitoblar"
            sub="Qaysi kun qaysi kitob nechta sotilgani — kunni bosing, sotuvlar soati bilan ochiladi"
          >
            {days.length === 0 ? (
              <div className="state">
                <div className="state-icon">
                  <CalendarDays className="h-5 w-5" />
                </div>
                <h4>Sotuv yo'q</h4>
                <p>Bu davrda tasdiqlangan sotuv topilmadi.</p>
              </div>
            ) : (
              <div className="max-h-[560px] overflow-auto">
                <ul className="divide-y divide-slate-100">
                  {days.map((d) => {
                    const open = openDay === d.date
                    return (
                      <li key={d.date}>
                        <button
                          type="button"
                          onClick={() => setOpenDay(open ? null : d.date)}
                          className="flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors hover:bg-slate-50"
                        >
                          {open ? (
                            <ChevronDown className="h-4 w-4 shrink-0 text-slate-400" />
                          ) : (
                            <ChevronRight className="h-4 w-4 shrink-0 text-slate-400" />
                          )}
                          <span className="w-40 shrink-0 text-sm font-semibold text-slate-800">
                            {dayLabel(d.date)}
                          </span>
                          <span className="shrink-0 rounded bg-violet-50 px-2 py-0.5 text-xs font-semibold text-violet-700">
                            {d.qty} dona
                          </span>
                          {/* Kunlik son SOF — shu kungi sotuvlardan qaytarilgani ayirilgan. */}
                          {d.returnedQty > 0 && (
                            <span
                              className="shrink-0 rounded bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700"
                              title="Shu kungi sotuvlardan keyinchalik qaytarilgan (yuqoridagi sondan ayirilgan)"
                            >
                              −{d.returnedQty} qaytarildi
                            </span>
                          )}
                          <span className="truncate text-xs text-slate-400">
                            {d.books.map((b) => `${b.bookTitle} ×${b.qty}`).join(', ')}
                          </span>
                          <span className="ml-auto shrink-0 font-mono text-sm font-semibold text-slate-800">
                            {formatMoney(d.total)}
                          </span>
                        </button>

                        {open && (
                          <div className="bg-slate-50/60 px-4 pb-3 pt-1">
                            {/* Kitob kesimi — shu kunning to'liq surati */}
                            <table className="table">
                              <thead>
                                <tr>
                                  <th>Kitob</th>
                                  <th className="text-right">Dona</th>
                                  <th className="text-right">Sotuvlar</th>
                                  <th className="text-right">Summa</th>
                                </tr>
                              </thead>
                              <tbody>
                                {d.books.map((b) => (
                                  <tr key={b.bookId}>
                                    <td className="text-slate-700">
                                      {b.bookTitle}
                                      {b.returnedQty > 0 && (
                                        <span className="ml-1.5 text-xs text-amber-700">
                                          ({b.returnedQty} qaytarildi)
                                        </span>
                                      )}
                                    </td>
                                    <td className="text-right font-mono font-semibold text-slate-800">{b.qty}</td>
                                    <td className="text-right font-mono text-slate-500">{b.orders}</td>
                                    <td className="text-right font-mono text-slate-700">{formatMoney(b.total)}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>

                            {/* Sotuvlar lentasi — aniq soati va xaridori bilan */}
                            {d.sales.length > 0 ? (
                              <ul className="mt-2 space-y-1">
                                {d.sales.map((s) => (
                                  <li
                                    key={s.id}
                                    className="flex flex-wrap items-center gap-2 rounded-lg bg-white px-3 py-1.5 text-sm"
                                  >
                                    <span className="font-mono text-xs text-slate-400">
                                      {s.soldAt.slice(11, 16)}
                                    </span>
                                    <span className="text-slate-700">{s.bookTitle}</span>
                                    <span className="font-mono text-xs text-slate-500">×{s.qty}</span>
                                    <span className="truncate text-xs text-slate-500">
                                      {s.customerName || "Noma'lum"}
                                    </span>
                                    <span className={paymentPillCls(s.paymentMethod)}>
                                      {paymentLabel(s.paymentMethod)}
                                      {s.paymentMethod === 'credit'
                                        && !s.isPaid
                                        && s.returnedQty < s.qty
                                        && ' · qarz'}
                                    </span>
                                    {/* Lentada XOM (sotuv paytidagi) son turadi — qaytarilgani
                                        alohida belgi bilan, kunlik jamlanma esa sof. */}
                                    {s.returnedQty > 0 && (
                                      <span className="rounded bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700">
                                        {s.returnedQty >= s.qty
                                          ? 'qaytarilgan'
                                          : `${s.returnedQty} qaytarildi`}
                                      </span>
                                    )}
                                    <span className="ml-auto font-mono text-slate-700">
                                      {formatMoney(s.total)}
                                    </span>
                                    {canReturn && s.returnedQty < s.qty && (
                                      <button
                                        type="button"
                                        title="Sotilgan kitobni qaytarib olish"
                                        onClick={() =>
                                          setReturning({
                                            id: s.id,
                                            number: s.number,
                                            bookTitle: s.bookTitle,
                                            customerName: s.customerName,
                                            qty: s.qty,
                                            returnedQty: s.returnedQty,
                                            // Lentada bir dona narxi yo'q — sotuv summasidan
                                            // hisoblanadi (Total = UnitPrice × Qty).
                                            unitPrice: s.qty > 0 ? s.total / s.qty : 0,
                                            paymentMethod: s.paymentMethod,
                                            isPaid: s.isPaid,
                                          })
                                        }
                                        className="inline-flex items-center gap-1 rounded-lg border border-slate-200 px-2 py-1 text-xs font-medium text-amber-700 transition-colors hover:border-amber-300 hover:bg-amber-50"
                                      >
                                        <Undo2 className="h-3.5 w-3.5" /> Qaytarish
                                      </button>
                                    )}
                                  </li>
                                ))}
                              </ul>
                            ) : (
                              data.salesTruncated && (
                                <p className="mt-2 text-xs text-slate-400">
                                  Bu kunning alohida sotuvlari lentaga sig'madi (faqat eng oxirgi{' '}
                                  {data.sales.length} ta sotuv ko'rsatiladi) — to'liq ro'yxat
                                  «Buyurtmalar» tabida.
                                </p>
                              )
                            )}
                          </div>
                        )}
                      </li>
                    )
                  })}
                </ul>
              </div>
            )}
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            {/* ---- Kitob kesimi ---- */}
            <Card
              title="Kitoblar bo'yicha sotuv"
              sub="Qaysi kitob qancha sotildi va hozir qancha qoldi"
              tight
            >
              {data.byBook.length === 0 ? (
                <p className="p-6 text-center text-sm text-slate-400">Bu davrda sotuv yo'q.</p>
              ) : (
                <div className="max-h-[420px] overflow-auto">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Kitob</th>
                        <th className="text-right">Sotilgan</th>
                        <th className="text-right">Tushum</th>
                        <th className="text-right">Qoldiq</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.byBook.map((b, i) => (
                        <tr key={b.bookId}>
                          <td>
                            <span className="mr-1.5 font-mono text-xs text-slate-400">{i + 1}.</span>
                            <span className="text-slate-700">{b.bookTitle}</span>
                          </td>
                          <td className="text-right font-mono font-semibold text-slate-800">{b.qty}</td>
                          <td className="text-right font-mono text-slate-700">{formatMoney(b.total)}</td>
                          <td
                            className={cn(
                              'text-right font-mono',
                              b.stock === 0 ? 'text-red-600' : b.stock <= 3 ? 'text-amber-600' : 'text-slate-500',
                            )}
                          >
                            {b.stock}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>

            {/* ---- Qoldiq ogohlantirishi ---- */}
            <Card
              title="Qoldiq kam qolgan kitoblar"
              sub="3 donadan kam qolgan (yoki tugagan) sotuvdagi kitoblar"
              tight
            >
              {data.lowStock.length === 0 ? (
                <div className="state">
                  <div className="state-icon">
                    <TrendingUp className="h-5 w-5" />
                  </div>
                  <h4>Hammasi joyida</h4>
                  <p>Barcha sotuvdagi kitoblarning qoldig'i yetarli.</p>
                </div>
              ) : (
                <div className="max-h-[420px] overflow-auto">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Kitob</th>
                        <th className="text-right">Qoldiq</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.lowStock.map((b) => (
                        <tr key={b.bookId}>
                          <td className="text-slate-700">{b.bookTitle}</td>
                          <td
                            className={cn(
                              'text-right font-mono font-semibold',
                              b.stock === 0 ? 'text-red-600' : 'text-amber-600',
                            )}
                          >
                            {b.stock === 0 ? 'tugagan' : b.stock}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>
        </>
      )}

      {/* ---- Kitobni qaytarish (sotuvlar lentasidagi qatordan) ---- */}
      <BookReturnModal
        order={returning}
        onClose={() => setReturning(null)}
        onDone={() => {
          // Qaytarish butun hisobotni o'zgartiradi (sof sotuv, kunlik grafik, qoldiq) —
          // shuning uchun davr bo'yicha qayta yuklanadi.
          load()
          onReturned?.()
        }}
      />
    </div>
  )
}

/** Grafik izohi: pul taqsimotidan tashqari SOTILGAN DONA ham ko'rinadi (ikkinchi y-o'q solmasdan). */
function DayTooltip({ active, payload }: { active?: boolean; payload?: { payload: DayPoint }[] }) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  const rows: [string, number, string][] = [
    ['Naqd', d.Naqd, 'text-emerald-600'],
    ['Karta', d.Karta, 'text-sky-600'],
    ['Nasiya', d.Nasiya, 'text-orange-600'],
  ]
  return (
    <div className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[13px] shadow-sm">
      <div className="font-semibold text-slate-800">{dayLabel(d.date)}</div>
      <div className="mb-1 text-xs text-slate-500">{d.qty} dona sotilgan</div>
      {rows
        .filter(([, v]) => v > 0)
        .map(([label, value, cls]) => (
          <div key={label} className="flex justify-between gap-6">
            <span className={cls}>{label}</span>
            <span className="font-mono text-slate-700">{formatMoney(value)}</span>
          </div>
        ))}
      <div className="mt-1 flex justify-between gap-6 border-t border-slate-100 pt-1">
        <span className="text-slate-500">Jami</span>
        <span className="font-mono font-semibold text-slate-800">{formatMoney(d.total)}</span>
      </div>
    </div>
  )
}

/** "Jami"dan ulush foizi (0 bo'lsa bo'sh). */
function pct(part: number, total: number): string {
  if (total <= 0) return ''
  return `jamining ${Math.round((part / total) * 100)}%`
}
