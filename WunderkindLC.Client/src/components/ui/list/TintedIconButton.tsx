import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface TintedIconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  /** Tugma ichidagi ikonka (Tabler, 20px). */
  children: ReactNode
  /** Bosilgan holat (masalan filtr paneli ochiq) — foni to'qroq. */
  pressed?: boolean
  /** Maslahat matni — `title` va `aria-label` ga yoziladi. */
  label: string
}

/**
 * edutizimdagi och-ko'k (tinted) kvadrat ikonka tugmasi: 32px, radius 8, fon
 * `rgba(61,104,255,.10)`, ramka `rgba(61,104,255,.20)`, ko'k ikonka.
 */
export function TintedIconButton({ children, pressed, label, className, ...rest }: TintedIconButtonProps) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      aria-pressed={pressed}
      className={cn(
        'inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-brand-600/20 text-brand-600 transition-colors disabled:opacity-50',
        pressed ? 'bg-brand-600/25' : 'bg-brand-600/10 hover:bg-brand-600/15',
        className,
      )}
      {...rest}
    >
      {children}
    </button>
  )
}
