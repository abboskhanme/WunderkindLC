import { useEffect, useRef, useState } from 'react'
import { BadgePercent, X } from 'lucide-react'
import {
  applyLessonDiscount,
  cancelLessonDiscount,
  getLessonDiscounts,
  lessonDiscountLabel,
  quoteLessonDiscount,
  type LessonDiscount,
  type LessonDiscountChannel,
  type LessonDiscountQuote,
} from '@/api/services/students'
import { DrawerField, DrawerInput } from '@/components/ui/RightDrawer'
import { NumberInput } from '@/components/ui/NumberInput'
import { formatMonth } from '@/config/constants'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { toast } from '@/lib/toast'

/**
 * To'lov oynasidagi «CHEGIRMA» — KELGAN darslar bo'yicha (2026-10-15 dan; ilgari kelmagan darslar soni
 * so'ralardi). Kassir kelgan darslar sonini va sababni kiritadi; o'quvchi faqat kelgan darslari uchun
 * to'laydi: server `oy hisobi − kelgan darslar × bir dars narxi` ni (to'lanmagan qoldiq bilan cheklab)
 * ayiradi (`LessonDiscountService.ApplyAttendedAsync`, `discounts.md` §13.1).
 *
 * Tanlangan guruh + oy uchun ishlaydi; o'zgarishdan keyin `onChanged` — oyna oylar qoldig'ini
 * qayta yuklaydi. Qo'llangan chegirma shu yerda ko'rinadi va bekor qilinadi (xato bo'lsa).
 */
export function LessonDiscountPanel({
  channel,
  studentId,
  groupId,
  month,
  onChanged,
}: {
  channel: LessonDiscountChannel
  studentId: string
  groupId: string
  month: string
  /** Balans o'zgarishi bilan: qo'llansa +summa (qarz kamaydi), bekor qilinsa −summa. */
  onChanged: (balanceDelta: number) => void
}) {
  const [open, setOpen] = useState(false)
  const [attended, setAttended] = useState<number | null>(null)
  const [reason, setReason] = useState('')
  const [quote, setQuote] = useState<LessonDiscountQuote | null>(null)
  const [quoteError, setQuoteError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [list, setList] = useState<LessonDiscount[]>([])
  const [listTick, setListTick] = useState(0)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  // Sinxron qulf — tez ikki bosishda chegirma ikki marta qo'llanmasin.
  const inFlight = useRef(false)

  // Shu o'quvchining chegirmalari (fon o'qish — xato bo'lsa panel baribir ishlaydi, ro'yxat bo'sh).
  useEffect(() => {
    let alive = true
    getLessonDiscounts(channel, studentId)
      .then((l) => alive && setList(l))
      .catch(() => {
        /* ikkilamchi ro'yxat — chegirma qo'yish undan mustaqil ishlaydi */
      })
    return () => {
      alive = false
    }
  }, [channel, studentId, listTick])

  // Hisob-kitob (preview) — kelgan darslar soni o'zgarganda, kichik kechikish bilan. 0 ham to'g'ri qiymat.
  useEffect(() => {
    if (!open || attended == null || attended < 0) return
    let alive = true
    const t = setTimeout(() => {
      quoteLessonDiscount(channel, studentId, groupId, month, attended)
        .then((q) => {
          if (!alive) return
          setQuote(q)
          setQuoteError(null)
        })
        .catch((e) => {
          if (!alive) return
          setQuote(null)
          setQuoteError(apiErrorMessage(e, "Hisoblab bo'lmadi"))
        })
    }, 300)
    return () => {
      alive = false
      clearTimeout(t)
    }
  }, [open, attended, channel, studentId, groupId, month])

  const standing = list.filter((d) => !d.cancelled && d.groupId === groupId && d.month === month)
  const fresh = quote && attended != null && quote.attended === attended ? quote : null
  const canApply = !!fresh && fresh.amount > 0 && reason.trim().length > 0 && !busy

  const apply = async () => {
    if (inFlight.current || !canApply || attended == null) return
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      const d = await applyLessonDiscount(channel, studentId, { groupId, month, attended, reason: reason.trim() })
      toast.success('Chegirma qo\'llandi', `${lessonDiscountLabel(d)} — ${formatMoney(d.amount)}`)
      setOpen(false)
      setAttended(null)
      setReason('')
      setQuote(null)
      setListTick((x) => x + 1)
      onChanged(d.amount)
    } catch (e) {
      // Panel yopilmaydi, kiritilgan qiymatlar qoladi (`error-visibility.md`).
      setError(apiErrorMessage(e, "Chegirmani qo'llab bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  const cancel = async (id: string) => {
    if (inFlight.current) return
    inFlight.current = true
    setBusy(true)
    setError(null)
    try {
      const d = await cancelLessonDiscount(channel, studentId, id)
      setConfirmId(null)
      setListTick((x) => x + 1)
      onChanged(-d.amount)
    } catch (e) {
      setError(apiErrorMessage(e, "Chegirmani bekor qilib bo'lmadi"))
    } finally {
      inFlight.current = false
      setBusy(false)
    }
  }

  return (
    <div className="space-y-2">
      {/* Shu oyda qo'llangan chegirmalar — xato bo'lsa bekor qilinadi. */}
      {standing.map((d) => (
        <div key={d.id} className="rounded-lg border border-emerald-200 bg-emerald-50 px-3 py-2 text-[13px] text-emerald-800">
          <div className="flex items-start justify-between gap-2">
            <span>
              Chegirma qo'llangan: <b>{lessonDiscountLabel(d)}</b> — <b className="font-mono">{formatMoney(d.amount)}</b>
              <span className="block text-xs text-emerald-700">
                Sabab: {d.reason}
                {d.createdByName ? ` · ${d.createdByName}` : ''}
              </span>
            </span>
            {confirmId === d.id ? (
              <span className="flex shrink-0 gap-1">
                <button type="button" disabled={busy} onClick={() => void cancel(d.id)}
                  className="rounded-md bg-red-600 px-2 py-1 text-xs font-semibold text-white disabled:opacity-50">
                  Ha, bekor qilish
                </button>
                <button type="button" onClick={() => setConfirmId(null)}
                  className="rounded-md px-2 py-1 text-xs font-semibold text-emerald-800 hover:bg-emerald-100">
                  Yo'q
                </button>
              </span>
            ) : (
              <button type="button" onClick={() => setConfirmId(d.id)}
                className="shrink-0 rounded-md px-2 py-1 text-xs font-semibold text-red-600 hover:bg-red-50">
                Bekor qilish
              </button>
            )}
          </div>
        </div>
      ))}

      {/* Shu oyga chegirma bor — ikkinchisi qo'shilmaydi ("kelgan darslar" butun oy haqidagi gap), server ham rad etadi. */}
      {standing.length > 0 ? (
        <p className="px-1 text-xs text-slate-500">Chegirmani o'zgartirish uchun avval qo'llanganini bekor qiling.</p>
      ) : !open ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-lg px-1 text-[13px] font-semibold text-brand-600 hover:underline"
        >
          <BadgePercent className="h-4 w-4" /> Chegirma (kelgan darslar bo'yicha)
        </button>
      ) : (
        <div className="space-y-3 rounded-xl border border-slate-200 p-3">
          <div className="flex items-center justify-between">
            <p className="text-[13px] font-semibold text-slate-800">Chegirma — {formatMonth(month)}</p>
            <button type="button" aria-label="Yopish" onClick={() => setOpen(false)}
              className="rounded p-1 text-slate-400 hover:bg-slate-100">
              <X className="h-4 w-4" />
            </button>
          </div>
          <DrawerField label="Kelgan darslar soni" required>
            <NumberInput as={DrawerInput} value={attended} onChange={setAttended} />
          </DrawerField>
          <DrawerField label="Sabab" required>
            <DrawerInput
              value={reason}
              maxLength={500}
              placeholder="Masalan: kasal edi"
              onChange={(e) => setReason(e.target.value)}
            />
          </DrawerField>

          {quoteError && attended != null ? <p className="text-[13px] text-red-600">{quoteError}</p> : null}
          {fresh && (
            <div className={cn('space-y-0.5 rounded-lg px-3 py-2 text-[13px]', fresh.amount > 0 ? 'bg-slate-50 text-slate-700' : 'bg-amber-50 text-amber-700')}>
              <p>
                1 dars — <span className="font-mono">{formatMoney(fresh.attendedPrice)}</span> (oyda {fresh.lessonsInMonth} ta dars).
              </p>
              <p>
                Kelgan darslar puli: <span className="font-mono">{formatMoney(fresh.payable)}</span> · oy hisobi:{' '}
                <span className="font-mono">{formatMoney(fresh.monthAmount)}</span>
              </p>
              {fresh.amount > 0 ? (
                <p>
                  Ayriladi: <b className="font-mono">{formatMoney(fresh.amount)}</b>
                  {fresh.amount < fresh.monthAmount - fresh.payable && (
                    <> — to'lanmagan qoldiq ({formatMoney(fresh.remaining)}) bilan cheklandi</>
                  )}
                </p>
              ) : (
                <p>
                  {fresh.remaining <= 0
                    ? "Bu oy to'langan — chegirma qo'llanmaydi."
                    : "Kelgan darslar puli oy hisobidan kam emas — chegirma kerak emas."}
                </p>
              )}
            </div>
          )}
          {error && <p className="text-[13px] text-red-600">{error}</p>}
          <button
            type="button"
            disabled={!canApply}
            onClick={() => void apply()}
            className="w-full rounded-lg bg-brand-600 px-3 py-2 text-[13px] font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
          >
            {busy ? 'Qo\'llanmoqda…' : 'Chegirmani qo\'llash'}
          </button>
        </div>
      )}
    </div>
  )
}
