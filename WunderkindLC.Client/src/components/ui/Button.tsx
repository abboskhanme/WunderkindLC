import type { ButtonHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

type Variant = 'primary' | 'secondary' | 'danger' | 'ghost'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
}

// edutizim (MUI) tugmalari: asosiy — to'la ko'k; ikkilamchi — ramkali ko'k matn.
const variants: Record<Variant, string> = {
  primary: 'bg-brand-600 text-white hover:bg-brand-700',
  secondary: 'border border-brand-600/50 bg-white text-brand-600 hover:bg-brand-50',
  danger: 'bg-[#d32f2f] text-white hover:bg-[#c62828]',
  ghost: 'text-brand-600 hover:bg-brand-50',
}

export function Button({ variant = 'primary', className, ...rest }: ButtonProps) {
  return (
    <button
      className={cn(
        // edutizim (MUI small): 31px, radius 8, 12px / 500
        'inline-flex min-h-[31px] items-center justify-center gap-1.5 rounded-lg px-3 py-1 text-xs font-medium transition-colors disabled:cursor-not-allowed disabled:opacity-50',
        variants[variant],
        className,
      )}
      {...rest}
    />
  )
}
