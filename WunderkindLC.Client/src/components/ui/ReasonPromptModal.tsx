import { useEffect, useRef, useState } from 'react'
import type { ActionReason } from '@/types'
import { getActionReasons } from '@/api/services/actionReasons'
import { Modal } from './Modal'
import { Button } from './Button'
import { cn } from '@/lib/utils'
import { localIsoDate } from '@/lib/localDate'

import { DateInput } from '@/components/ui/DateInput'

/** Izoh uzunligi — serverdagi `LeaveNotes.MaxLength` bilan bir xil. */
const NOTE_MAX = 500

interface Props {
  open: boolean
  /** Sabablar kategoriyasi (ReasonsPage'dagi kalit). */
  category: string
  title: string
  message?: string
  confirmLabel: string
  tone?: 'red' | 'sky' | 'brand'
  /** Sana maydoni ko'rsatilsinmi (masalan muzlatish uchun). */
  showDate?: boolean
  defaultDate?: string
  /**
   * "Bonus hisoblansin" ptichkasi ko'rsatilsinmi — FAQAT aktivlashtirish oqimida.
   * Ko'rsatilmasa `onConfirm` ga `retentionBonus` umuman uzatilmaydi (undefined).
   */
  showRetentionBonus?: boolean
  /**
   * Erkin «Izoh» maydoni ko'rsatilsinmi — o'quvchi KETADIGAN oqimlarda (guruhdan chiqarish,
   * arxivga ko'chirish). Ko'rsatilmasa `onConfirm` ga `note` umuman uzatilmaydi (undefined);
   * bo'sh izoh ham `undefined`. Server uni o'quvchi profilidagi izohlarga yozadi (`LeaveNotes`).
   */
  showNote?: boolean
  /**
   * Tasdiqlash. Promise qaytarsa — tugma u tugaguncha qulf, keyin (masalan xatodan so'ng) qayta
   * yoqiladi. `void` qaytarsa — oyna yopilguncha qulf (eski xatti-harakat).
   */
  onConfirm: (
    reasonId: string | undefined,
    date?: string,
    retentionBonus?: boolean,
    note?: string,
  ) => void | Promise<unknown>
  onClose: () => void
}

/**
 * Amal (muzlatish/o'chirish/sinovga qaytarish/lid/guruh) bajarishdan oldin SABAB tanlash modali.
 * Sabablar Sozlamalar → Sabablar bo'limidan (kategoriya bo'yicha) keladi. Sabab ixtiyoriy.
 */
export function ReasonPromptModal({
  open, category, title, message, confirmLabel, tone = 'red', showDate, defaultDate,
  showRetentionBonus, showNote, onConfirm, onClose,
}: Props) {
  const [reasons, setReasons] = useState<ActionReason[]>([])
  const [selected, setSelected] = useState<string | undefined>(undefined)
  const [date, setDate] = useState('')
  /** Ushlab turish bonusi hisoblansinmi — standart holatda BELGILANGAN. */
  const [retentionBonus, setRetentionBonus] = useState(false)
  const [note, setNote] = useState('')
  const [submitting, setSubmitting] = useState(false)
  /** SINXRON qulf — `submitting` keyingi renderda yangilanadi, tez ikkinchi bosish o'tib ketmasin. */
  const inFlightRef = useRef(false)

  useEffect(() => {
    if (!open) return
    setSelected(undefined)
    inFlightRef.current = false
    setSubmitting(false)
    setRetentionBonus(false)
    setNote('')
    setDate(defaultDate ?? localIsoDate(new Date()))
    getActionReasons()
      .then((all) => setReasons(all.filter((r) => r.category === category)))
      .catch(() => setReasons([]))
  }, [open, category, defaultDate])

  const toneCls =
    tone === 'sky' ? 'bg-sky-600 hover:bg-sky-700'
    : tone === 'brand' ? 'bg-brand-600 hover:bg-brand-700'
    : 'bg-red-600 hover:bg-red-700'

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={title}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor
          </Button>
          <button
            type="button"
            disabled={submitting}
            onClick={() => {
              if (inFlightRef.current || submitting) return
              inFlightRef.current = true
              setSubmitting(true)
              const r = onConfirm(
                selected,
                showDate ? date : undefined,
                showRetentionBonus ? retentionBonus : undefined,
                showNote ? note.trim() || undefined : undefined,
              )
              if (r instanceof Promise)
                void r.finally(() => {
                  inFlightRef.current = false
                  setSubmitting(false)
                })
            }}
            className={cn('rounded-lg px-4 py-2 text-sm font-medium text-white transition-colors disabled:opacity-50', toneCls)}
          >
            {confirmLabel}
          </button>
        </>
      }
    >
      <div className="space-y-3">
        {message && <p className="text-sm text-slate-600">{message}</p>}

        {showDate && (
          <div>
            <span className="mb-1 block text-sm font-medium text-slate-600">Sana</span>
            <DateInput
              value={date}
              onChange={(e) => setDate(e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
          </div>
        )}

        {showRetentionBonus && (
          <label className="flex cursor-pointer items-start gap-2">
            <input
              type="checkbox"
              className="mt-1 h-4 w-4 rounded border-slate-300 text-blue-600 focus:ring-blue-500"
              checked={retentionBonus}
              onChange={(e) => setRetentionBonus(e.target.checked)}
            />
            <span className="text-sm text-slate-700">
              Bonus hisoblansin
              <span className="block text-xs text-slate-400">
                Ushlab turish bonusi shu fan bo'yicha sanaladi. Sanoq aktivlashtirilgan oydan
                boshlanadi.
              </span>
            </span>
          </label>
        )}

        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-600">
            Sabab {reasons.length === 0 && <span className="text-xs font-normal text-slate-400">(Sabablar bo'limida sozlanadi)</span>}
          </span>
          {reasons.length > 0 ? (
            <div className="flex flex-wrap gap-1.5">
              {reasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => setSelected((s) => (s === r.id ? undefined : r.id))}
                  className={cn(
                    'rounded-full border px-3 py-1.5 text-sm transition-colors',
                    selected === r.id
                      ? 'border-brand-500 bg-brand-50 font-medium text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:border-brand-300 hover:bg-slate-50',
                  )}
                >
                  {r.label}
                </button>
              ))}
            </div>
          ) : (
            <p className="text-xs text-slate-400">Sabab tanlanmaydi — to'g'ridan-to'g'ri davom etadi.</p>
          )}
        </div>

        {showNote && (
          <label className="block">
            <span className="mb-1 block text-sm font-medium text-slate-600">
              Izoh <span className="text-xs font-normal text-slate-400">(ixtiyoriy)</span>
            </span>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={NOTE_MAX}
              rows={3}
              placeholder="Masalan: ota-onasi bilan gaplashildi, narx qimmatlik qildi"
              className="w-full resize-y rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
            <span className="mt-1 block text-xs text-slate-400">
              O'quvchi profilidagi izohlarga yoziladi.
            </span>
          </label>
        )}
      </div>
    </Modal>
  )
}
