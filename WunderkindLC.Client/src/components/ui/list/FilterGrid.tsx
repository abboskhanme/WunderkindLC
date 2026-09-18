import type { InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { IconChevronDown, IconSearch } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

/**
 * edutizim filtr to'ri: 5 ta teng ustun (tor ekranda kamroq), 34px ramkali maydonlar, oq fon,
 * ~8px oraliq. Yorliq YO'Q — placeholder yorliq vazifasini bajaradi.
 */
export function FilterGrid({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={cn('mb-2.5 grid grid-cols-1 gap-2 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-5', className)}>
      {children}
    </div>
  )
}

const field =
  'h-[34px] w-full rounded-lg border border-black/25 bg-white text-[13px] text-black outline-none transition-colors hover:border-black/60 focus:border-brand-600 focus:ring-1 focus:ring-brand-600'

/** Matnli filtr (ixtiyoriy qidiruv ikonkasi bilan). */
export function FilterInput({
  search,
  className,
  ...rest
}: InputHTMLAttributes<HTMLInputElement> & { search?: boolean }) {
  return (
    <div className="relative">
      {search && (
        <IconSearch className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
      )}
      <input
        className={cn(field, search ? 'pl-8 pr-3' : 'px-3', 'placeholder:text-[#6b7280]', className)}
        {...rest}
      />
    </div>
  )
}

/**
 * Tanlov filtri — bo'sh qiymat placeholder bo'lib ko'rinadi (kulrang), o'ngda ko'k-kulrang uchburchak.
 */
export function FilterSelect({
  placeholder,
  children,
  className,
  value,
  ...rest
}: SelectHTMLAttributes<HTMLSelectElement> & { placeholder: string }) {
  const empty = value === '' || value == null
  return (
    <div className="relative">
      <select
        value={value}
        className={cn(field, 'appearance-none pl-3 pr-8', empty ? 'text-[#6b7280]' : 'text-black', className)}
        {...rest}
      >
        <option value="">{placeholder}</option>
        {children}
      </select>
      <IconChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
    </div>
  )
}
