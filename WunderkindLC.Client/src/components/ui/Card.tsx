import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'

interface CardProps {
  className?: string
  children: ReactNode
  /** Sarlavha — berilsa card-header ko'rsatiladi */
  title?: ReactNode
  /** Sarlavha ostidagi kichik izoh */
  sub?: ReactNode
  /** Sarlavhaning o'ng tomonidagi amallar (tugmalar) */
  actions?: ReactNode
  /** Ichki padding'siz (jadval/list uchun) */
  tight?: boolean
  /** Tana uchun qo'shimcha class (tight bo'lmaganda) */
  bodyClassName?: string
}

/**
 * Chaqiruvchi TO'LIQ padding bergan-bermaganini aniqlaydi (`p-0`, `p-4`, `sm:p-[18px]`).
 *
 * ⚠️ NEGA KERAK: loyihadagi `cn()` — oddiy `join`, `tailwind-merge` EMAS. Ya'ni
 * `<Card className="p-0">` yozilganda class satrida `p-5` ham, `p-0` ham qoladi va CSS'da
 * keyinroq turgani (`p-5`) g'olib chiqadi — card baribir paddingli bo'lib qolardi
 * (o'qituvchi PWA'sidagi jurnal jadvali shu sababdan ikki qavat "devordan uzoqda" turardi).
 *
 * FAQAT to'liq `p-*` hisobga olinadi: `px-`/`py-` bergan chaqiruvchilar (masalan
 * `py-12 text-center`) gorizontal paddingni AYNAN standartdan oladi — ularni buzmaymiz.
 */
function overridesPadding(className?: string): boolean {
  return !!className && /(^|\s)([\w-]+:)*p-\S+/.test(className)
}

export function Card({
  className,
  children,
  title,
  sub,
  actions,
  tight,
  bodyClassName,
}: CardProps) {
  const hasHeader = title != null || actions != null

  // Backward-compatible: sarlavhasiz va tight bo'lmaganda — eski "padded div" ko'rinishi
  if (!hasHeader && !tight) {
    return (
      <div
        className={cn(
          'rounded-2xl border border-slate-200/80 bg-white shadow-sm',
          !overridesPadding(className) && 'p-5',
          className,
        )}
      >
        {children}
      </div>
    )
  }

  return (
    <div
      className={cn(
        'rounded-2xl border border-slate-200/80 bg-white shadow-sm',
        className,
      )}
    >
      {hasHeader && (
        <div className="flex items-center justify-between border-b border-slate-100 px-[18px] py-4">
          <div className="min-w-0">
            {title != null && (
              <h3 className="text-sm font-semibold text-slate-800">{title}</h3>
            )}
            {sub != null && <p className="mt-0.5 text-xs font-medium text-slate-400">{sub}</p>}
          </div>
          {actions != null && <div className="flex items-center gap-2">{actions}</div>}
        </div>
      )}
      {tight ? children : <div className={cn('p-[18px]', bodyClassName)}>{children}</div>}
    </div>
  )
}
