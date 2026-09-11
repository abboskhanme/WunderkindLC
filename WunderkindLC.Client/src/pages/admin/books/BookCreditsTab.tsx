import { useCallback, useEffect, useState } from 'react'
import {
  AlertTriangle, Banknote, CheckCircle2, CreditCard, FileDown, HandCoins, Loader2, Search,
  TrendingDown, Undo2, Users, Wallet,
} from 'lucide-react'
import type { BookCredits, BookOrder, BookSettleMethod } from '@/api/services/books'
import { exportBookCredits, getBookCredits, payBookCredit } from '@/api/services/books'
import type { BookReturnTarget } from './BookReturnModal'
import { BookReturnModal } from './BookReturnModal'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { apiErrorMessage, cn, formatMoney, maskPhone } from '@/lib/utils'

interface Props {
  /** "To'landi" va "Qaytarish" tugmalari ko'rinadimi (books:edit ruxsati) */
  canDecide: boolean
  /** To'lov qabul qilingach (yoki kitob qaytarilgach) — sahifadagi qarz belgisini yangilash */
  onPaid: () => void
}

/** Nasiya qatoridan qaytarish oynasiga uzatiladigan ma'lumot. */
function returnTarget(o: BookOrder): BookReturnTarget {
  return {
    id: o.id,
    number: o.number,
    bookTitle: o.bookTitle,
    customerName: o.customerName,
    qty: o.qty,
    returnedQty: o.returnedQty,
    unitPrice: o.unitPrice,
    paymentMethod: o.paymentMethod,
    isPaid: o.isPaid,
  }
}

type CreditTab = 'unpaid' | 'paid'

/** Joriy oyning 1-kuni (default davr boshi — "yig'ilgan pul" uchun). */
function monthStart(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`
}
const today = () => new Date().toISOString().slice(0, 10)

/**
 * NASIYA — kitob berilgan, pul hali olinmagan sotuvlar.
 *
 * Oqim: «Kitob sotish» oynasida to'lov turi "Nasiya" tanlanadi va xaridor F.I.Sh. bo'yicha
 * qidirib biriktiriladi → kitob ombordan ayiriladi, summa QARZ bo'lib shu tabga tushadi →
 * pul olingach «To'landi» bosiladi va summa o'sha paytdan boshlab tushumga (to'lovlarga) qo'shiladi.
 *
 * «Qaytarish» — kitob qaytarib olinsa dona omborga qaytadi va QARZ o'shancha kamayadi (to'langan
 * nasiyada esa pul mijozga qaytariladi). To'liq qaytarilgan nasiya ro'yxatdan butunlay chiqadi:
 * kitob ham, qarz ham qolmagan.
 *
 * ⚠️ Yuqoridagi "Jami qarz" va "Muddati o'tgan" — JORIY holat: davr va qidiruvdan qat'i nazar
 * butun tarix bo'yicha hisoblanadi (aks holda filtr qarzning bir qismini yashirib qo'yardi).
 * Davr faqat "To'langan" ro'yxatiga va "Davrda yig'ildi" raqamiga ta'sir qiladi.
 */
export function BookCreditsTab({ canDecide, onPaid }: Props) {
  const [tab, setTab] = useState<CreditTab>('unpaid')
  const [from, setFrom] = useState(monthStart)
  const [to, setTo] = useState(today)
  const [search, setSearch] = useState('')
  const [q, setQ] = useState('')

  const [data, setData] = useState<BookCredits | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  // To'lov oynasi
  const [paying, setPaying] = useState<BookOrder | null>(null)
  const [payMethod, setPayMethod] = useState<BookSettleMethod>('cash')
  const [cardLast4, setCardLast4] = useState('')
  const [busy, setBusy] = useState(false)
  /** Qaytarish oynasi (kitob qaytarib olindi — qarz kamayadi yoki pul qaytariladi). */
  const [returning, setReturning] = useState<BookReturnTarget | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    getBookCredits({ status: tab, from, to, q })
      .then(setData)
      .catch((err) => setError(apiErrorMessage(err, "Nasiyalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [tab, from, to, q])

  useEffect(load, [load])

  const confirmPay = async () => {
    if (!paying || busy) return
    if (payMethod === 'card' && cardLast4.replace(/\D/g, '').length < 4) return
    setBusy(true)
    setError('')
    try {
      await payBookCredit(paying.id, {
        method: payMethod,
        ...(payMethod === 'card' ? { cardLast4: cardLast4.replace(/\D/g, '').slice(-4) } : {}),
      })
      setPaying(null)
      setCardLast4('')
      load()
      onPaid()
    } catch (err) {
      setError(apiErrorMessage(err, "To'lovni qayd etib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const orders = data?.orders ?? []

  return (
    <div className="space-y-4">
      {/* ---- Joriy qarz (davrga bog'liq EMAS) ---- */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label="Jami qarz"
          value={`${formatMoney(data?.totalUnpaid ?? 0)} so'm`}
          icon={HandCoins}
          iconBg="bg-orange-50"
          iconColor="text-orange-600"
          hint={`${data?.countUnpaid ?? 0} ta to'lanmagan nasiya`}
        />
        <StatCard
          label="Muddati o'tgan"
          value={`${formatMoney(data?.totalOverdue ?? 0)} so'm`}
          icon={TrendingDown}
          iconBg="bg-red-50"
          iconColor="text-red-600"
          hint={`${data?.countOverdue ?? 0} ta — va'da qilingan sana o'tib ketgan`}
        />
        <StatCard
          label="Qarzdorlar"
          value={`${data?.debtors.length ?? 0} kishi`}
          icon={Users}
          hint="Xaridor kesimida (o'quvchi bo'lsa — o'quvchi bo'yicha)"
        />
        <StatCard
          label="Davrda yig'ildi"
          value={`${formatMoney(data?.collectedInPeriod ?? 0)} so'm`}
          icon={Wallet}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint={`${data?.collectedCount ?? 0} ta nasiya to'landi`}
        />
      </div>

      {/* ---- Filtrlar ---- */}
      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <div className="tabs">
            <button
              type="button"
              className={cn('tab', tab === 'unpaid' && 'active')}
              onClick={() => setTab('unpaid')}
            >
              To'lanmagan
            </button>
            <button
              type="button"
              className={cn('tab', tab === 'paid' && 'active')}
              onClick={() => setTab('paid')}
            >
              To'langan
            </button>
          </div>

          <Input
            label="Sanadan"
            type="date"
            className="w-auto"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
          <Input
            label="Sanagacha"
            type="date"
            className="w-auto"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />

          <form
            className="flex items-end gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              setQ(search.trim())
            }}
          >
            <Input
              label="Qidiruv"
              placeholder="F.I.Sh, telefon, № ..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <Button type="submit" variant="secondary">
              <Search className="h-4 w-4" /> Qidirish
            </Button>
          </form>

          <div className="ml-auto">
            <Button variant="secondary" onClick={() => exportBookCredits({ status: tab, from, to, q })}>
              <FileDown className="h-4 w-4" /> Excel
            </Button>
          </div>
        </div>
        <p className="border-t border-slate-100 px-4 py-2 text-xs text-slate-400">
          Davr faqat «To'langan» ro'yxatiga va «Davrda yig'ildi» raqamiga ta'sir qiladi —
          to'lanmagan qarz har doim to'liq ko'rsatiladi.
        </p>
      </Card>

      {error && (
        <div className="flex items-center gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_340px]">
          {/* ---- Nasiyalar ro'yxati ---- */}
          <Card tight title={tab === 'unpaid' ? "To'lanmagan nasiyalar" : "To'langan nasiyalar"}>
            {orders.length === 0 ? (
              <div className="state">
                <div className="state-icon">
                  <HandCoins className="h-5 w-5" />
                </div>
                <h4>{tab === 'unpaid' ? 'Qarz yo\'q' : "Bu davrda to'lov yo'q"}</h4>
                <p>
                  Nasiya «Buyurtmalar → Kitob sotish» oynasida to'lov turi «Nasiya» tanlanganda
                  paydo bo'ladi.
                </p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="table">
                  <thead>
                    <tr>
                      <th>№</th>
                      <th>Sotilgan</th>
                      <th>Xaridor</th>
                      <th>Kitob</th>
                      <th className="text-right">Soni</th>
                      <th className="text-right">Summa</th>
                      <th>{tab === 'unpaid' ? 'Muddat' : "To'landi"}</th>
                      <th />
                    </tr>
                  </thead>
                  <tbody>
                    {orders.map((o) => (
                      <tr key={o.id} className={cn(o.isOverdue && 'bg-red-50/40')}>
                        <td className="font-mono text-slate-500">#{o.number}</td>
                        <td className="whitespace-nowrap text-slate-500">{o.createdAt.slice(0, 10)}</td>
                        <td>
                          <div className="font-medium text-slate-800">
                            {o.customerName || "Noma'lum"}
                          </div>
                          {o.phone && (
                            <div className="font-mono text-xs text-slate-400">{maskPhone(o.phone)}</div>
                          )}
                          {o.studentName && (
                            <div className="text-xs text-brand-600">o'quvchi: {o.studentName}</div>
                          )}
                        </td>
                        <td className="text-slate-700">
                          {o.bookTitle}
                          {o.returnedQty > 0 && (
                            <div className="text-xs font-medium text-amber-700">
                              {o.returnedQty} dona qaytarildi
                              {o.returnReason && ` — ${o.returnReason}`}
                            </div>
                          )}
                        </td>
                        {/* Soni va summa — SOF (qaytarilgani ayirilgan): qarz aynan shu. */}
                        <td className="text-right font-mono">
                          {o.qty - o.returnedQty}
                          {o.returnedQty > 0 && (
                            <span className="ml-1 text-xs text-slate-400 line-through">{o.qty}</span>
                          )}
                        </td>
                        <td className="text-right font-mono font-semibold text-slate-800">
                          {formatMoney(o.netTotal)}
                          {o.returnedQty > 0 && (
                            <div className="text-xs font-normal text-slate-400 line-through">
                              {formatMoney(o.total)}
                            </div>
                          )}
                        </td>
                        <td className="whitespace-nowrap">
                          {tab === 'paid' ? (
                            <div className="text-xs text-slate-500">
                              {o.paidAt?.slice(0, 10)}
                              <div className="text-slate-400">
                                {o.settledMethod === 'card' ? 'Karta' : 'Naqd'}
                                {o.cardLast4 && ` •••• ${o.cardLast4}`}
                              </div>
                              {o.paidBy && <div className="text-slate-400">{o.paidBy}</div>}
                            </div>
                          ) : o.dueDate ? (
                            <span
                              className={cn(
                                'text-sm',
                                o.isOverdue ? 'font-semibold text-red-600' : 'text-slate-600',
                              )}
                            >
                              {o.dueDate}
                              {o.isOverdue && <span className="ml-1 text-xs">o'tib ketgan</span>}
                            </span>
                          ) : (
                            <span className="text-xs text-slate-400">belgilanmagan</span>
                          )}
                        </td>
                        <td className="whitespace-nowrap text-right">
                          {canDecide && (
                            <div className="inline-flex gap-1.5">
                              {!o.isPaid && (
                                <Button
                                  variant="secondary"
                                  className="!bg-emerald-50 !text-emerald-700 hover:!bg-emerald-100"
                                  onClick={() => {
                                    setPaying(o)
                                    setPayMethod('cash')
                                    setCardLast4('')
                                  }}
                                >
                                  <CheckCircle2 className="h-4 w-4" /> To'landi
                                </Button>
                              )}
                              {/* QAYTARISH: kitob omborga qaytadi va qarz kamayadi (to'langan
                                  nasiyada esa pul mijozga qaytariladi). */}
                              {o.qty > o.returnedQty && (
                                <Button
                                  variant="secondary"
                                  className="!bg-amber-50 !text-amber-700 hover:!bg-amber-100"
                                  onClick={() => setReturning(returnTarget(o))}
                                >
                                  <Undo2 className="h-4 w-4" /> Qaytarish
                                </Button>
                              )}
                            </div>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          {/* ---- Qarzdorlar kesimi ---- */}
          <Card
            tight
            title="Qarzdorlar"
            sub="Kimda qancha qarz bor (to'lanmaganlar bo'yicha)"
          >
            {(data?.debtors.length ?? 0) === 0 ? (
              <p className="p-6 text-center text-sm text-slate-400">Qarzdor yo'q.</p>
            ) : (
              <div className="max-h-[520px] overflow-auto">
                <ul className="divide-y divide-slate-100">
                  {data!.debtors.map((d) => (
                    <li key={d.key}>
                      <button
                        type="button"
                        onClick={() => {
                          // Qarzdorni bosish — ro'yxatni shu odam bo'yicha filtrlaydi.
                          setSearch(d.name)
                          setQ(d.name)
                          setTab('unpaid')
                        }}
                        className="flex w-full items-center gap-3 px-4 py-2.5 text-left transition-colors hover:bg-slate-50"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="truncate text-sm font-medium text-slate-800">
                            {d.name || "Noma'lum"}
                            {d.hasOverdue && (
                              <span className="ml-1.5 rounded bg-red-50 px-1.5 py-0.5 text-[11px] font-semibold text-red-600">
                                muddati o'tgan
                              </span>
                            )}
                          </div>
                          <div className="truncate text-xs text-slate-400">
                            {[d.phone && maskPhone(d.phone), `${d.orders} ta nasiya`, `${d.oldestDate} dan`]
                              .filter(Boolean)
                              .join(' · ')}
                          </div>
                        </div>
                        <span className="shrink-0 font-mono text-sm font-semibold text-orange-600">
                          {formatMoney(d.total)}
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </Card>
        </div>
      )}

      {/* ---- To'lovni qabul qilish ---- */}
      <Modal
        open={!!paying}
        onClose={busy ? () => {} : () => setPaying(null)}
        size="sm"
        title={`Nasiya #${paying?.number} — to'lovni qabul qilish`}
        footer={
          <>
            <Button variant="secondary" onClick={() => setPaying(null)} disabled={busy}>
              Bekor qilish
            </Button>
            <Button
              onClick={confirmPay}
              disabled={busy || (payMethod === 'card' && cardLast4.replace(/\D/g, '').length < 4)}
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <CheckCircle2 className="h-4 w-4" />}
              Tasdiqlash
            </Button>
          </>
        }
      >
        {paying && (
          <div className="space-y-4">
            <div className="rounded-lg bg-slate-50 px-3 py-2.5 text-sm text-slate-600">
              <b className="text-slate-800">{paying.customerName || "Noma'lum"}</b>
              <div>
                {paying.bookTitle} — {paying.qty} dona ·{' '}
                <b className="text-slate-800">{formatMoney(paying.total)} so'm</b>
              </div>
            </div>
            <p className="text-sm text-slate-600">
              Pul olinganini tasdiqlaysiz. Ombor <b>tegilmaydi</b> (kitob sotuv paytida berilgan) —
              summa shu paytdan boshlab tushumga qo'shiladi.
            </p>

            <div>
              <label className="mb-1.5 block text-sm font-medium text-slate-600">Pul qanday olindi</label>
              <div className="grid grid-cols-2 gap-2">
                {([
                  { value: 'cash' as const, label: 'Naqd', icon: Banknote },
                  { value: 'card' as const, label: 'Karta', icon: CreditCard },
                ]).map((m) => (
                  <button
                    key={m.value}
                    type="button"
                    onClick={() => setPayMethod(m.value)}
                    className={cn(
                      'flex items-center justify-center gap-2 rounded-lg border px-3 py-2.5 text-sm font-medium transition-colors',
                      payMethod === m.value
                        ? 'border-brand-400 bg-brand-50 text-brand-700'
                        : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                    )}
                  >
                    <m.icon className="h-4 w-4" /> {m.label}
                  </button>
                ))}
              </div>
            </div>

            {payMethod === 'card' && (
              <Input
                label="Karta (oxirgi 4 raqam)"
                required
                inputMode="numeric"
                placeholder="1234"
                value={cardLast4}
                onChange={(e) => setCardLast4(e.target.value.replace(/\D/g, '').slice(-4))}
              />
            )}
          </div>
        )}
      </Modal>

      {/* ---- Kitobni qaytarish (vozvrat) ---- */}
      <BookReturnModal
        order={returning}
        onClose={() => setReturning(null)}
        onDone={() => {
          // Qaytarishdan keyin qarz summasi ham, qarzdorlar kesimi ham o'zgaradi — ro'yxatni
          // qayta yuklaymiz (to'liq qaytarilgan nasiya ro'yxatdan butunlay chiqadi).
          load()
          onPaid()
        }}
      />
    </div>
  )
}
