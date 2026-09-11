import { useCallback, useEffect, useState } from 'react'
import {
  AlertTriangle, Check, CreditCard, Clock, ExternalLink, Loader2, Receipt, Search, Undo2, X, XCircle,
} from 'lucide-react'
import type { BookCardPayments, BookOrder, BookOrderFilters, BookOrderStatus } from '@/api/services/books'
import { approveBookOrder, getBookCardPayments, rejectBookOrder } from '@/api/services/books'
import type { BookReturnTarget } from './BookReturnModal'
import { BookReturnModal } from './BookReturnModal'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { apiErrorMessage, cn, formatMoney, maskPhone } from '@/lib/utils'
import { statusLabel, statusPillCls } from './bookLabels'

interface Props {
  /** Tasdiqlash/rad etish tugmalari ko'rinadimi (books:edit ruxsati) */
  canDecide: boolean
  /** Qaror qabul qilingach — "Buyurtmalar" tabidagi kutilayotganlar belgisini yangilash */
  onDecided: () => void
}

type Filters = Omit<BookOrderFilters, 'method'>

const statusTabs: { value: BookOrderStatus | ''; label: string }[] = [
  { value: 'pending', label: 'Kutilmoqda' },
  { value: 'approved', label: 'Tasdiqlangan' },
  { value: 'rejected', label: 'Rad etilgan' },
  { value: '', label: 'Barchasi' },
]

/** Chek fayli PDFmi (rasm bo'lsa kichik ko'rinishda chiqariladi). */
const isPdf = (url: string) => /\.pdf$/i.test(url)

/**
 * KARTA TO'LOVLARI — mijoz kartaga o'tkazma qilib, botdagi «🧾 Chekni yuborish» orqali
 * yuborgan cheklar shu yerda ko'rinadi: har qatorda chek rasmi (bosilsa kattalashadi),
 * tepada esa shu kartaga hisoblangan jami summa.
 *
 * Jami summalar SERVERDA butun topilma bo'yicha hisoblanadi — jadval ro'yxati ko'rsatish
 * uchun cheklangan bo'lishi mumkin, undan qo'shib chiqarish noto'g'ri natija berardi.
 *
 * DIQQAT: kitob puli Moliyaga (FinanceTransaction) yozilmaydi — o'quv to'lovi hisobotlari
 * kitob savdosidan ataylab ajratilgan (.claude/rules/books.md §7). "Kartaga hisoblangan"
 * = tasdiqlangan karta buyurtmalari yig'indisi.
 */
export function BookCardPaymentsTab({ canDecide, onDecided }: Props) {
  const [data, setData] = useState<BookCardPayments | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [rejecting, setRejecting] = useState<BookOrder | null>(null)
  const [rejectReason, setRejectReason] = useState('')
  const [receipt, setReceipt] = useState<BookOrder | null>(null)
  /** Qaytarish oynasi (karta bilan to'langan kitob qaytarib olinadi — pul mijozga qaytadi). */
  const [returning, setReturning] = useState<BookReturnTarget | null>(null)

  const [filters, setFilters] = useState<Filters>({ status: 'pending' })
  // Qidiruv har harfda emas, "Enter"/tugma bosilganda qo'llanadi.
  const [search, setSearch] = useState('')

  const load = useCallback((f: Filters) => {
    setLoading(true)
    setError('')
    getBookCardPayments(f)
      .then(setData)
      .catch((err) => setError(apiErrorMessage(err, "Karta to'lovlarini yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load(filters)
  }, [filters, load])

  /** Qaror qabul qilingach jamlanma ham o'zgaradi — ro'yxatni serverdan qayta olamiz. */
  const afterDecision = () => {
    onDecided()
    load(filters)
  }

  const approve = async (order: BookOrder) => {
    if (busyId) return
    setBusyId(order.id)
    setError('')
    try {
      await approveBookOrder(order.id)
      afterDecision()
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
      await rejectBookOrder(rejecting.id, reason)
      setRejecting(null)
      setRejectReason('')
      afterDecision()
    } catch (err) {
      setError(apiErrorMessage(err, "Rad etib bo'lmadi"))
    } finally {
      setBusyId(null)
    }
  }

  const orders = data?.orders ?? []

  return (
    <div className="space-y-4">
      {/* ---- Bo'lim bog'langan karta ---- */}
      <Card tight>
        <div className="flex flex-wrap items-center gap-4 p-4">
          <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-sky-50 text-sky-600">
            <CreditCard className="h-5 w-5" />
          </div>
          <div className="min-w-0">
            <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              To'lovlar tushadigan karta
            </div>
            {data?.cardNumber ? (
              <>
                <div className="font-mono text-lg font-semibold tracking-wide text-slate-800">
                  {data.cardNumber}
                </div>
                {data.cardHolder && <div className="text-sm text-slate-500">{data.cardHolder}</div>}
              </>
            ) : (
              <div className="text-sm text-amber-600">
                Karta rekvizitlari kiritilmagan — «Sozlamalar» tabidan to'ldiring, aks holda botda
                karta orqali to'lov taklif qilinmaydi.
              </div>
            )}
          </div>
        </div>
      </Card>

      {/* ---- Jamlanma (butun topilma bo'yicha) ---- */}
      <div className="grid gap-3 sm:grid-cols-3">
        <StatCard
          label="Kartaga hisoblangan"
          value={`${formatMoney(data?.totalApproved ?? 0)} so'm`}
          icon={CreditCard}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint={`${data?.countApproved ?? 0} ta tasdiqlangan to'lov`}
        />
        <StatCard
          label="Tekshirilmoqda"
          value={`${formatMoney(data?.totalPending ?? 0)} so'm`}
          icon={Clock}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
          hint={`${data?.countPending ?? 0} ta chek kutilmoqda`}
        />
        <StatCard
          label="Rad etilgan"
          value={data?.countRejected ?? 0}
          icon={XCircle}
          iconBg="bg-red-50"
          iconColor="text-red-600"
          hint="Pul tushmagan / chek noto'g'ri"
        />
      </div>

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
            label="Dan"
            type="date"
            value={filters.from ?? ''}
            onChange={(e) => setFilters((f) => ({ ...f, from: e.target.value }))}
          />
          <Input
            label="Gacha"
            type="date"
            value={filters.to ?? ''}
            onChange={(e) => setFilters((f) => ({ ...f, to: e.target.value }))}
          />
          <Input
            label="Qidiruv"
            placeholder="Ism, telefon yoki №"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') setFilters((f) => ({ ...f, q: search.trim() }))
            }}
          />
          <Button variant="secondary" onClick={() => setFilters((f) => ({ ...f, q: search.trim() }))}>
            <Search className="h-4 w-4" /> Qidirish
          </Button>
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
              <CreditCard className="h-5 w-5" />
            </div>
            <h4>Karta to'lovi yo'q</h4>
            <p>
              Bu filtr bo'yicha karta to'lovi topilmadi. Mijoz botda «💳 Karta orqali» ni tanlab,
              «🧾 Chekni yuborish» tugmasi orqali chek rasmini yuborganda shu yerda paydo bo'ladi.
            </p>
          </div>
        </Card>
      ) : (
        <Card tight>
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Chek</th>
                  <th>№</th>
                  <th>Sana</th>
                  <th>Mijoz</th>
                  <th>Kitob</th>
                  <th className="text-right">Soni</th>
                  <th className="text-right">Summa</th>
                  <th>Holat</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {orders.map((o) => (
                  <tr key={o.id}>
                    {/* Chek rasmi — to'g'ridan-to'g'ri ro'yxatda ko'rinadi */}
                    <td>
                      {o.receiptUrl ? (
                        <button
                          type="button"
                          onClick={() => setReceipt(o)}
                          title="Chekni kattalashtirish"
                          className="block overflow-hidden rounded-lg border border-slate-200 transition hover:border-brand-400"
                        >
                          {isPdf(o.receiptUrl) ? (
                            <span className="flex h-16 w-14 flex-col items-center justify-center gap-1 bg-slate-50 text-[10px] font-semibold text-slate-500">
                              <Receipt className="h-5 w-5" /> PDF
                            </span>
                          ) : (
                            <img
                              src={o.receiptUrl}
                              alt="To'lov cheki"
                              loading="lazy"
                              className="h-16 w-14 bg-slate-50 object-cover"
                            />
                          )}
                        </button>
                      ) : o.cardLast4 ? (
                        // Markazda qo'lda sotuv — chek rasmi yo'q, kassir karta va vaqtni kiritgan.
                        <div className="flex h-16 w-14 flex-col items-center justify-center gap-0.5 rounded-lg border border-dashed border-slate-200 bg-slate-50 text-[10px] text-slate-500">
                          <span className="font-mono font-semibold">••{o.cardLast4}</span>
                          {o.paidTime && <span className="text-slate-400">{o.paidTime}</span>}
                        </div>
                      ) : (
                        <span className="text-xs text-slate-400">chek yo'q</span>
                      )}
                    </td>
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
                    {/* Soni va summa — SOF (qaytarilgani ayirilgan): kartaga qolgan pul aynan shu. */}
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
                      <span className={statusPillCls(o.status)}>{statusLabel(o.status)}</span>
                      {o.returnedQty > 0 && (
                        <div className="mt-0.5">
                          <span className="rounded bg-amber-50 px-2 py-0.5 text-xs font-semibold text-amber-700">
                            {o.returnedQty >= o.qty
                              ? 'Qaytarilgan'
                              : `Qisman qaytarildi (${o.returnedQty})`}
                          </span>
                        </div>
                      )}
                      {o.status === 'rejected' && o.rejectReason && (
                        <div className="mt-0.5 max-w-[180px] text-xs text-slate-400">
                          {o.rejectReason}
                        </div>
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
                      {/* QAYTARISH — tasdiqlangan karta sotuvida pul mijozga qaytariladi. */}
                      {canDecide && o.status === 'approved' && o.qty > o.returnedQty && (
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

      {/* ---- Rad etish sababi (mijozga botda yuboriladi) ---- */}
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
            <Button
              variant="danger"
              onClick={confirmReject}
              disabled={!!busyId || !rejectReason.trim()}
            >
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

      {/* ---- Chekni kattalashtirib ko'rish ---- */}
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
            {isPdf(receipt.receiptUrl) ? (
              <iframe
                src={receipt.receiptUrl}
                title="Chek"
                className="h-[65vh] w-full rounded-lg border"
              />
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

      {/* ---- Kitobni qaytarish (vozvrat) — jamlanma ham o'zgaradi, ro'yxat qayta yuklanadi ---- */}
      <BookReturnModal
        order={returning}
        onClose={() => setReturning(null)}
        onDone={afterDecision}
      />
    </div>
  )
}
