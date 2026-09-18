import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface PageHeaderProps {
  title: ReactNode
  sub?: ReactNode
  /** O'ng tomondagi amallar (tugmalar, filtrlar) */
  actions?: ReactNode
  className?: string
}

/** Sahifa sarlavhasi — edutizimdagidek: 18px / 600 / qora, o'ngda amallar. */
export function PageHeader({ title, sub, actions, className }: PageHeaderProps) {
  return (
    <div
      className={cn(
        'mb-3 flex flex-wrap items-center justify-between gap-3',
        className,
      )}
    >
      <div className="min-w-0">
        <h1 className="text-lg font-semibold text-black">{title}</h1>
        {sub != null && <p className="mt-0.5 text-[13px] text-[#6b7280]">{sub}</p>}
      </div>
      {actions != null && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}
