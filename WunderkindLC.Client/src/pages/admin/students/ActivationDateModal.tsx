import { useState } from 'react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { changeActivationDate, type ActivationDateResult } from '@/api/services/classes'
import { formatMonth } from '@/config/constants'
import { localIsoDate } from '@/lib/localDate'
import { apiErrorMessage, cn, formatDate, formatMoney } from '@/lib/utils'

interface Props {
  studentId: string
  groupId: string
  groupName: string
  /** Hozirgi aktivlashtirish sanasi "yyyy-MM-dd". */
  activatedAt: string
  onClose: () => void
  /** Saqlangach — profil balansi va guruhlar qayta yuklanadi. */
  onDone: () => void
}

/**
 * «Aktivlashtirish sanasini o'zgartirish» — sanasi xato yoki kech kiritilgan o'quvchi (masalan 8-sanadan
 * qisman hisoblangan, aslida 1-sanadan o'qiyapti). Qoida serverda (`ActivationDateChange`).
 *
 * ⚠️ Ikki qadam ATAYIN: avval «Hisoblash» (server `preview` — hech narsa saqlanmaydi) qaysi oy qanchaga
 * o'zgarishini va yangi balansni ko'rsatadi, «Saqlash» faqat o'sha sana uchun ochiladi. Pul jimgina
 * o'zgarmasin. Ochilganda `key` bilan qayta yaratiladi — boshlang'ich qiymatlar initializer'da.
 */
export function ActivationDateModal({ studentId, groupId, groupName, activatedAt, onClose, onDone }: Props) {
  const today = localIsoDate(new Date())
  const [date, setDate] = useState(activatedAt.slice(0, 7) + '-01' <= today ? activatedAt.slice(0, 7) + '-01' : today)
  const [preview, setPreview] = useState<ActivationDateResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const run = async (save: boolean) => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const r = await changeActivationDate(groupId, studentId, date, !save)
      if (save) {
        onDone()
        return
      }
      setPreview(r)
    } catch (e) {
      setError(apiErrorMessage(e, save ? "Saqlab bo'lmadi" : "Hisoblab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const ready = preview?.newDate === date

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      title="Aktivlashtirish sanasini o'zgartirish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          {ready ? (
            <Button onClick={() => run(true)} disabled={busy}>
              {busy ? 'Saqlanmoqda...' : 'Saqlash'}
            </Button>
          ) : (
            <Button onClick={() => run(false)} disabled={busy || !date}>
              {busy ? 'Hisoblanmoqda...' : 'Hisoblash'}
            </Button>
          )}
        </>
      }
    >
      <div className="space-y-3 text-sm">
        <p className="text-slate-600">
          <b>{groupName}</b> — hozir <b>{formatDate(activatedAt)}</b> dan aktiv. Sana o'zgarsa shu oyning
          hisobi qolgan darslar bo'yicha qayta hisoblanadi; oldingi oyga surilsa, keyingi oylar to'liq oylik bo'ladi.
        </p>
        <Input
          label="Yangi aktivlashtirish sanasi"
          type="date"
          value={date}
          max={today}
          onChange={(e) => {
            setDate(e.target.value)
            setError('')
          }}
        />
        {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-red-700">{error}</p>}
        {preview && ready && (
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
            {preview.months.length === 0 ? (
              <p className="text-slate-600">Hisob o'zgarmaydi — faqat sana yangilanadi.</p>
            ) : (
              <table className="w-full text-left">
                <thead className="text-xs text-slate-500">
                  <tr>
                    <th className="py-1 font-medium">Oy</th>
                    <th className="py-1 text-right font-medium">Hozir</th>
                    <th className="py-1 text-right font-medium">Yangi</th>
                  </tr>
                </thead>
                <tbody>
                  {preview.months.map((m) => (
                    <tr key={m.month} className="border-t border-slate-200">
                      <td className="py-1">{formatMonth(m.month)}</td>
                      <td className="py-1 text-right">{m.before === null ? '—' : formatMoney(m.before)}</td>
                      <td className="py-1 text-right font-medium">{m.after === null ? '—' : formatMoney(m.after)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
            <p className="mt-2 border-t border-slate-200 pt-2">
              Balans: {formatMoney(preview.balanceBefore)} →{' '}
              <b className={cn(preview.balanceAfter < 0 ? 'text-red-600' : 'text-emerald-700')}>
                {formatMoney(preview.balanceAfter)}
              </b>
            </p>
            {preview.lockedMonths.length > 0 && (
              <p className="mt-2 text-amber-700">
                {preview.lockedMonths.map(formatMonth).join(', ')} hisobi qo'lda tahrirlangan — u o'zgarmaydi
                (kerak bo'lsa «Balansni tuzatish» orqali).
              </p>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
