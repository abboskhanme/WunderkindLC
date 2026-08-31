import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

export type BadgeTone = 'default' | 'violet' | 'indigo' | 'green' | 'amber' | 'red' | 'blue' | 'teal'

const tones: Record<BadgeTone, string> = {
  default: 'border-slate-200 bg-slate-50 text-slate-600',
  violet: 'border-transparent bg-brand-50 text-brand-700',
  // «Aktiv muzlatish» uchun: sky (oddiy muzlatish) va brand-violet (sinov) dan ajralib tursin.
  indigo: 'border-transparent bg-indigo-50 text-indigo-700',
  green: 'border-transparent bg-emerald-50 text-emerald-600',
  amber: 'border-transparent bg-amber-50 text-amber-600',
  red: 'border-transparent bg-red-50 text-red-600',
  blue: 'border-transparent bg-sky-50 text-sky-600',
  teal: 'border-transparent bg-tealsoft text-teal-700',
}

interface BadgeProps {
  tone?: BadgeTone
  dot?: boolean
  className?: string
  children: ReactNode
}

export function Badge({ tone = 'default', dot, className, children }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-semibold',
        tones[tone],
        className,
      )}
    >
      {dot && <span className="h-1.5 w-1.5 rounded-full bg-current" />}
      {children}
    </span>
  )
}
