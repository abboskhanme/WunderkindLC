import { useState } from 'react'
import { Button } from '@/components/ui/Button'
import { Textarea } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { conflictLine, type ScheduleConflictInfo } from '@/lib/scheduleConflicts'
import { apiErrorMessage } from '@/lib/utils'

/**
 * «Jadvalda to'qnashuv bor» oynasi (CRM Phase 5). Xona yoki o'qituvchi shu vaqtda band — saqlanmadi.
 * Faqat SUPERADMIN (`canForce`) «Baribir saqlash» ni ko'radi va SABAB yozishi shart (server tarixga yozadi).
 * Boshqalarga faqat «Orqaga» — forma ochiq qoladi, kiritilgan ma'lumot yo'qolmaydi.
 * `onForce` xato bilan tugasa — sabab oynada qizil satrda chiqadi, oyna yopilmaydi.
 */
export function ScheduleConflictModal({
  info,
  onClose,
  onForce,
}: {
  info: ScheduleConflictInfo | null
  onClose: () => void
  /** Sabab bilan qayta yuborish; muvaffaqiyatda chaqiruvchi oynani yopadi. */
  onForce: (reason: string) => Promise<unknown>
}) {
  const [reason, setReason] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (!info) return null

  const force = async () => {
    if (busy || !reason.trim()) return
    setBusy(true)
    setError(null)
    try {
      await onForce(reason.trim())
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Jadvalda to'qnashuv bor"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Orqaga
          </Button>
          {info.canForce && (
            <Button variant="danger" onClick={force} disabled={busy || !reason.trim()}>
              {busy ? 'Saqlanmoqda...' : 'Baribir saqlash'}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="font-medium text-red-700">{info.message}</p>
        {info.conflicts.length > 0 && (
          <ul className="space-y-1 rounded-lg bg-red-50 px-3 py-2 text-slate-700">
            {info.conflicts.map((c) => (
              <li key={`${c.groupId}:${c.reason}`}>{conflictLine(c)}</li>
            ))}
          </ul>
        )}
        {info.canForce ? (
          <Textarea
            label="Sabab"
            required
            rows={2}
            maxLength={500}
            placeholder="Nega ikki dars bir vaqtda bo'lishi kerak?"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
          />
        ) : (
          <p className="text-slate-500">
            Vaqt, xona yoki o'qituvchini o'zgartirish uchun <b>Orqaga</b> bosing — kiritilgan ma'lumot saqlanib qoladi.
          </p>
        )}
        {error && <p className="text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
