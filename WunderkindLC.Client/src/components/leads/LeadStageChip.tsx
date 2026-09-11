import { stageColors } from '@/config/stageColors'
import type { StageColor } from '@/types'
import { cn } from '@/lib/utils'

/**
 * Lidning KANBAN BOSQICHI — kichik rangli chip (ranglar kanban ustunlari bilan bir xil).
 *
 * <p>Formalardan va daraja testidan kelgan lidning "voronkaning qayerida turgani" shu bilan
 * ko'rsatiladi: bo'lim ro'yxatiga o'tmasdan turib sotuv holati ko'rinadi.</p>
 *
 * <p>⚠️ Bosqich bo'sh bo'lsa (lid o'chirilgan yoki ustun tanlanmagan) — chiziqcha. Rang noma'lum
 * bo'lsa `slate` ga tushadi: ustun rangi keyin o'zgarib ketsa ham chip ko'rinmay qolmaydi.</p>
 */
export function LeadStageChip({
  title, color, className,
}: {
  title: string
  color: string
  className?: string
}) {
  if (!title) return <span className="text-slate-300">—</span>
  const c = stageColors[color as StageColor] ?? stageColors.slate
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-[11px] font-semibold',
        c.badge,
        className,
      )}
    >
      <span className={cn('h-1.5 w-1.5 rounded-full', c.dot)} />
      {title}
    </span>
  )
}
