import { useEffect, useState } from 'react'
import { AlertTriangle, Loader2, Undo2 } from 'lucide-react'
import type { BookOrder, BookPaymentMethod } from '@/api/services/books'
import { returnBookOrder } from '@/api/services/books'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { paymentLabel } from './bookLabels'

/**
 * Qaytarish oynasiga kerak bo'ladigan MINIMAL ma'lumot. Ataylab `BookOrder` emas: bir xil oyna
 * "Buyurtmalar", "Nasiya" va "Analitika → sotuvlar lentasi" dan ochiladi, lenta qatorida esa
 * (`BookSaleRow`) buyurtmaning hamma maydoni yo'q.
 */
export interface BookReturnTarget {
  id: string
  number: number
  bookTitle: string
  customerName: string
  /** Sotilgan (xom) dona */
  qty: number
  /** Allaqachon qaytarilgan dona */
  returnedQty: number
  /** Bir dona sotuv narxi — qaytariladigan summa shundan hisoblanadi */
  unitPrice: number
  paymentMethod: BookPaymentMethod
  /** Pul mijozdan olinganmi (to'lanmagan nasiyada `false`) */
  isPaid: boolean
}

interface Props {
  /** `null` — oyna yopiq */
  order: BookReturnTarget | null
  onClose: () => void
  /** Qaytarish bajarilgach — chaqiruvchi ro'yxatni/belgilarni yangilaydi */
  onDone: (updated: BookOrder) => void
}

const REASONS = ['Mijoz fikridan qaytdi', 'Kitob yaroqsiz', 'Xato sotildi', 'Ortiqcha olingan']

/**
 * SOTILGAN KITOBNI QAYTARISH (vozvrat) — naqd, karta va nasiya sotuvlari uchun bitta oyna.
 *
 * Qaytarilgan dona OMBORGA qaytadi va sotuv summasidan o'sha qismi AYIRILADI (tushum, kunlik
 * grafik, kitob kesimi va qarz — hammasi sof qiymat bilan ishlaydi).
 *
 * ⚠️ Pul faqat ALLAQACHON OLINGAN bo'lsa qaytariladi: to'lanmagan nasiyada kassadan hech narsa
 * chiqmaydi — shunchaki qarz kamayadi. Oyna buni ochiq yozib turadi, chunki kassir uchun
 * "pulni qaytaraymi yoki yo'qmi" eng muhim savol.
 */
export function BookReturnModal({ order, onClose, onDone }: Props) {
  const left = order ? Math.max(0, order.qty - order.returnedQty) : 0
  const [qty, setQty] = useState(1)
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  // Oyna har ochilganda toza holat: default — QOLGANINING HAMMASI (eng ko'p uchraydigan holat
  // "butun sotuvni qaytarish"; qisman qaytarishda kassir sonni kamaytiradi).
  useEffect(() => {
    if (!order) return
    setQty(Math.max(1, left))
    setReason('')
    setError('')
  }, [order, left])

  if (!order) return null

  const refund = order.isPaid ? order.unitPrice * qty : 0
  const invalid = qty < 1 || qty > left

  const confirm = async () => {
    if (busy || invalid) return
    setBusy(true)
    setError('')
    try {
      const updated = await returnBookOrder(order.id, { qty, reason: reason.trim() || undefined })
      onDone(updated)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Qaytarib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={busy ? () => {} : onClose}
      size="sm"
      title={`Sotuv #${order.number} — kitobni qaytarish`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={confirm} disabled={busy || invalid}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Undo2 className="h-4 w-4" />}
            Qaytarish
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2.5 text-sm text-slate-600">
          <b className="text-slate-800">{order.customerName || "Noma'lum"}</b>
          <div>
            {order.bookTitle} — {order.qty} dona ·{' '}
            <b className="text-slate-800">{formatMoney(order.unitPrice)} so'm</b> / dona ·{' '}
            {paymentLabel(order.paymentMethod)}
          </div>
          {order.returnedQty > 0 && (
            <div className="mt-0.5 text-xs text-amber-700">
              Allaqachon qaytarilgan: {order.returnedQty} dona — yana {left} dona qaytarish mumkin.
            </div>
          )}
        </div>

        <div className="flex items-end gap-2">
          <Input
            label="Nechta qaytariladi"
            type="number"
            min={1}
            max={left}
            required
            className="w-40"
            value={qty}
            onChange={(e) => setQty(Number(e.target.value))}
          />
          {left > 1 && (
            <Button variant="secondary" type="button" onClick={() => setQty(left)}>
              Hammasi ({left})
            </Button>
          )}
        </div>
        {invalid && (
          <p className="text-xs font-medium text-red-600">
            1 dan {left} gacha son kiriting (bu sotuvdan {left} dona qaytarish mumkin).
          </p>
        )}

        {/* Kassir uchun asosiy savol: pul qaytadimi yoki qarz kamayadimi. */}
        <div
          className={cn(
            'rounded-lg px-3 py-2.5 text-sm',
            refund > 0 ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-800',
          )}
        >
          {refund > 0 ? (
            <>
              Mijozga <b>{formatMoney(refund)} so'm</b> qaytariladi (
              {paymentLabel(order.paymentMethod)} bilan to'langan edi).
            </>
          ) : order.paymentMethod === 'credit' ? (
            <>
              Pul olinmagan (nasiya) — kassadan hech narsa chiqmaydi, faqat{' '}
              <b>qarz {formatMoney(order.unitPrice * qty)} so'mga kamayadi</b>.
            </>
          ) : (
            <>Pul olinmagan — qaytariladigan summa yo'q.</>
          )}
          <div className="mt-1 text-xs opacity-80">
            Ombor qoldig'iga +{qty || 0} dona qo'shiladi va sotuv hisobotidan shu qism ayiriladi.
          </div>
        </div>

        <Textarea
          label="Sabab (ixtiyoriy)"
          rows={2}
          placeholder="Masalan: Kitob yaroqsiz chiqdi"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
        />
        <div className="flex flex-wrap gap-1.5">
          {REASONS.map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => setReason(r)}
              className="rounded-full border border-slate-200 px-2.5 py-1 text-xs text-slate-600 hover:border-brand-300 hover:bg-brand-50"
            >
              {r}
            </button>
          ))}
        </div>

        {error && (
          <div className="flex items-center gap-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">
            <AlertTriangle className="h-4 w-4 shrink-0" /> {error}
          </div>
        )}
      </div>
    </Modal>
  )
}
