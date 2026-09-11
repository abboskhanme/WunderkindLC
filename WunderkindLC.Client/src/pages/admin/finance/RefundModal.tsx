import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Undo2, Snowflake } from 'lucide-react'
import type { FinanceTransaction } from '@/types'
import { refundPayment } from '@/api/services/finance'
import { getStudentLedger } from '@/api/services/students'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Textarea } from '@/components/ui/Input'
import { apiErrorMessage, formatMoney } from '@/lib/utils'
import posthog from '@/lib/posthog'

interface Props {
  /** Vozvrat qilinadigan o'quvchi to'lovi (income + tuition) */
  payment: FinanceTransaction | null
  onClose: () => void
  onSaved: () => void
}

const today = () => new Date().toISOString().slice(0, 10)

/**
 * O'quvchi to'lovidan pul qaytarish (VOZVRAT) — FAQAT superadmin.
 *
 * Muzlatish bilan bog'liq: o'quvchi oy o'rtasida MUZLATILGANDA shu oy hisobi qatnashilgan darslarga qayta
 * hisoblanadi va o'quvchida AVANS (ortiqcha to'lov) paydo bo'ladi — shu avans qaytariladi, balans 0 ga tushadi.
 * Server: alohida vozvrat yozuvi (kassa chiqimi), balans −summa, o'qituvchi foizi net'dan qayta hisoblanadi.
 */
export function RefundModal({ payment, onClose, onSaved }: Props) {
  const [amount, setAmount] = useState(0)
  const [date, setDate] = useState(today())
  const [reason, setReason] = useState('')
  const [advance, setAdvance] = useState<number | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const alreadyRefunded = payment?.refunded ?? 0
  const refundable = payment ? payment.amount - alreadyRefunded : 0

  useEffect(() => {
    if (!payment) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani to'lov bilan sinxronlash (maqsadli)
    setError(null)
    setDate(today())
    setReason('')
    setAdvance(null)
    const sid = payment.studentId
    if (!sid) {
      setAmount(refundable)
      return
    }
    // O'quvchining joriy avansi (musbat balans) = muzlatishdan hosil bo'lgan qaytariladigan summa — taklif qilamiz.
    getStudentLedger(sid)
      .then((l) => {
        const adv = l.balance > 0 ? l.balance : 0
        setAdvance(adv)
        // Taklif: min(avans, qaytarish mumkin bo'lgan qoldiq). Avans 0 bo'lsa — qoldiqni taklif qilamiz.
        setAmount(Math.min(adv > 0 ? adv : refundable, refundable))
      })
      .catch(() => setAmount(refundable))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [payment])

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!payment || amount <= 0 || amount > refundable) return
    setSaving(true)
    setError(null)
    try {
      await refundPayment(payment.id, { amount, date, reason: reason.trim() || undefined })
      posthog.capture('payment_refunded', { is_partial_refund: amount < refundable })
      onSaved()
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Vozvratni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={!!payment}
      onClose={onClose}
      title="Pul qaytarish (vozvrat)"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button
            type="submit"
            form="refund-form"
            disabled={saving || amount <= 0 || amount > refundable}
          >
            {saving ? 'Saqlanmoqda...' : 'Qaytarish'}
          </Button>
        </>
      }
    >
      {!payment ? null : (
        <form id="refund-form" onSubmit={submit} className="space-y-4">
          {/* O'quvchi + asl to'lov */}
          <div className="rounded-lg bg-slate-50 px-3 py-2.5">
            <p className="text-xs text-slate-400">O'quvchi</p>
            <p className="font-semibold text-slate-800">{payment.studentName ?? '—'}</p>
            <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
              <span>
                Asl to'lov: <b className="font-mono text-slate-700">{formatMoney(payment.amount)}</b>
              </span>
              {alreadyRefunded > 0 && (
                <span>
                  Qaytarilgan: <b className="font-mono text-amber-600">{formatMoney(alreadyRefunded)}</b>
                </span>
              )}
              <span>
                Qaytarish mumkin: <b className="font-mono text-slate-700">{formatMoney(refundable)}</b>
              </span>
            </div>
          </div>

          {/* Muzlatishdan hosil bo'lgan avans — taklif qilinadigan summa */}
          {advance !== null && advance > 0 && (
            <div className="flex items-start gap-2.5 rounded-lg bg-sky-50 px-3 py-2.5 text-xs text-sky-800">
              <Snowflake className="mt-0.5 h-4 w-4 shrink-0" />
              <p>
                O'quvchida <b className="font-mono">{formatMoney(advance)}</b> avans bor (odatda muzlatishdan).
                Shu summani qaytarsangiz — balans <b>0</b> ga tushadi.
              </p>
            </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <Input
              label="Qaytariladigan summa (so'm)"
              type="number"
              min={0}
              max={refundable}
              step="any"
              required
              value={amount}
              onChange={(e) => setAmount(Number(e.target.value))}
            />
            <Input
              label="Sana"
              type="date"
              required
              max={today()}
              value={date}
              onChange={(e) => setDate(e.target.value)}
            />
          </div>

          {amount > refundable && (
            <p className="text-xs font-medium text-red-600">
              Ko'pi bilan {formatMoney(refundable)} so'm qaytarish mumkin.
            </p>
          )}

          <Textarea
            label="Sabab (ixtiyoriy)"
            rows={2}
            placeholder="Masalan: kelmagani uchun qaytarildi"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />

          <div className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
            <Undo2 className="mt-0.5 h-4 w-4 shrink-0" />
            <p>
              O'quvchi balansi <b className="font-mono">−{formatMoney(amount || 0)}</b> ga o'zgaradi.
              Vozvrat "Vozvratlar" tarixida ko'rinadi va o'qituvchining foizli maoshi qolgan (qaytarilmagan)
              summadan qayta hisoblanadi.
            </p>
          </div>

          {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        </form>
      )}
    </Modal>
  )
}
