import { useEffect, useState } from 'react'
import { AlertTriangle, BellRing, Info, ShieldAlert, Wallet } from 'lucide-react'
import {
  getAttendanceSettings,
  saveAttendanceSettings,
  type AttendanceSettings,
} from '@/api/services/attendanceSettings'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { MoneyInput, NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, cn } from '@/lib/utils'
import { SettingToggle } from './SettingToggle'

/** Tahrir paytida raqam maydoni bo'sh bo'la oladi (saqlashda tekshiriladi). */
interface Form {
  disciplineEnabled: boolean
  deadlineMinutes: number | null
  reminders: string
  escalateEnabled: boolean
  fineEnabled: boolean
  fineAmount: number | null
}

const inputCls =
  'w-24 rounded-lg border border-slate-200 px-2.5 py-1.5 text-center text-sm font-semibold outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

/**
 * «Davomat intizomi» (Sozlamalar → O'quv → Jurnal va davomat) — SPEC P3.2/P3.4: o'qituvchiga dars
 * paytida eslatmalar, muddat o'tsa administratorlarga xabar va davomat o'z vaqtida olinmasa jarima.
 * Server barcha chegaralarni tekshiradi (`AttendanceSettingsRules`); xato oynada ko'rinadi, kiritilgan
 * qiymatlar saqlanadi (error-visibility.md).
 */
export function AttendanceDisciplineModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [loaded, setLoaded] = useState<AttendanceSettings | null>(null)
  const [form, setForm] = useState<Form | null>(null)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    let alive = true
    getAttendanceSettings()
      .then((s) => {
        if (!alive) return
        setLoaded(s)
        setError(null)
        setForm({
          disciplineEnabled: s.disciplineEnabled,
          deadlineMinutes: s.deadlineMinutes,
          reminders: s.reminderMinutes.join(', '),
          escalateEnabled: s.escalateEnabled,
          fineEnabled: s.fineEnabled,
          fineAmount: s.fineAmount,
        })
      })
      .catch((err) => {
        if (alive) setError(apiErrorMessage(err, "Sozlamani yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [open])

  const set = (patch: Partial<Form>) => setForm((f) => (f ? { ...f, ...patch } : f))

  const save = async () => {
    if (!form || !loaded) return
    if (form.deadlineMinutes === null) {
      setError('Davomat muddatini kiriting')
      return
    }
    const parts = form.reminders.split(',').map((x) => x.trim()).filter(Boolean)
    if (parts.some((x) => !/^\d+$/.test(x))) {
      setError("Eslatmalar daqiqada, vergul bilan yoziladi (masalan: 5, 10)")
      return
    }
    setSaving(true)
    setError(null)
    try {
      const saved = await saveAttendanceSettings({
        ...loaded,
        disciplineEnabled: form.disciplineEnabled,
        deadlineMinutes: form.deadlineMinutes,
        reminderMinutes: parts.map(Number),
        escalateEnabled: form.escalateEnabled,
        fineEnabled: form.fineEnabled,
        fineAmount: form.fineAmount ?? 0,
      })
      setLoaded(saved)
      // Ogohlantirish bo'lsa (masalan ikki marta eslatma) oyna OCHIQ qoladi — admin uni ko'rsin.
      if (saved.warnings?.length) return
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Sozlamani saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Davomat intizomi"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !form}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!form ? (
        error ? <p className="text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <SettingToggle
            icon={ShieldAlert}
            title="Davomat intizomini yoqish"
            desc="O'qituvchiga dars paytida eslatma, muddat o'tsa administratorlarga xabar va jarima."
            checked={form.disciplineEnabled}
            onChange={(v) => set({ disciplineEnabled: v })}
          />

          <div className={cn('space-y-3', !form.disciplineEnabled && 'pointer-events-none opacity-50')}>
            <div className="flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
              <span className="text-sm text-slate-600">
                Davomat dars boshlangandan keyin necha daqiqada olinishi shart
              </span>
              <NumberInput
                as="input"
                value={form.deadlineMinutes}
                onChange={(v) => set({ deadlineMinutes: v })}
                className={inputCls}
              />
              <span className="text-sm text-slate-600">daqiqa</span>
            </div>

            <div className="flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
              <BellRing className="h-4 w-4 text-slate-400" />
              <span className="text-sm text-slate-600">O'qituvchiga eslatma (daqiqa, vergul bilan)</span>
              <input
                value={form.reminders}
                onChange={(e) => set({ reminders: e.target.value })}
                placeholder="5, 10"
                inputMode="numeric"
                className={cn(inputCls, 'w-32')}
              />
            </div>

            <SettingToggle
              icon={AlertTriangle}
              title="Muddat o'tsa administratorlarga xabar"
              desc="Davomat belgilangan muddatgacha olinmasa administratorlarga Telegram va bildirishnoma ketadi."
              checked={form.escalateEnabled}
              onChange={(v) => set({ escalateEnabled: v })}
            />

            <SettingToggle
              icon={Wallet}
              title="Jarima yozilsin"
              desc="Davomat muddatgacha olinmasa o'sha kuni darsni o'tgan o'qituvchiga (o'rinbosar bo'lsa — unga) jarima yoziladi va oyligidan ushlanadi."
              checked={form.fineEnabled}
              onChange={(v) => set({ fineEnabled: v })}
            />

            {form.fineEnabled && (
              <div className="flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
                <span className="text-sm text-slate-600">Bir dars uchun jarima (so'm)</span>
                <MoneyInput
                  as="input"
                  value={form.fineAmount}
                  onChange={(v) => set({ fineAmount: v })}
                  className={cn(inputCls, 'w-36')}
                />
              </div>
            )}

            <div className="flex items-start gap-2.5 rounded-lg bg-blue-50 px-3 py-2.5 text-xs text-blue-700">
              <Info className="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <p>
                  Davomat <b>birinchi belgi</b> (keldi yoki kelmadi) qo'yilgan payt bo'yicha hisoblanadi. Kech
                  kiritilgan davomat ham jarimadan qutqarmaydi, lekin kiritish taqiqlanmaydi.
                </p>
                <p className="mt-1">
                  Jarima «Maoshni jurnalga bog'lash» ushlanmasidan <b>mustaqil</b> — ikkalasi birga qo'llanishi
                  mumkin. Bayram kunlari jarimalarni Moliya → Jarima → «Davomat jarimalari» da kun bo'yicha bekor
                  qiling.
                </p>
              </div>
            </div>
          </div>

          {loaded?.warnings?.map((w) => (
            <div key={w} className="flex items-start gap-2.5 rounded-lg bg-amber-50 px-3 py-2.5 text-xs text-amber-800">
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
              <p>{w}</p>
            </div>
          ))}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
