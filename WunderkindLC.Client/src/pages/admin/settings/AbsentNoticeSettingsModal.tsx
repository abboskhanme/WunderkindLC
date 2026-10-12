import { useEffect, useState } from 'react'
import { Info } from 'lucide-react'
import {
  getAttendanceSettings,
  saveAttendanceSettings,
  type AbsentNoticeApproval,
  type AttendanceSettings,
} from '@/api/services/attendanceSettings'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, cn } from '@/lib/utils'

const APPROVALS: { key: AbsentNoticeApproval; title: string; desc: string }[] = [
  {
    key: 'auto',
    title: 'Avtomatik',
    desc: "«Kelmadi» xabari dars tugagach belgilangan vaqtda o'zi ketadi.",
  },
  {
    key: 'manual',
    title: 'Har birini menejer tasdiqlaydi',
    desc: 'Tasdiqlanmagan xabar yuborilmaydi va muddati o\'tib bekor bo\'ladi.',
  },
  {
    key: 'late_only',
    title: 'Faqat davomat kech olinganda tasdiqlash (tavsiya)',
    desc: "Davomat o'z vaqtida olingan bo'lsa — avtomatik; kech kiritilgan bo'lsa — menejer tasdiqlaydi.",
  },
]

/**
 * «Davomat xabarlari» (Sozlamalar → O'quv → Jurnal va davomat) — ota-onaga «kelmadi» xabari qachon ketishi
 * va menejer tasdig'i (SPEC P3.2/P3.3). «Keldi» xabari hamon darhol ketadi.
 */
export function AbsentNoticeSettingsModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [loaded, setLoaded] = useState<AttendanceSettings | null>(null)
  const [delay, setDelay] = useState<number | null>(null)
  const [approval, setApproval] = useState<AbsentNoticeApproval>('late_only')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    let alive = true
    getAttendanceSettings()
      .then((s) => {
        if (!alive) return
        setLoaded(s)
        setDelay(s.absentNoticeDelayMinutes)
        setApproval(s.absentNoticeApproval)
        setError(null)
      })
      .catch((err) => {
        if (alive) setError(apiErrorMessage(err, "Sozlamani yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [open])

  const save = async () => {
    if (!loaded) return
    if (delay === null) {
      setError('Necha daqiqadan keyin yuborilishini kiriting')
      return
    }
    setSaving(true)
    setError(null)
    try {
      await saveAttendanceSettings({ ...loaded, absentNoticeDelayMinutes: delay, absentNoticeApproval: approval })
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
      title="Davomat xabarlari"
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !loaded}>
            {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {!loaded ? (
        error ? <p className="text-sm text-red-600">{error}</p> : <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-3 rounded-lg bg-slate-50 px-3 py-2.5">
            <span className="text-sm text-slate-600">Dars tugagandan keyin necha daqiqada yuborilsin</span>
            <NumberInput
              as="input"
              value={delay}
              onChange={setDelay}
              className="w-24 rounded-lg border border-slate-200 px-2.5 py-1.5 text-center text-sm font-semibold outline-none focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
            <span className="text-sm text-slate-600">daqiqa</span>
          </div>

          <div className="space-y-2" role="radiogroup" aria-label="Tasdiqlash">
            {APPROVALS.map((a) => (
              <button
                key={a.key}
                type="button"
                role="radio"
                aria-checked={approval === a.key}
                onClick={() => setApproval(a.key)}
                className={cn(
                  'w-full rounded-xl border p-3 text-left transition-all',
                  approval === a.key
                    ? 'border-brand-400 bg-brand-50 ring-2 ring-brand-100'
                    : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
                )}
              >
                <div className={cn('text-sm font-semibold', approval === a.key ? 'text-brand-700' : 'text-slate-700')}>
                  {a.title}
                </div>
                <p className="mt-0.5 text-xs text-slate-500">{a.desc}</p>
              </button>
            ))}
          </div>

          <div className="flex items-start gap-2.5 rounded-lg bg-blue-50 px-3 py-2.5 text-xs text-blue-700">
            <Info className="mt-0.5 h-4 w-4 shrink-0" />
            <p>«Keldi» xabari tasdiqsiz, darhol ketadi — bu sozlama faqat «kelmadi» xabariga tegishli.</p>
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
