import { useEffect, useState } from 'react'
import {
  getRetentionSettings,
  saveRetentionSettings,
  type RetentionSettings,
} from '@/api/services/retentionBonus'
import { apiErrorMessage } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'

interface Props {
  onClose: () => void
  onSaved: () => void
}

/** Ushlab turish bonusi sozlamalari (CenterMeta): muddat, ruxsat etilgan tanaffus, standart summa. */
export function RetentionSettingsModal({ onClose, onSaved }: Props) {
  const [form, setForm] = useState<RetentionSettings | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getRetentionSettings()
      .then(setForm)
      .catch((err) => setError(apiErrorMessage(err, "Sozlamalarni yuklab bo'lmadi")))
  }, [])

  const submit = async () => {
    if (!form) return
    setSaving(true)
    setError('')
    try {
      await saveRetentionSettings(form)
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
          <Input
            label="Necha oy uzluksiz o'qishi kerak"
            type="number"
            min={1}
            max={36}
            value={form.monthsRequired}
            onChange={(e) => setForm({ ...form, monthsRequired: Number(e.target.value) || 1 })}
          />
          <Input
            label="Ruxsat etilgan tanaffus (oy)"
            type="number"
            min={0}
            max={12}
            value={form.maxGapMonths}
            onChange={(e) => setForm({ ...form, maxGapMonths: Number(e.target.value) || 0 })}
          />
          <p className="text-xs text-slate-400">
            Muzlatilgan yoki a'zoliksiz oylar sanoqni <b>to'xtatadi</b>, lekin siklni buzmaydi.
            Ketma-ket shu sondan ko'p bo'lsa — sikl uziladi. 0 = har qanday tanaffus uzadi.
          </p>
          <Input
            label="Standart bonus summasi (so'm)"
            type="number"
            min={0}
            step={1000}
            value={form.defaultAmount}
            onChange={(e) => setForm({ ...form, defaultAmount: Number(e.target.value) || 0 })}
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
