import { useEffect, useState } from 'react'
import { Check, Info } from 'lucide-react'
import type { StageColor } from '@/types'
import type { ContactMeta } from '@/api/services/contacts'
import type { ContactStage, ContactStagePayload } from '@/api/services/contactStages'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { stageColors, stageColorKeys } from '@/config/stageColors'
import { cn } from '@/lib/utils'

/**
 * TAXTA USTUNINI qo'shish/tahrirlash (lidlardagi `StageFormModal` bilan bir xil naqsh).
 *
 * <p>⚠️ Lidlardagidan BITTA farqi bor va u majburiy: <b>bazaviy bosqich</b>. Bu modulda
 * hisobotlar, navbat va muddat guruhlari talabning HOLATI bo'yicha ishlaydi, shuning uchun
 * har ustun to'rtta bazaviy holatdan biriga bog'lanadi. Shunda foydalanuvchi xohlagancha
 * ustun qo'shsa ham hisobot o'zgarmaydi.</p>
 *
 * <p>TIZIM ustunida bazaviy bosqich o'zgartirilmaydi (nomi va rangi esa o'zgaradi) — u
 * o'z holatidagi kartalar uchun "uy" bo'lib qolishi kerak.</p>
 */
export function ContactStageFormModal({
  open,
  initial,
  meta,
  onClose,
  onSubmit,
  busy,
}: {
  open: boolean
  /** Tahrirlash uchun mavjud ustun, qo'shish uchun null. */
  initial?: ContactStage | null
  /** Bazaviy bosqich yorliqlari serverdan (yagona katalog). */
  meta: ContactMeta
  onClose: () => void
  onSubmit: (values: ContactStagePayload) => void
  busy?: boolean
}) {
  const [title, setTitle] = useState('')
  const [color, setColor] = useState<StageColor>('blue')
  // Yangi ustun odatda ORALIQ qadam ("SMS yuborildi") — ya'ni talab hali navbatda turadi.
  const [baseStatus, setBaseStatus] = useState('callback')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setTitle(initial?.title ?? '')
    setColor(initial?.color ?? 'blue')
    setBaseStatus(initial?.baseStatus ?? 'callback')
  }, [open, initial])

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!title.trim()) return
    onSubmit({ title: title.trim(), color, baseStatus })
  }

  const currentLabel =
    meta.statuses.find((s) => s.key === baseStatus)?.label ?? baseStatus

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Ustunni tahrirlash' : 'Yangi ustun'}
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="contact-stage-form" disabled={busy || !title.trim()}>
            {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="contact-stage-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Ustun nomi"
          required
          placeholder="Masalan: Bog'lanildi"
          value={title}
          maxLength={100}
          onChange={(e) => setTitle(e.target.value)}
        />

        <div>
          <span className="mb-2 block text-sm font-medium text-slate-600">Rang</span>
          <div className="flex flex-wrap gap-2">
            {stageColorKeys.map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setColor(key)}
                className={cn(
                  'flex h-8 w-8 items-center justify-center rounded-full transition',
                  stageColors[key].swatch,
                  color === key && 'ring-2 ring-slate-800 ring-offset-2',
                )}
              >
                {color === key && <Check className="h-4 w-4 text-white" />}
              </button>
            ))}
          </div>
        </div>

        <div>
          <span className="mb-1.5 block text-sm font-medium text-slate-600">Bazaviy bosqich</span>
          {initial?.isSystem ? (
            <p className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-600">
              {currentLabel}
              <span className="mt-0.5 block text-xs text-slate-400">
                Tizim ustuni — bazaviy bosqichi o'zgartirilmaydi (nomi va rangini o'zgartirsangiz bo'ladi).
              </span>
            </p>
          ) : (
            <select
              value={baseStatus}
              onChange={(e) => setBaseStatus(e.target.value)}
              className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              {meta.statuses.map((s) => (
                <option key={s.key} value={s.key}>
                  {s.label}
                </option>
              ))}
            </select>
          )}
          <p className="mt-1.5 flex gap-1.5 text-xs text-slate-400">
            <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
            <span>
              Hisobotlar, navbat va muddat guruhlari AYNAN shu bosqich bo'yicha hisoblanadi —
              ustun faqat ko'rinishni bo'ladi. Shuning uchun ustun qo'shish hisobotdagi
              raqamlarni o'zgartirmaydi.
            </span>
          </p>
        </div>
      </form>
    </Modal>
  )
}
