import { useEffect, type ReactNode } from 'react'
import { IconX } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * edutizimdagi forma oynasi ("Yangi guruh qo'shish", "Guruhni tahrirlash", "Xona qo'shish"):
 * 500px, radius 12, fon `rgba(0,0,0,.4)`, sarlavha 18px/600, ostida "* Zarurligini bildiradi",
 * maydonlar — yorliq TEPADA, kulrang to'ldirilgan 36px input; pastda o'ngda "Orqaga" (matnli)
 * va "Saqlash" (ko'k).
 *
 * Forma tugmasi `form={formId}` bilan bog'lanadi — ichkaridagi `<form id>` submit qiladi
 * (Enter ham ishlaydi).
 */
export function EduFormModal({
  open,
  onClose,
  title,
  formId,
  children,
  saving,
  saveLabel = 'Saqlash',
  showRequiredHint = true,
  width = 500,
}: {
  open: boolean
  onClose: () => void
  title: string
  formId: string
  children: ReactNode
  saving?: boolean
  saveLabel?: string
  showRequiredHint?: boolean
  width?: number
}) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/40" onClick={onClose} />
      <div
        className="relative z-10 flex max-h-[92vh] w-full flex-col overflow-hidden rounded-xl bg-white shadow-xl"
        style={{ maxWidth: width }}
        role="dialog"
        aria-modal="true"
        aria-label={title}
      >
        <header className="flex items-start justify-between gap-3 px-5 pb-2 pt-4">
          <div>
            <h3 className="text-[18px] font-semibold leading-6 text-black">{title}</h3>
            {showRequiredHint && <p className="mt-0.5 text-[12px] text-[#333]">* Zarurligini bildiradi</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Yopish"
            className="rounded-lg p-1 text-[#757575] transition-colors hover:bg-black/[0.06]"
          >
            <IconX className="h-5 w-5" />
          </button>
        </header>
        <div className="flex-1 overflow-y-auto px-5 py-2">{children}</div>
        <footer className="flex items-center justify-end gap-2 px-5 py-3">
          <button
            type="button"
            onClick={onClose}
            className="inline-flex min-h-[31px] items-center rounded-lg px-3 text-[13px] font-medium text-[#333] transition-colors hover:bg-black/[0.06]"
          >
            Orqaga
          </button>
          <button
            type="submit"
            form={formId}
            disabled={saving}
            className="inline-flex min-h-[31px] items-center rounded-lg bg-brand-600 px-3.5 text-[13px] font-medium text-white transition-colors hover:bg-brand-700 disabled:opacity-60"
          >
            {saving ? 'Saqlanmoqda...' : saveLabel}
          </button>
        </footer>
      </div>
    </div>
  )
}

/** Maydon: yorliq TEPADA (15px/500), `*` — majburiy. */
export function EduField({
  label,
  required,
  children,
  className,
  hint,
}: {
  label: string
  required?: boolean
  children: ReactNode
  className?: string
  hint?: ReactNode
}) {
  return (
    <label className={cn('mb-3 block', className)}>
      <span className="mb-1 block text-[15px] font-medium text-black">
        {label}
        {required && <span className="text-[#d32f2f]">*</span>}
      </span>
      {children}
      {hint && <span className="mt-1 block text-[12px] text-[#6b7280]">{hint}</span>}
    </label>
  )
}
