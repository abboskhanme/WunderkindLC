import { useEffect, useState } from 'react'
import { Archive, AlertTriangle } from 'lucide-react'
import type { ActionReason } from '@/types'
import { getActionReasons } from '@/api/services/actionReasons'
import { closeClass, type CloseGroupResult } from '@/api/services/classes'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { apiErrorMessage } from '@/lib/utils'

/** "YYYY-MM-DD" bugungi sana */
const today = () => new Date().toISOString().slice(0, 10)

/**
 * GURUHNI YOPISH modali — sertifikatsiz "arxivga olish":
 * tanlangan sanadan guruhning BARCHA a'zolari muzlatiladi (qarzdorlik shu sanagacha hisoblanadi)
 * va guruh arxivga (faol emas) o'tadi. To'lov keyin ham qabul qilinaveradi.
 */
export function CloseGroupModal({
  open,
  onClose,
  groupId,
  groupName,
  activeMembers,
  onSuccess,
}: {
  open: boolean
  onClose: () => void
  groupId: string
  groupName: string
  /** Guruhdagi faol a'zolar soni — ogohlantirishda ko'rsatiladi. */
  activeMembers: number
  onSuccess?: (result: CloseGroupResult) => void
}) {
  const [date, setDate] = useState(today())
  const [reasons, setReasons] = useState<ActionReason[]>([])
  const [reasonId, setReasonId] = useState<string>('')
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tiklash (maqsadli)
    setDate(today())
    setReasonId('')
    setError(null)
    getActionReasons()
      .then((all) => setReasons(all.filter((r) => r.category === 'freeze')))
      .catch(() => setReasons([]))
  }, [open])

  const handleSubmit = async () => {
    if (loading || !date) return
    setLoading(true)
    setError(null)
    try {
      const result = await closeClass(groupId, { date, reasonId: reasonId || undefined })
      onSuccess?.(result)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Guruhni yopib bo'lmadi"))
    } finally {
      setLoading(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title="Guruhni yopish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={loading}>
            Bekor qilish
          </Button>
          <Button onClick={handleSubmit} disabled={loading || !date}>
            <Archive className="h-4 w-4" /> {loading ? 'Yopilmoqda...' : 'Yopish'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
          <p className="font-semibold text-slate-700">{groupName}</p>
          <p className="mt-0.5 text-slate-500">{activeMembers} ta faol a'zo</p>
        </div>

        <div>
          <Input
            label="Muzlatish sanasi"
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
          />
          <p className="mt-1 text-xs text-slate-400">
            Qarzdorlik AYNAN shu sanagacha hisoblanadi — shu kundan keyin oylik to'lov yozilmaydi.
          </p>
        </div>

        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">Sabab (ixtiyoriy)</label>
          <select
            value={reasonId}
            onChange={(e) => setReasonId(e.target.value)}
            className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          >
            <option value="">— Tanlanmagan —</option>
            {reasons.map((r) => (
              <option key={r.id} value={r.id}>
                {r.label}
              </option>
            ))}
          </select>
        </div>

        <div className="rounded-lg bg-sky-50 px-3 py-2.5 text-xs leading-relaxed text-sky-800">
          <p className="mb-1 font-semibold">Nima bo'ladi?</p>
          <ul className="ml-4 list-disc space-y-0.5">
            <li>Guruhning barcha faol a'zolari shu sanadan muzlatiladi</li>
            <li>Qarzdorlik shu sanagacha hisoblanadi (keyingi oylar bekor qilinadi)</li>
            <li>Sinovdagi a'zoliklar yakunlanadi (ularda hisob ochilmagan)</li>
            <li>Guruh arxivga o'tadi — faol guruhlar ro'yxatida ko'rinmaydi</li>
            <li>O'quvchilar arxivlanmaydi, muzlatilgan guruhga to'lov qilish mumkin</li>
          </ul>
        </div>

        <p className="flex items-start gap-1.5 text-xs text-amber-700">
          <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          Sertifikat berilmaydi va yangi guruh ochilmaydi — buning uchun "Tugatish (sertifikat bilan)".
        </p>

        {error && (
          <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
        )}
      </div>
    </Modal>
  )
}
