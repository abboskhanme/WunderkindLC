import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { ArrowRight, Ban } from 'lucide-react'
import {
  createCashboxTransfer,
  getCashboxTransfers,
  voidCashboxTransfer,
  type Cashbox,
  type CashboxTransfer,
} from '@/api/services/cashboxes'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { MoneyInput } from '@/components/ui/NumberInput'
import { RightDrawer, DrawerActions, DrawerField, DrawerInput, DrawerSelect } from '@/components/ui/RightDrawer'
import { apiErrorMessage, cn, formatDate, formatMoney } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import { newRequestId } from '@/lib/requestId'
import { toast } from '@/lib/toast'

/**
 * KASSADAN KASSAGA KO'CHIRISH oynasi (FAQAT superadmin — server ham 403 qaytaradi).
 *
 * - Ko'chirish kirim/chiqim EMAS: hisobotlarga tushmaydi, faqat ikki kassaning qoldig'i o'zgaradi.
 * - Kassa MINUSGA tushmaydi: qoldiqdan ko'p summa kiritilsa ogohlantirish chiqadi, server esa rad etadi.
 * - Arxivlangan kassa pul QABUL QILMAYDI, lekin undan qoldiqni chiqarish mumkin (shu yo'l bilan bo'shatiladi).
 * - Xato forma ichida chiqadi, kiritilgan qiymatlar qoladi (`error-visibility.md`).
 */
export function CashboxTransferForm({
  boxes,
  initialFromId,
  onClose,
  onSaved,
}: {
  boxes: Cashbox[]
  initialFromId?: string | null
  onClose: () => void
  onSaved: () => void
}) {
  const today = todayIso()
  // Qayerdan: faol kassalar + qoldig'i bor arxivdagi kassalar (ularni bo'shatish uchun).
  const sources = boxes.filter((b) => !b.isArchived || b.balance > 0)
  const [fromId, setFromId] = useState(initialFromId ?? sources[0]?.id ?? '')
  const targets = boxes.filter((b) => !b.isArchived && b.id !== fromId)
  const [toPick, setToPick] = useState('')
  // Tanlangan "qayerga" endi mos bo'lmasa (qayerdan bilan bir xil bo'lib qolsa) — asosiy kassa yoki birinchisi.
  const toId = targets.some((b) => b.id === toPick)
    ? toPick
    : (targets.find((b) => b.isMain)?.id ?? targets[0]?.id ?? '')
  const [amount, setAmount] = useState<number | null>(null)
  const [date, setDate] = useState(today)
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const inFlight = useRef(false)
  // Oyna kaliti: shu oynadagi har urinishda bir xil — javobi kelmagan ko'chirish qayta yozilmaydi.
  const [requestId] = useState(newRequestId)

  const from = boxes.find((b) => b.id === fromId)
  const over = !!from && amount !== null && amount > from.balance

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (inFlight.current) return
    if (!fromId || !toId) return setError('Ikkala kassani tanlang')
    if (!amount || amount <= 0) return setError('Summani kiriting')
    inFlight.current = true
    setSaving(true)
    setError(null)
    try {
      await createCashboxTransfer({
        fromCashboxId: fromId,
        toCashboxId: toId,
        amount,
        date: date || undefined,
        note: note.trim() || undefined,
        requestId,
      })
      toast.success("Pul ko'chirildi", formatMoney(amount))
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Ko'chirib bo'lmadi"))
    } finally {
      inFlight.current = false
      setSaving(false)
    }
  }

  const label = (b: Cashbox) => `${b.name}${b.isArchived ? ' (arxiv)' : ''} — ${formatMoney(b.balance)}`

  return (
    <RightDrawer
      open
      onClose={onClose}
      title="Kassadan kassaga ko'chirish"
      footer={
        <DrawerActions onBack={onClose}>
          <Button type="submit" form="cashbox-transfer-form" disabled={saving || !fromId || !toId || !amount}>
            {saving ? 'Saqlanmoqda...' : "Ko'chirish"}
          </Button>
        </DrawerActions>
      }
    >
      <form id="cashbox-transfer-form" onSubmit={(e) => void submit(e)} className="space-y-4">
        {error && <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
        <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-600">
          Ko'chirish kirim ham, chiqim ham emas — hisobotlarga tushmaydi, faqat ikki kassaning qoldig'i o'zgaradi.
        </p>
        <DrawerField label="Qaysi kassadan" required>
          <DrawerSelect value={fromId} onChange={(e) => setFromId(e.target.value)}>
            {sources.length === 0 && <option value="">Kassa yo'q</option>}
            {sources.map((b) => (
              <option key={b.id} value={b.id}>
                {label(b)}
              </option>
            ))}
          </DrawerSelect>
        </DrawerField>
        <DrawerField label="Qaysi kassaga" required hint="Arxivlangan kassa pul qabul qilmaydi.">
          <DrawerSelect value={toId} onChange={(e) => setToPick(e.target.value)}>
            {targets.length === 0 && <option value="">Boshqa faol kassa yo'q</option>}
            {targets.map((b) => (
              <option key={b.id} value={b.id}>
                {label(b)}
              </option>
            ))}
          </DrawerSelect>
        </DrawerField>
        <DrawerField label="Summa (so'm)" required>
          <MoneyInput as={DrawerInput} required value={amount} onChange={setAmount} />
          {over && from && (
            <span className="mt-1 block text-xs text-red-600">
              «{from.name}» kassasida faqat {formatMoney(from.balance)} bor — kassa minusga tushmaydi.
            </span>
          )}
        </DrawerField>
        <DrawerField label="Sana" hint="Pul haqiqatan o'tkazilgan kun (kelajak sana — yo'q).">
          <DrawerInput type="date" value={date} max={today} onChange={(e) => setDate(e.target.value)} />
        </DrawerField>
        <DrawerField label="Izoh">
          <DrawerInput value={note} maxLength={500} placeholder="Masalan: kun yakuni — naqd topshirildi"
            onChange={(e) => setNote(e.target.value)} />
        </DrawerField>
      </form>
    </RightDrawer>
  )
}

/**
 * KO'CHIRISHLAR TARIXI (tanlangan davr). Bekor qilingani ustiga chizilgan holda qoladi.
 * Bekor qilish — FAQAT superadmin; qabul qilgan kassa pulni ishlatib bo'lgan bo'lsa server rad etadi
 * (sabab `alert` bilan — ro'yxat qatoridagi amal, `error-visibility.md` §2).
 */
export function CashboxTransfersCard({
  from,
  to,
  tick,
  isSuperAdmin,
  onChanged,
}: {
  from: string
  to: string
  /** Ota komponent hisoblagichi — ko'chirishdan keyin ro'yxat qayta yuklansin. */
  tick: number
  isSuperAdmin: boolean
  onChanged: () => void
}) {
  const [rows, setRows] = useState<CashboxTransfer[]>([])
  const [loadError, setLoadError] = useState<string | null>(null)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const inFlight = useRef(false)

  useEffect(() => {
    let alive = true
    getCashboxTransfers(from, to)
      .then((r) => {
        if (!alive) return
        setRows(r)
        setLoadError(null)
      })
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Ko'chirishlar tarixini yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [from, to, tick])

  const doVoid = async (t: CashboxTransfer) => {
    if (inFlight.current) return
    inFlight.current = true
    try {
      await voidCashboxTransfer(t.id, reason.trim())
      toast.success("Ko'chirish bekor qilindi")
      setConfirmId(null)
      setReason('')
      onChanged()
    } catch (e) {
      alert(apiErrorMessage(e, "Bekor qilib bo'lmadi"))
    } finally {
      inFlight.current = false
    }
  }

  const live = rows.filter((r) => !r.isVoided)

  return (
    <Card
      tight
      title="Kassadan kassaga ko'chirishlar"
      sub={`Tanlangan davr: ${live.length} ta, jami ${formatMoney(live.reduce((a, r) => a + r.amount, 0))}. Kirim/chiqim emas — hisobotlarga tushmaydi.`}
    >
      {loadError && <p className="px-4 py-3 text-sm text-red-600">{loadError}</p>}
      {!loadError && rows.length === 0 && (
        <p className="px-4 py-6 text-center text-sm text-slate-400">Bu davrda ko'chirish bo'lmagan.</p>
      )}
      {rows.length > 0 && (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Sana</th>
                <th>Qayerdan → qayerga</th>
                <th className="num">Summa</th>
                <th>Izoh</th>
                <th>Kim</th>
                {isSuperAdmin && <th className="num" />}
              </tr>
            </thead>
            <tbody>
              {rows.map((t) => (
                <tr key={t.id} className={cn(t.isVoided && 'text-slate-400 line-through')}>
                  <td className="font-mono text-[12.5px]">{formatDate(t.date)}</td>
                  <td>
                    <span className="inline-flex items-center gap-1">
                      {t.fromCashboxName} <ArrowRight className="h-3.5 w-3.5 shrink-0" /> {t.toCashboxName}
                    </span>
                    {t.isVoided && (
                      <span
                        title={`${t.voidedByName ?? ''} ${t.voidedAt ? formatDate(t.voidedAt) : ''}${t.voidReason ? ` — ${t.voidReason}` : ''}`}
                        className="ml-2 rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-semibold text-slate-500 no-underline"
                      >
                        Bekor qilindi
                      </span>
                    )}
                  </td>
                  <td className="num font-semibold">{formatMoney(t.amount)}</td>
                  <td className="text-slate-500">{t.note ?? '—'}</td>
                  <td className="text-slate-500">{t.createdByName ?? '—'}</td>
                  {isSuperAdmin && (
                    <td className="num">
                      {!t.isVoided && confirmId !== t.id && (
                        <button type="button" title="Bekor qilish (qoldiqlar tiklanadi)"
                          onClick={() => {
                            setConfirmId(t.id)
                            setReason('')
                          }}
                          className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600">
                          <Ban className="h-4 w-4" />
                        </button>
                      )}
                      {confirmId === t.id && (
                        <div className="flex items-center justify-end gap-1">
                          <input value={reason} maxLength={500} placeholder="Sabab (ixtiyoriy)"
                            onChange={(e) => setReason(e.target.value)}
                            className="h-7 w-36 rounded-md border border-slate-200 px-2 text-xs" />
                          <button type="button" onClick={() => void doVoid(t)}
                            className="rounded-md bg-red-600 px-2 py-1 text-xs font-semibold text-white">
                            Bekor qilish
                          </button>
                          <button type="button" onClick={() => setConfirmId(null)}
                            className="rounded-md px-2 py-1 text-xs font-semibold text-slate-600 hover:bg-slate-100">
                            Yo'q
                          </button>
                        </div>
                      )}
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}
