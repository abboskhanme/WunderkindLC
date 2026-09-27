import { useEffect, useState } from 'react'
import {
  getRetentionSettings,
  saveRetentionSettings,
  type RetentionSettings,
} from '@/api/services/retentionBonus'
import { apiErrorMessage } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { MoneyInput, NumberInput } from '@/components/ui/NumberInput'
import { Loader } from '@/components/ui/Loader'

/** Forma holati: maydonlar BO'SH bo'la oladi (`null`). */
type SettingsForm = { [K in keyof RetentionSettings]: RetentionSettings[K] | null }

interface Props {
  onClose: () => void
  onSaved: () => void
}

/** Ushlab turish bonusi sozlamalari (CenterMeta): muddat, ruxsat etilgan tanaffus, standart summa. */
export function RetentionSettingsModal({ onClose, onSaved }: Props) {
  const [form, setForm] = useState<SettingsForm | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getRetentionSettings()
      .then(setForm)
      .catch((err) => setError(apiErrorMessage(err, "Sozlamalarni yuklab bo'lmadi")))
  }, [])

  const submit = async () => {
    if (!form) return
    const monthsRequired = form.monthsRequired
    if (monthsRequired === null || monthsRequired < 1) {
      setError('Necha oy uzluksiz o\'qishi kerakligini kiriting (1–36)')
      return
    }
    setSaving(true)
    setError('')
    try {
      await saveRetentionSettings({
        monthsRequired,
        maxGapMonths: form.maxGapMonths ?? 0,
        defaultAmount: form.defaultAmount ?? 0,
      })
      onSaved()
    } catch (err) {
      setError(apiErrorMessage(err, "Sozlamalarni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      title="Bonus sozlamalari"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor
          </Button>
          <Button onClick={() => void submit()} disabled={saving || !form}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!form ? (
        <Loader />
      ) : (
        <div className="space-y-3">
          <NumberInput
            label="Necha oy uzluksiz o'qishi kerak"
            value={form.monthsRequired}
            onChange={(v) => setForm({ ...form, monthsRequired: v === null ? null : Math.min(v, 36) })}
          />
          <NumberInput
            label="Ruxsat etilgan tanaffus (oy)"
            value={form.maxGapMonths}
            onChange={(v) => setForm({ ...form, maxGapMonths: v === null ? null : Math.min(v, 12) })}
          />
          <p className="text-xs text-slate-400">
            Muzlatilgan yoki a'zoliksiz oylar sanoqni <b>to'xtatadi</b>, lekin siklni buzmaydi.
            Ketma-ket shu sondan ko'p bo'lsa — sikl uziladi. 0 = har qanday tanaffus uzadi.
          </p>
          <MoneyInput
            label="Standart bonus summasi (so'm)"
            value={form.defaultAmount}
            onChange={(v) => setForm({ ...form, defaultAmount: v })}
          />
          <p className="text-xs text-slate-400">
            Bonus berish oynasi shu summa bilan ochiladi — admin har safar o'zgartira oladi.
          </p>

          {error && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
              {error}
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
