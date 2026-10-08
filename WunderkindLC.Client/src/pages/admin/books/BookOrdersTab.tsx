import { useCallback, useEffect, useState } from 'react'
import {
  Check, X, FileDown, Loader2, Search, ShoppingCart, ExternalLink, AlertTriangle, Receipt, Undo2,
  Banknote, CreditCard, Ban,
} from 'lucide-react'
import type {
  Book, BookOrder, BookOrderFilters, BookOrderStatusFilter, BookSettleMethod,
} from '@/api/services/books'
import {
  approveBookOrder, cancelBookOrder, exportBookOrders, getBookOrders, getBooks, payBookCredit,
  rejectBookOrder,
} from '@/api/services/books'
import type { BookReturnTarget } from './BookReturnModal'
import { BookReturnModal } from './BookReturnModal'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn, formatMoney, maskPhone } from '@/lib/utils'
import { statusLabel, statusPillCls, paymentLabel, paymentPillCls } from './bookLabels'
import { BookSellModal } from './BookSellModal'

interface Props {
  /** Tasdiqlash/rad etish/qaytarish tugmalari ko'rinadimi (books:edit ruxsati) */
  canDecide: boolean
  /** "Kitob sotish" (markazda qo'lda sotuv) tugmasi ko'rinadimi (books:create ruxsati) */
  canSell: boolean
  /** Qaror qabul qilingach — "Buyurtmalar" tabidagi qizil belgini yangilash */
  onDecided: () => void
}

const statusTabs: { value: BookOrderStatusFilter | ''; label: string }[] = [
  { value: 'pending', label: 'Kutilmoqda' },
  { value: 'approved', label: 'Tasdiqlangan' },
  // "To'lov kutilmoqda" — HOLAT emas, kesim: botdan naqd buyurtma tasdiqlangan (kitob zaxirada),
  // mijoz hali kelib olib ketmagan. Kassir shu yerdan «To'landi» yoki «Bekor qilish» qiladi.
  { value: 'awaiting', label: "To'lov kutilmoqda" },
  { value: 'rejected', label: 'Rad etilgan' },
  // "Qaytarilgan" — HOLAT emas, kesim: qaytarilgan sotuv "Tasdiqlangan" bo'lib qolaveradi
  // (qaytarish qisman ham bo'ladi), shuning uchun uni alohida ko'rish uchun filtr kerak.
  { value: 'returned', label: 'Qaytarilgan' },
  { value: '', label: 'Barchasi' },
]

/**
 * Botdan tushgan buyurtmalar. Admin chekni ko'rib "Tasdiqlash" bosadi → ombor qoldig'idan
 * ayiriladi va mijozga botda tasdiq xabari ketadi; "Rad etish"da sabab kiritiladi va mijozga
 * shu sabab yuboriladi.
 */
export function BookOrdersTab({ canDecide, canSell, onDecided }: Props) {
  const [orders, setOrders] = useState<BookOrder[]>([])
  const [books, setBooks] = useState<Book[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState<BookOrder | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [receipt, setReceipt] = useState<BookOrder | null>(null)
  /** Qaytarish oynasi (sotilgan kitob qaytarib olinadi). */
  const [returning, setReturning] = useState<BookReturnTarget | null>(null)
  /** Markazda qo'lda sotuv oynasi ("Kitob sotish"). */
  const [sellOpen, setSellOpen] = useState(false)
  /** OLIB KETISHDA to'lov: mijoz keldi va pul berdi («To'landi»). */
  const [paying, setPaying] = useState<BookOrder | null>(null)
  const [payMethod, setPayMethod] = useState<BookSettleMethod>('cash')
  const [cardLast4, setCardLast4] = useState('')
  /** Mijoz kelmadi — zaxira bekor qilinadi. */
  const [cancelling, setCancelling] = useState<BookOrder | null>(null)
  const [cancelReason, setCancelReason] = useState('')
  /** Oyna ichidagi xato — oyna yopilmaydi, kiritilgan qiymat qoladi. */
  const [modalError, setModalError] = useState('')

  const [filters, setFilters] = useState<BookOrderFilters>({ status: 'pending' })
  // Qidiruv maydonini har harfda so'rov yubormasdan, "Qidirish" bosilganda qo'llaymiz.
  const [search, setSearch] = useState('')

  const load = useCallback((f: BookOrderFilters) => {
    setLoading(true)
    setError('')
    getBookOrders(f)
      .then(setOrders)
      .catch((err) => setError(apiErrorMessage(err, "Buyurtmalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load(filters)
  }, [filters, load])

  useEffect(() => {
    getBooks().then(setBooks).catch(() => setBooks([]))
  }, [])

  const patch = (updated: BookOrder) => {
    // Holat filtri yoqilgan bo'lsa, qarordan keyin qator ro'yxatdan chiqadi. "Qaytarilgan"
    // kesimida esa holat emas, qaytarilgan dona muhim (qaytarilgan sotuv "approved" bo'lib qoladi).
    const stays =
      !filters.status
        ? true
        : filters.status === 'returned'
          ? updated.returnedQty > 0
          : filters.status === 'awaiting'
            ? awaitingPickup(updated)
            : filters.status === updated.status
    setOrders((prev) =>
      stays
        ? prev.map((o) => (o.id === updated.id ? updated : o))
        : prev.filter((o) => o.id !== updated.id),
    )
    onDecided()
  }

  const approve = async (order: BookOrder) => {
    if (busyId) return
    setBusyId(order.id)
    setError('')
    try {
      patch(await approveBookOrder(order.id))
    } catch (err) {
      setError(apiErrorMessage(err, "Tasdiqlab bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const confirmReject = async () => {
    if (!rejecting || busyId) return
    const reason = rejectReason.trim()
    if (!reason) return
    setBusyId(rejecting.id)
    setError('')
    try {
      patch(await rejectBookOrder(rejecting.id, reason))
      setRejecting(null)
      setRejectReason('')
    } catch (err) {
      setError(apiErrorMessage(err, "Rad etib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const confirmPay = async () => {
    if (!paying || busyId) return
    const digits = cardLast4.replace(/\D/g, '')
    if (payMethod === 'card' && digits.length < 4) return
    setBusyId(paying.id)
    setModalError('')
    try {
      patch(await payBookCredit(paying.id, {
        method: payMethod,
        ...(payMethod === 'card' ? { cardLast4: digits.slice(-4) } : {}),
      }))
      setPaying(null)
      setCardLast4('')
    } catch (err) {
      setModalError(apiErrorMessage(err, "To'lovni qayd etib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const confirmCancel = async () => {
    if (!cancelling || busyId) return
    setBusyId(cancelling.id)
    setModalError('')
    try {
      patch(await cancelBookOrder(cancelling.id, cancelReason.trim()))
      setCancelling(null)
      setCancelReason('')
      // Zaxiradagi kitob omborga qaytdi — "omborda N dona" ham yangilansin.
      getBooks().then(setBooks).catch(() => {})
    } catch (err) {
      setModalError(apiErrorMessage(err, "Bekor qilib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="space-y-4">
      {/* ---- Filtrlar ---- */}
      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <div className="tabs">
            {statusTabs.map((s) => (
              <button
                key={s.value || 'all'}
                type="button"
                onClick={() => setFilters((f) => ({ ...f, status: s.value }))}
                className={cn('tab', (filters.status ?? '') === s.value && 'active')}
              >
                {s.label}
              </button>
            ))}
          </div>

          <Input
            label="Sanadan"
            type="date"
            className="w-auto"
            value={filters.from ?? ''}
            onChange={(e) => setFilters((f) => ({ ...f, from: e.target.value }))}
          />
          <Input
            label="Sanagacha"
            type="date"
            className="w-auto"
            value={filters.to ?? ''}
            onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value }))}
          />
          <Select
            label="Kitob"
            className="w-auto"
            value={filters.bookId ?? ''}
            onChange={(e) => setFilters((f) => ({ ...f, bookId: e.target.value }))}
          >
            <option value="">Barcha kitoblar</option>
            {books.map((b) => (
              <option key={b.id} value={b.id}>
                {b.title}
              </option>
            ))}
          </Select>
          <Select
            label="To'lov turi"
            className="w-auto"
            value={filters.method ?? ''}
            onChange={(e) =>
              setFilters((f) => ({ ...f, method: e.target.value as BookOrderFilters['method'] }))
            }
          >
            <option value="">Barchasi</option>
            <option value="cash">Naqd</option>
            <option value="card">Karta</option>
            <option value="credit">Nasiya</option>
          </Select>

          <form
            className="flex items-end gap-2"
            onSubmit={(e) => {
              e.preventDefault()
              setFilters((f) => ({ ...f, q: search.trim() }))
            }}
          >
            <Input
              label="Qidiruv"
              placeholder="Ism, telefon, № ..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <Button type="submit" variant="secondary">
              <Search className="h-4 w-4" /> Qidirish
            </Button>
          </form>

          <div className="ml-auto flex gap-2">
            {canSell && (
              <Button onClick={() => setSellOpen(true)}>
                <ShoppingCart className="h-4 w-4" /> Kitob sotish
              </Button>
            )}
            <Button variant="secondary" onClick={() => exportBookOrders(filters)}>
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

      {/* ---- Ro'yxat ---- */}
      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : orders.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <ShoppingCart className="h-5 w-5" />
            </div>
            <h4>Buyurtma yo'q</h4>
            <p>
              Bu filtr bo'yicha buyurtma topilmadi. Mijozlar botdagi «📚 Kitob sotib olish» tugmasi
              orqali buyurtma berishadi.
            </p>
          </div>
        </Card>
      ) : (
        <Card tight>
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>№</th>
                  <th>Sana</th>
                  <th>Mijoz</th>
                  <th>Kitob</th>
                  <th className="text-right">Soni</th>
                  <th className="text-right">Summa</th>
                  <th>To'lov</th>
                  <th>Chek</th>
                  <th>Holat</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => (
                  <tr key={o.id}>
                    <td className="font-mono text-slate-500">#{o.number}</td>
                    <td className="whitespace-nowrap text-slate-500">
                      {o.createdAt.slice(0, 10)}
                      <span className="ml-1 text-xs text-slate-400">{o.createdAt.slice(11, 16)}</span>
                    </td>
                    <td>
                      <div className="flex items-center gap-1.5">
                        <span className="font-medium text-slate-800">{o.customerName || "Noma'lum"}</span>
                        {o.source === 'manual' && (
                          <span
                            className="rounded bg-amber-50 px-1.5 py-0.5 text-[11px] font-semibold text-amber-700"
                            title="Markazda qo'lda sotilgan (bot orqali emas)"
                          >
                            Qo'lda
                          </span>
                        )}
                      </div>
                      {o.phone && (
                        <div className="font-mono text-xs text-slate-400">{maskPhone(o.phone)}</div>
                      )}
                      {o.studentName && (
                        <div className="text-xs text-brand-600">o'quvchi: {o.studentName}</div>
                      )}
                    </td>
                    <td>
                      <div className="text-slate-700">{o.bookTitle}</div>
                      <div className="text-xs text-slate-400">
                        omborda {o.bookStock} dona
                        {o.status === 'pending' && o.bookStock < o.qty && (
                          <span className="ml-1 font-semibold text-red-500">— yetarli emas!</span>
                        )}
                      </div>
                    </td>
                    {/* Soni va summa — SOF (qaytarilgani ayirilgan), xomi tagi chizilgan holda. */}
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
                    <td>
                      <span className={paymentPillCls(o.paymentMethod)}>
                        {paymentLabel(o.paymentMethod)}
                      </span>
                      {/* Nasiyada eng muhim savol — pul olindimi yoki hali qarzmi.
                          To'liq qaytarilganda esa qarz ham qolmaydi — "qarz" deb yozib
                          qo'yish noto'g'ri bo'lardi. */}
                      {/* Botdan naqd: kitob zaxirada, pul mijoz OLIB KETAYOTGANDA olinadi. */}
                      {o.payAtPickup && o.status === 'approved' && (
                        <div className={cn('mt-0.5 text-xs font-medium',
                          o.isPaid ? 'text-emerald-600' : 'text-orange-600')}>
                          {o.isPaid ? "to'landi" : "olib ketishda to'lanadi"}
                        </div>
                      )}
                      {o.paymentMethod === 'credit' && o.returnedQty < o.qty && (
                        <div
                          className={cn(
                            'mt-0.5 text-xs font-medium',
                            o.isPaid ? 'text-emerald-600' : o.isOverdue ? 'text-red-600' : 'text-orange-600',
                          )}
                        >
                          {o.isPaid
                            ? "to'landi"
                            : o.isOverdue
                              ? `muddati o'tgan (${o.dueDate})`
                              : o.dueDate
                                ? `qarz · ${o.dueDate}`
                                : 'qarz'}
                        </div>
                      )}
                    </td>
                    <td>
                      {o.receiptUrl ? (
                        <button
                          type="button"
                          onClick={() => setReceipt(o)}
                          className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:underline"
                        >
                          <Receipt className="h-4 w-4" /> Ko'rish
                        </button>
                      ) : o.cardLast4 ? (
                        // Qo'lda sotuvda chek rasmi yo'q — kassir kiritgan karta va to'lov vaqti.
                        <div className="whitespace-nowrap font-mono text-xs text-slate-500">
                          •••• {o.cardLast4}
                          {o.paidTime && <span className="ml-1 text-slate-400">{o.paidTime}</span>}
                        </div>
                      ) : (
                        <span className="text-xs text-slate-400">—</span>
                      )}
                    </td>
                    <td>
                      <span className={statusPillCls(o.status)}>{statusLabel(o.status)}</span>
                      {/* QAYTARISH holatni o'zgartirmaydi — alohida belgi bilan ko'rsatiladi. */}
                      {o.returnedQty > 0 && (
                        <div className="mt-0.5">
                          <span className="rounded bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700">
                            {o.returnedQty >= o.qty
                              ? 'Qaytarilgan'
                              : `Qisman qaytarildi (${o.returnedQty})`}
                          </span>
                          <div className="mt-0.5 max-w-[180px] text-xs text-slate-400">
                            {[o.returnedAt?.slice(0, 10), o.returnReason, o.returnedBy]
                              .filter(Boolean)
                              .join(' · ')}
                          </div>
                        </div>
                      )}
                      {o.status === 'rejected' && o.rejectReason && (
                        <div className="mt-0.5 max-w-[180px] text-xs text-slate-400">{o.rejectReason}</div>
                      )}
                      {o.status !== 'pending' && o.decidedBy && (
                        <div className="text-xs text-slate-400">{o.decidedBy}</div>
                      )}
                    </td>
                    <td className="whitespace-nowrap text-right">
                      {canDecide && o.status === 'pending' && (
                        <div className="inline-flex gap-1.5">
                          <Button
                            variant="secondary"
                            disabled={busyId === o.id}
                            onClick={() => approve(o)}
                            className="!bg-emerald-50 !text-emerald-700 hover:!bg-emerald-100"
                          >
                            {busyId === o.id ? (
                              <Loader2 className="h-4 w-4 animate-spin" />
                            ) : (
                              <Check className="h-4 w-4" />
                            )}
                            Tasdiqlash
                          </Button>
                          <Button
                            variant="secondary"
                            disabled={busyId === o.id}
                            onClick={() => {
                              setRejecting(o)
                              setRejectReason('')
                            }}
                            className="!bg-red-50 !text-red-700 hover:!bg-red-100"
                          >
                            <X className="h-4 w-4" /> Rad etish
                          </Button>
                        </div>
                      )}
                      {/* QAYTARISH — faqat TASDIQLANGAN sotuvda (kitob mijozga berilgan va
                          ombordan ayirilgan). Kutilayotgani "Rad etish" bilan yopiladi. */}
                      {/* OLIB KETILMAGAN bot buyurtmasi: mijoz keldi → «To'landi», kelmadi → «Bekor qilish».
                          Bunda «Qaytarish» YO'Q — kitob mijozga berilmagan, pul olinmagan. */}
                      {canDecide && awaitingPickup(o) && (
                        <div className="inline-flex gap-1.5">
                          <Button
                            variant="secondary"
                            disabled={busyId === o.id}
                            className="!bg-emerald-50 !text-emerald-700 hover:!bg-emerald-100"
                            onClick={() => { setPaying(o); setPayMethod('cash'); setCardLast4(''); setModalError('') }}
                          >
                            <Check className="h-4 w-4" /> To'landi
                          </Button>
                          <Button
                            variant="secondary"
                            disabled={busyId === o.id}
                            className="!bg-red-50 !text-red-700 hover:!bg-red-100"
                            onClick={() => { setCancelling(o); setCancelReason(''); setModalError('') }}
                          >
                            <Ban className="h-4 w-4" /> Bekor qilish
                          </Button>
                        </div>
                      )}
                      {canDecide && o.status === 'approved' && !awaitingPickup(o) && o.qty > o.returnedQty && (
                        <Button
                          variant="secondary"
                          className="!bg-amber-50 !text-amber-700 hover:!bg-amber-100"
                          onClick={() =>
                            setReturning({
                              id: o.id,
                              number: o.number,
                              bookTitle: o.bookTitle,
                              customerName: o.customerName,
                              qty: o.qty,
                              returnedQty: o.returnedQty,
                              unitPrice: o.unitPrice,
                              paymentMethod: o.paymentMethod,
                              isPaid: o.isPaid,
                            })
                          }
                        >
                          <Undo2 className="h-4 w-4" /> Qaytarish
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      )}

      {/* ---- Rad etish sababi ---- */}
      <Modal
        open={!!rejecting}
        onClose={() => setRejecting(null)}
        size="sm"
        title={`Buyurtma #${rejecting?.number} — rad etish`}
        footer={
          <>
            <Button variant="secondary" onClick={() => setRejecting(null)} disabled={!!busyId}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={confirmReject} disabled={!!busyId || !rejectReason.trim()}>
              {busyId ? <Loader2 className="h-4 w-4 animate-spin" /> : <X className="h-4 w-4" />}
              Rad etish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            Sabab mijozga botda xabar sifatida yuboriladi. Ombor qoldig'i o'zgarmaydi.
          </p>
          <Textarea
            label="Sabab"
            required
            rows={3}
            placeholder="Masalan: Chek noto'g'ri / Pul tushmadi"
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
          />
          <div className="flex flex-wrap gap-1.5">
            {['Pul tushmadi', "Chek noto'g'ri", 'Summa mos emas', 'Kitob tugadi'].map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => setRejectReason(r)}
                className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-600 hover:border-brand-300 hover:bg-brand-50"
              >
                {r}
              </button>
            ))}
          </div>
        </div>
      </Modal>

      {/* ---- Olib ketishda to'lov qabul qilish ---- */}
      <Modal
        open={!!paying}
        onClose={busyId ? () => {} : () => setPaying(null)}
        size="sm"
        title={`Buyurtma #${paying?.number} — to'lovni qabul qilish`}
        footer={
          <>
            <Button variant="secondary" onClick={() => setPaying(null)} disabled={!!busyId}>
              Bekor
            </Button>
            <Button
              onClick={confirmPay}
              disabled={!!busyId || (payMethod === 'card' && cardLast4.replace(/\D/g, '').length < 4)}
            >
              {busyId ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
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
                {paying.bookTitle} — {paying.qty - paying.returnedQty} dona ·{' '}
                <b className="text-slate-800">{formatMoney(paying.netTotal)} so'm</b>
              </div>
            </div>
            <p className="text-sm text-slate-600">
              Mijoz kitobni olib ketmoqda va pulni berdi. Summa shu paytda <b>kassaga</b> kirim bo'ladi;
              ombor tegilmaydi (kitob tasdiqlashda zaxiraga olingan).
            </p>
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
            {modalError && <p className="text-sm text-red-600">{modalError}</p>}
          </div>
        )}
      </Modal>

      {/* ---- Olib ketilmagan buyurtmani bekor qilish ---- */}
      <Modal
        open={!!cancelling}
        onClose={busyId ? () => {} : () => setCancelling(null)}
        size="sm"
        title={`Buyurtma #${cancelling?.number} — bekor qilish`}
        footer={
          <>
            <Button variant="secondary" onClick={() => setCancelling(null)} disabled={!!busyId}>
              Yopish
            </Button>
            <Button variant="danger" onClick={confirmCancel} disabled={!!busyId}>
              {busyId ? <Loader2 className="h-4 w-4 animate-spin" /> : <Ban className="h-4 w-4" />}
              Bekor qilish
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-slate-600">
            Mijoz kitobni olib ketmadi: zaxiradagi {cancelling ? cancelling.qty - cancelling.returnedQty : 0} dona
            omborga qaytadi, buyurtma «Rad etilgan» bo'ladi va mijozga botda xabar ketadi. Pul olinmagan —
            kassaga hech narsa yozilmaydi.
          </p>
          <Textarea
            label="Sabab (ixtiyoriy)"
            rows={2}
            placeholder="Masalan: Mijoz kelmadi"
            value={cancelReason}
            onChange={(e) => setCancelReason(e.target.value)}
          />
          {modalError && <p className="text-sm text-red-600">{modalError}</p>}
        </div>
      </Modal>

      {/* ---- Chek (rasm yoki PDF) ---- */}
      <Modal
        open={!!receipt}
        onClose={() => setReceipt(null)}
        size="lg"
        title={`Buyurtma #${receipt?.number} — to'lov cheki`}
        footer={
          <>
            {receipt && (
              <a href={receipt.receiptUrl} target="_blank" rel="noreferrer">
                <Button variant="secondary">
                  <ExternalLink className="h-4 w-4" /> Yangi oynada ochish
                </Button>
              </a>
            )}
            <Button onClick={() => setReceipt(null)}>Yopish</Button>
          </>
        }
      >
        {receipt && (
          <div className="space-y-3">
            <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm text-slate-600">
              <b className="text-slate-800">{receipt.customerName || "Noma'lum"}</b>
              {receipt.phone && <span className="ml-2 font-mono">{maskPhone(receipt.phone)}</span>}
              <div>
                {receipt.bookTitle} — {receipt.qty} dona ·{' '}
                <b className="text-slate-800">{formatMoney(receipt.total)} so'm</b>
              </div>
            </div>
            {/\.pdf$/i.test(receipt.receiptUrl) ? (
              <iframe src={receipt.receiptUrl} title="Chek" className="h-[65vh] w-full rounded-lg border" />
            ) : (
              <img
                src={receipt.receiptUrl}
                alt="To'lov cheki"
                className="mx-auto max-h-[65vh] rounded-lg border border-slate-200"
              />
            )}
          </div>
        )}
      </Modal>

      {/* ---- Kitobni qaytarish (vozvrat) ---- */}
      <BookReturnModal
        order={returning}
        onClose={() => setReturning(null)}
        onDone={(updated) => {
          patch(updated)
          // Qoldiq oshdi — kitoblar ro'yxatidagi "omborda N dona" ham yangilansin.
          getBooks().then(setBooks).catch(() => {})
        }}
      />

      {/* Markazda qo'lda sotuv — buyurtma darhol tasdiqlangan holatda yaratiladi */}
      <BookSellModal
        open={sellOpen}
        books={books}
        onClose={() => setSellOpen(false)}
        onSold={() => {
          // Sotuv "approved" bo'lgani uchun joriy filtr "pending" bo'lsa ro'yxatda ko'rinmaydi —
          // shuning uchun butun ro'yxatni qayta yuklaymiz (qoldiqlar ham yangilansin).
          load(filters)
          getBooks().then(setBooks).catch(() => {})
          onDecided()
        }}
      />
    </div>
  )
}

/** Tasdiqlangan, lekin mijoz hali OLIB KETMAGAN (to'lanmagan) bot buyurtmasi. */
function awaitingPickup(o: BookOrder): boolean {
  return !!o.payAtPickup && o.status === 'approved' && !o.isPaid
}
