import { useEffect, useState } from 'react'
import { IconChevronDown, IconX } from '@tabler/icons-react'
import {
  FIELD_SLOTS,
  LESSON_FIELD_LABELS,
  type FieldSettings,
  type LessonField,
} from '@/lib/scheduleGrid'

const FIELD_OPTIONS = Object.entries(LESSON_FIELD_LABELS) as [LessonField, string][]

/**
 * "Jadval ko'rinishi sozlamalari" — o'ng tomondagi drawer (≈400px, edutizim §3): dars blokining
 * har qatorida qaysi maydon chiqishini tanlash. Qoralama ICHKARIDA turadi va faqat "Saqlash"
 * bosilganda qo'llanadi (oyna har ochilishda qayta yaratiladi — boshlang'ich qiymat initializer'da).
 */
export function ScheduleSettingsDrawer({
  initial,
  onSave,
  onClose,
}: {
  initial: FieldSettings
  onSave: (s: FieldSettings) => void
  onClose: () => void
}) {
  const [draft, setDraft] = useState<FieldSettings>(initial)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="fixed inset-0 z-[80]" role="dialog" aria-modal="true" aria-label="Jadval ko'rinishi sozlamalari">
      <button type="button" aria-label="Yopish" className="absolute inset-0 bg-black/30" onClick={onClose} />
      <div className="absolute inset-y-0 right-0 flex w-[400px] max-w-full flex-col bg-white shadow-2xl">
        <div className="flex items-start justify-between gap-3 border-b border-[#DBE0E6] px-5 py-4">
          <div>
            <h2 className="text-[16px] font-semibold text-black">Jadval ko'rinishi sozlamalari</h2>
            <p className="mt-0.5 text-[13px] text-[#6B7280]">Har bir qator uchun maydon tanlang</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Yopish"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-[#6B7280] hover:bg-[#F0F2F2]"
          >
            <IconX size={20} stroke={2} />
          </button>
        </div>

        <div className="flex-1 space-y-4 overflow-y-auto px-5 py-4">
          {FIELD_SLOTS.map((slot) => (
            <label key={slot.key} className="block">
              <span className="mb-1 block text-[13px] font-medium text-[#374151]">{slot.label}</span>
              <span className="relative block">
                <select
                  value={draft[slot.key]}
                  onChange={(e) => setDraft((d) => ({ ...d, [slot.key]: e.target.value as LessonField }))}
                  className="h-10 w-full appearance-none rounded-lg border border-[rgba(0,0,0,0.23)] bg-white pl-3 pr-9 text-[14px] text-black outline-none hover:border-black/60 focus:border-[#3D68FF] focus:ring-1 focus:ring-[#3D68FF]"
                >
                  {FIELD_OPTIONS.map(([value, label]) => (
                    <option key={value} value={value}>
                      {label}
                    </option>
                  ))}
                </select>
                <IconChevronDown
                  size={18}
                  stroke={2}
                  className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-black/60"
                />
              </span>
            </label>
          ))}
        </div>

        <div className="flex justify-end border-t border-[#DBE0E6] px-5 py-3">
          <button
            type="button"
            onClick={() => onSave(draft)}
            className="inline-flex h-9 items-center rounded-lg bg-[#3D68FF] px-5 text-[14px] font-medium text-white hover:bg-[#2F57E8]"
          >
            Saqlash
          </button>
        </div>
      </div>
    </div>
  )
}
