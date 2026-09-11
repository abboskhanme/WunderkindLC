import type { LucideIcon } from 'lucide-react'
import { ArrowDownRight, ArrowUpRight } from 'lucide-react'
import { cn } from '@/lib/utils'

interface StatCardProps {
  label: string
  value: string | number
  icon: LucideIcon
  /** Ikon foni uchun tailwind class, masalan "bg-brand-50" */
  iconBg?: string
  /** Ikon rangi uchun tailwind class, masalan "text-brand-600" */
  iconColor?: string
  hint?: string
  /** O'zgarish ko'rsatkichi: {value:"+12%", dir:"up"|"down"} */
  delta?: { value: string; dir: 'up' | 'down' }
}

export function StatCard({
  label,
  value,
  icon: Icon,
  iconBg = 'bg-brand-50',
  iconColor = 'text-brand-600',
  hint,
  delta,
}: StatCardProps) {
  return (
    // LMS uslubi: chapda katta yumaloq ikon plitkasi, o'ngda yorliq + qiymat.
    // (Ilgari ikon o'ng yuqorida, yorliq esa CAPS bo'lgan "dashboard" ko'rinishi edi.)
    <div className="flex items-center gap-4 rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm">
      <div
        className={cn(
          'flex h-12 w-12 shrink-0 items-center justify-center rounded-xl',
          iconBg,
          iconColor,
        )}
      >
        <Icon className="h-6 w-6" />
      </div>
      <div className="min-w-0">
        <p className="truncate text-sm text-slate-500">{label}</p>
        <p className="text-2xl font-semibold text-slate-800">{value}</p>
        {(delta || hint) && (
          <div className="mt-0.5 flex items-center gap-2 text-xs">
            {delta && (
              <span
                className={cn(
                  'inline-flex items-center gap-0.5 rounded px-1.5 py-0.5 font-medium',
                  delta.dir === 'up' ? 'bg-emerald-50 text-emerald-600' : 'bg-red-50 text-red-600',
                )}
              >
                {delta.dir === 'up' ? (
                  <ArrowUpRight className="h-3 w-3" />
                ) : (
                  <ArrowDownRight className="h-3 w-3" />
                )}
                {delta.value}
              </span>
            )}
            {hint && <span className="text-slate-400">{hint}</span>}
          </div>
        )}
      </div>
    </div>
  )
}
