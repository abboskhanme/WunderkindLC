import { useEffect, useState } from 'react'
import {
  getRatingSettings,
  saveRatingSettings,
  type RatingSettings,
  type RatingTopBottomScope,
} from '@/api/services/rating'
import { apiErrorMessage, cn } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { NumberInput } from '@/components/ui/NumberInput'
import { Loader } from '@/components/ui/Loader'

/** Forma: son maydonlari BO'SH bo'la oladi (`null`) — number-inputs.md. */
interface Form {
  testWeight: number | null
  topBottomCount: number | null
  topBottomScope: RatingTopBottomScope
}

const SCOPES: { key: RatingTopBottomScope; label: string; hint: string }[] = [
  { key: 'group', label: 'Har guruh', hint: "Har guruhda alohida TOP/BOTTOM" },
  { key: 'centre', label: 'Butun markaz', hint: 'Markaz bo\'yicha bitta ro\'yxat' },
]

/**
 * «Reyting sozlamalari» (Sozlamalar → O'quv → Reyting; `classes.rating` tahrir). Testlar ulushi —
 * reyting balidagi testlar o'rtachasining ulushi, qolgani (100 − x) — guruhdagi eng yaxshi ballga nisbatan
 * ball. Saqlash tarixga qiymatlari bilan yoziladi (server). Xato bo'lsa oyna YOPILMAYDI.
 */
export function RatingSettingsModal({ onClose, onSaved }: { onClose: () => void; onSaved?: (s: RatingSettings) => void }) {
  const [form, setForm] = useState<Form | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    getRatingSettings()
      .then((s) => setForm({ testWeight: s.testWeight, topBottomCount: s.topBottomCount, topBottomScope: s.topBottomScope }))
      .catch((err) => setError(apiErrorMessage(err, "Sozlamalarni yuklab bo'lmadi")))
  }, [])

  const submit = async () => {
    if (!form) return
    if (form.testWeight === null || form.testWeight < 0 || form.testWeight > 100) {
      setError('Testlar ulushini kiriting (0–100)')
      return
    }
    if (form.topBottomCount === null || form.topBottomCount < 1 || form.topBottomCount > 10) {
      setError('TOP/BOTTOM sonini kiriting (1–10)')
      return
    }
    setSaving(true)
    setError('')
    try {
      const saved = await saveRatingSettings({
        testWeight: form.testWeight,
        topBottomCount: form.topBottomCount,
        topBottomScope: form.topBottomScope,
      })
      onSaved?.(saved)
      onClose()
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
      title="Reyting sozlamalari"
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
        error ? (
          <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>
        ) : (
          <Loader />
        )
      ) : (
        <div className="space-y-3">
          <NumberInput
            label="Testlar ulushi, %"
            value={form.testWeight}
            onChange={(v) => setForm({ ...form, testWeight: v === null ? null : Math.min(v, 100) })}
          />
          <p className="text-xs text-slate-500">
            Ball ulushi: <b>{form.testWeight === null ? '—' : `${100 - form.testWeight}%`}</b>. Reyting bali = testlar
            o'rtacha foizi × ulush + ball (guruhdagi eng yaxshi ballga nisbatan, 0–100) × qolgan ulush.
          </p>

          <NumberInput
            label="TOP/BOTTOM soni"
            value={form.topBottomCount}
            onChange={(v) => setForm({ ...form, topBottomCount: v === null ? null : Math.min(v, 10) })}
          />
          <p className="text-xs text-slate-500">
            Oylik TOP/BOTTOM xabarlarida har ro'yxatga nechta o'quvchi tushadi (kichik guruhda — yarmidan ko'p emas).
          </p>

          <div>
            <div className="mb-1.5 text-sm font-medium text-slate-700">Qamrov</div>
            <div className="flex gap-2">
              {SCOPES.map((s) => (
                <button
                  key={s.key}
                  type="button"
                  title={s.hint}
                  onClick={() => setForm({ ...form, topBottomScope: s.key })}
                  className={cn(
                    'flex-1 rounded-lg border px-3 py-2 text-sm font-medium transition-colors',
                    form.topBottomScope === s.key
                      ? 'border-indigo-500 bg-indigo-50 text-indigo-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {s.label}
                </button>
              ))}
            </div>
          </div>

          {error && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>
          )}
        </div>
      )}
    </Modal>
  )
}
