import { useEffect } from 'react'
import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react'
import { IconArrowLeft, IconChevronDown } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * O'NG TOMONDAN CHIQADIGAN PANEL — edutizimdagi to'lov oynalari (react-bootstrap Offcanvas):
 * o'ngda 400px, tepasida 70px ko'k (`#3D68FF`) sarlavha paneli — oq "orqaga" strelkasi + nom,
 * ichida yorliqlar 15px / 500 maydon USTIDA, maydonlar kulrang to'ldirilgan (36px, `#F0F2F2`,
 * ramka `#DBE0E6`, radius 8), pastda "Orqaga" (ramkali) + "Saqlash" (asosiy).
 *
 * Moliyaning barcha yozish oynalari (Kirim — o'quvchi to'lovi, Chiqim, maosh) SHU komponent
 * orqali chiziladi — ko'rinish bir joyda, oynalar ichki mantiqni o'zgartirmaydi.
 * Tor ekranda (telefon) butun kenglikni egallaydi.
 */
export function RightDrawer({
  open,
  onClose,
  title,
  children,
  footer,
  width = 400,
}: {
  open: boolean
  onClose: () => void
  title: ReactNode
  children: ReactNode
  /** Pastki tugmalar qatori (odatda `<DrawerActions />`). */
  footer?: ReactNode
  /** Panel kengligi (px). */
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
    <div className="fixed inset-0 z-50 flex justify-end">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <aside
        role="dialog"
        aria-modal="true"
        style={{ maxWidth: width }}
        className="relative z-10 flex h-full w-full flex-col bg-white shadow-[-8px_0_24px_rgba(0,0,0,0.12)]"
      >
        <header className="flex h-[70px] shrink-0 items-center gap-3 bg-brand-600 px-4 text-white">
          <button
            type="button"
            onClick={onClose}
            aria-label="Orqaga"
            className="inline-flex h-9 w-9 items-center justify-center rounded-lg transition-colors hover:bg-white/15"
          >
            <IconArrowLeft className="h-6 w-6" />
          </button>
          <h3 className="min-w-0 truncate text-[16px] font-medium">{title}</h3>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">{children}</div>
        {footer && <footer className="flex shrink-0 justify-end gap-2 px-4 pb-4 pt-2">{footer}</footer>}
      </aside>
    </div>
  )
}

/** Panel ichidagi maydon: yorliq (15px / 500) USTIDA, ixtiyoriy izoh ostida. */
export function DrawerField({
  label,
  required,
  hint,
  children,
  group,
}: {
  label: ReactNode
  required?: boolean
  hint?: ReactNode
  children: ReactNode
  /** Maydon bitta input emas (tugmalar guruhi) — `<label>` o'rniga `role="group"`. */
  group?: boolean
}) {
  const caption = (
    <span className="mb-1.5 block text-[15px] font-medium text-[#333]">
      {label}
      {required && <span className="text-red-500"> *</span>}
    </span>
  )
  const note = hint ? <span className="mt-1 block text-xs text-[#6b7280]">{hint}</span> : null
  return group ? (
    <div role="group">
      {caption}
      {children}
      {note}
    </div>
  ) : (
    <label className="block">
      {caption}
      {children}
      {note}
    </label>
  )
}

const drawerControl =
  'h-9 w-full rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 text-[14px] text-black outline-none transition-colors placeholder:text-[#9ca3af] focus:border-brand-600 focus:bg-white focus:ring-1 focus:ring-brand-600 disabled:cursor-not-allowed disabled:opacity-60'

/** Kulrang to'ldirilgan 36px maydon. */
export function DrawerInput({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn(drawerControl, className)} {...rest} />
}

/** Kulrang to'ldirilgan tanlov (o'ngda uchburchak). */
export function DrawerSelect({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className="relative">
      <select className={cn(drawerControl, 'appearance-none pr-9', className)} {...rest}>
        {children}
      </select>
      <IconChevronDown className="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
    </div>
  )
}

/** Ko'p qatorli izoh maydoni. */
export function DrawerTextarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      className={cn(
        'w-full resize-none rounded-lg border border-[#dbe0e6] bg-[#f0f2f2] px-3 py-2 text-[14px] text-black outline-none transition-colors placeholder:text-[#9ca3af] focus:border-brand-600 focus:bg-white focus:ring-1 focus:ring-brand-600',
        className,
      )}
      {...rest}
    />
  )
}

/** Variantlardan birini tanlash (masalan to'lov usuli) — kulrang "pill"lar, tanlangani ko'k. */
export function DrawerChoice<V extends string>({
  options,
  value,
  onChange,
}: {
  options: { value: V; label: string }[]
  value: V
  onChange: (v: V) => void
}) {
  return (
    <div className="grid gap-2" style={{ gridTemplateColumns: `repeat(${options.length}, minmax(0, 1fr))` }}>
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          aria-pressed={value === o.value}
          onClick={() => onChange(o.value)}
          className={cn(
            'h-9 rounded-lg border px-2 text-[13px] font-medium transition-colors',
            value === o.value
              ? 'border-brand-600 bg-brand-600/10 text-brand-700'
              : 'border-[#dbe0e6] bg-[#f0f2f2] text-[#333] hover:bg-[#e6eaea]',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

/** Pastki tugmalar: "Orqaga" (ramkali) + asosiy tugma(lar) — `children`. */
export function DrawerActions({
  onBack,
  backLabel = 'Orqaga',
  children,
}: {
  onBack: () => void
  backLabel?: string
  children: ReactNode
}) {
  return (
    <>
      <button
        type="button"
        onClick={onBack}
        className="inline-flex min-h-[31px] items-center justify-center rounded-lg border border-[#dbe0e6] bg-white px-3 py-1 text-xs font-medium text-[#333] transition-colors hover:bg-[#f0f2f2]"
      >
        {backLabel}
      </button>
      {children}
    </>
  )
}
