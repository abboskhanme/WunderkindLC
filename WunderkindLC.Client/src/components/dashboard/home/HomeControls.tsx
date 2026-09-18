import type { ReactNode } from 'react'
import { IconChevronDown, type TablerIcon } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * Bosh sahifa ("Dars jadvali") boshqaruv elementlari — edutizim o'lchamlarida
 * (`docs/edutizim/INVENTORY.md` §1): tugma 31px / r8 / 12px-500 / 20px ikonka, filtr select
 * 34px / r8, tinted ikonka tugma 32px, toggle guruh — oq quti r8 p-4, tugma 36×28.
 */

/** "Statistika" / "Filtr" / "Export" — asosiy (to'ldirilgan) kichik tugma. */
export function HomeButton({
  icon: Icon,
  children,
  onClick,
  disabled,
  active,
}: {
  icon: TablerIcon
  children: ReactNode
  onClick: () => void
  disabled?: boolean
  /** Toggle tugma bosilgan holatda (aria-pressed). */
  active?: boolean
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={active}
      className="inline-flex h-[31px] items-center gap-1.5 rounded-lg bg-[#3D68FF] px-3 text-[12px] font-medium text-white shadow-sm transition-colors hover:bg-[#2F57E8] disabled:cursor-not-allowed disabled:opacity-60"
    >
      <Icon size={20} stroke={2} />
      {children}
    </button>
  )
}

/** Tinted ikonka tugma (sozlamalar, katta holat): primary 10% fon + 20% chegara. */
export function TintIconButton({
  icon: Icon,
  title,
  onClick,
  active,
}: {
  icon: TablerIcon
  title: string
  onClick: () => void
  active?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      aria-pressed={active}
      onClick={onClick}
      className="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-[rgba(61,104,255,0.20)] bg-[rgba(61,104,255,0.10)] text-[#3D68FF] transition-colors hover:bg-[rgba(61,104,255,0.18)]"
    >
      <Icon size={20} stroke={2} />
    </button>
  )
}

/** Ikki holatli toggle guruh (xona/o'qituvchi, to'r/ro'yxat). */
export function ToggleGroup<T extends string>({
  value,
  options,
  onChange,
}: {
  value: T
  options: { value: T; icon: TablerIcon; title: string }[]
  onChange: (v: T) => void
}) {
  return (
    <div className="inline-flex gap-1 rounded-lg border border-[#DBE0E6] bg-white p-1">
      {options.map(({ value: v, icon: Icon, title }) => {
        const on = v === value
        return (
          <button
            key={v}
            type="button"
            title={title}
            aria-label={title}
            aria-pressed={on}
            onClick={() => onChange(v)}
            className={cn(
              'inline-flex h-7 w-9 items-center justify-center rounded-lg transition-colors',
              on ? 'bg-[#3D68FF] text-white' : 'text-[#3D68FF] hover:bg-[rgba(61,104,255,0.08)]',
            )}
          >
            <Icon size={18} stroke={2} />
          </button>
        )
      })}
    </div>
  )
}

/** Filtr qatoridagi ixcham select (≈165px, 34px). Birinchi variant — joy egasi (bo'sh qiymat). */
export function FilterSelect({
  placeholder,
  value,
  options,
  onChange,
}: {
  placeholder: string
  value: string
  options: { value: string; label: string }[]
  onChange: (v: string) => void
}) {
  return (
    <label className="relative inline-flex w-[165px] max-w-full">
      <span className="sr-only">{placeholder}</span>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className={cn(
          'h-[34px] w-full appearance-none truncate rounded-lg border border-[rgba(0,0,0,0.23)] bg-white pl-3 pr-8 text-[13px] outline-none transition-colors hover:border-black/60 focus:border-[#3D68FF] focus:ring-1 focus:ring-[#3D68FF]',
          value ? 'text-black' : 'text-black/80',
        )}
      >
        <option value="">{placeholder}</option>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      <IconChevronDown
        size={18}
        stroke={2}
        className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-black/60"
      />
    </label>
  )
}
