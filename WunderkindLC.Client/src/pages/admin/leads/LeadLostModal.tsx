import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import type { ActionReason } from '@/types'
import { getActionReasons } from '@/api/services/actionReasons'
import { LEAD_LOST_REASON_CATEGORY } from '@/api/services/leadLoss'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { apiErrorMessage, cn } from '@/lib/utils'

interface Props {
  open: boolean
  /** Oyna sarlavhasidagi lid ismi */
  leadName: string
  onClose: () => void
  /**
   * Tasdiqlash — so'rovni CHAQIRUVCHI yuboradi. Promise rad etilsa xato SHU OYNADA ko'rsatiladi
   * va oyna yopilmaydi (tanlangan sabab va yozilgan izoh joyida qoladi — `error-visibility.md` §2).
   */
  onConfirm: (reasonId: string, comment: string) => Promise<unknown>
}

/**
 * «YO'QOTILGAN» deb belgilash oynasi. Sabab MAJBURIY (katalog: O'quv bo'limi → Sabablar →
 * «Lid yo'qotilganda»), izoh ixtiyoriy.
 *
 * ⚠️ Mavjud `ReasonPromptModal` ATAYIN ishlatilmadi: u yerda sabab IXTIYORIY ("tanlanmasa
 * to'g'ridan-to'g'ri davom etadi"), bu yerda esa sababsiz yo'qotish hisobotni ma'nosiz qilardi.
 * Kanbanda karta "Yo'qotilgan" ustuniga tashlanganda ham AYNAN shu oyna ochiladi — holat hech
 * qachon jimgina o'zgarmaydi.
 */
export function LeadLostModal({ open, leadName, onClose, onConfirm }: Props) {
  const [reasons, setReasons] = useState<ActionReason[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [selected, setSelected] = useState('')
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  /** SINXRON qulf — tez ikkinchi bosish ikkinchi so'rovni yubormasin. */
  const inFlight = useRef(false)

  useEffect(() => {
    if (!open) return
    let cancelled = false
    getActionReasons()
      .then((all) => {
        if (!cancelled) setReasons(all.filter((r) => r.category === LEAD_LOST_REASON_CATEGORY))
      })
      .catch((err) => {
        if (!cancelled) setLoadError(apiErrorMessage(err, "Sabablarni yuklab bo'lmadi"))
      })
    return () => {
      cancelled = true
    }
  }, [open])

  const submit = async () => {
    if (inFlight.current) return
    if (!selected) {
      setError('Yo\'qotish sababini tanlang')
      return
    }
    inFlight.current = true
    setSaving(true)
    setError(null)
    try {
      await onConfirm(selected, comment.trim())
    } catch (err) {
      setError(apiErrorMessage(err, "Lidni yo'qotilgan deb belgilab bo'lmadi"))
    } finally {
      inFlight.current = false
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Yo'qotilgan deb belgilash"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <button
            type="button"
            disabled={saving || !reasons?.length}
            onClick={() => void submit()}
            className="rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-rose-700 disabled:opacity-50"
          >
            {saving ? 'Saqlanmoqda...' : "Yo'qotilganlarga o'tkazish"}
          </button>
        </>
      }
    >
      <div className="space-y-3">
        <p className="text-sm text-slate-600">
          <b className="text-slate-800">{leadName || 'Lid'}</b> faol ishdan chiqadi va «Yo'qotilgan» ustuniga
          o'tadi. Keyin istalgan payt tiklash mumkin.
        </p>

        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-600">
            Sabab <span className="text-red-500">*</span>
          </span>
          {loadError ? (
            <p className="text-sm text-red-600">{loadError}</p>
          ) : reasons === null ? (
            <p className="text-xs text-slate-400">Yuklanmoqda...</p>
          ) : reasons.length === 0 ? (
            <p className="text-xs text-slate-500">
              Sabablar ro'yxati bo'sh.{' '}
              <Link to="/admin/reasons" className="text-brand-600 hover:underline">
                Sabablar
              </Link>{' '}
              bo'limida «Lid yo'qotilganda» kartochkasiga sabab qo'shing.
            </p>
          ) : (
            <div className="flex flex-wrap gap-1.5">
              {reasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  aria-pressed={selected === r.id}
                  onClick={() => {
                    setSelected(r.id)
                    setError(null)
                  }}
                  className={cn(
                    'rounded-full border px-3 py-1.5 text-sm transition-colors',
                    selected === r.id
                      ? 'border-rose-500 bg-rose-50 font-medium text-rose-700'
                      : 'border-slate-200 text-slate-600 hover:border-rose-300 hover:bg-slate-50',
                  )}
                >
                  {r.label}
                </button>
              ))}
            </div>
          )}
        </div>

        <Textarea
          label="Izoh (ixtiyoriy)"
          rows={3}
          maxLength={1000}
          placeholder="Mijoz nima dedi?"
          value={comment}
          onChange={(e) => setComment(e.target.value)}
        />

        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-[13px] text-red-700">{error}</p>
        )}
      </div>
    </Modal>
  )
}
