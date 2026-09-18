import type { ReactNode } from 'react'
import { IconDotsVertical, IconFilter, IconPlus } from '@tabler/icons-react'
import { cn } from '@/lib/utils'
import { TintedIconButton } from './TintedIconButton'

interface ListToolbarProps {
  /** Chap tomon (ko'rinish almashtirgich, tablar ...). */
  left?: ReactNode
  /** Filtr panelini ochish/yopish — berilsa voronka tugmasi chiqadi. */
  filtersOpen?: boolean
  onToggleFilters?: () => void
  /** "⋮" tugmasi (masalan qatorlarni tanlash rejimi). */
  onMore?: () => void
  moreActive?: boolean
  /** O'ng tomondagi qo'shimcha tugmalar (asosiy tugmadan oldin). */
  extra?: ReactNode
  /** Asosiy "… qo'shish" tugmasi. */
  addLabel?: string
  onAdd?: () => void
  className?: string
}

/**
 * edutizim ro'yxat sahifasining asboblar qatori: sahifada SARLAVHA YO'Q — birinchi qator shu.
 * O'ngda: voronka (filtrlar) · qo'shimchalar · "⋮" · ko'k "+ … qo'shish".
 */
export function ListToolbar({
  left,
  filtersOpen,
  onToggleFilters,
  onMore,
  moreActive,
  extra,
  addLabel,
  onAdd,
  className,
}: ListToolbarProps) {
  return (
    <div className={cn('mb-2.5 flex flex-wrap items-center gap-2', className)}>
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">{left}</div>
      <div className="flex flex-wrap items-center gap-2">
        {onToggleFilters && (
          <TintedIconButton label="Filtrlar" pressed={filtersOpen} onClick={onToggleFilters}>
            <IconFilter className="h-5 w-5" />
          </TintedIconButton>
        )}
        {extra}
        {onMore && (
          <TintedIconButton label="Ko'proq" pressed={moreActive} onClick={onMore}>
            <IconDotsVertical className="h-5 w-5" />
          </TintedIconButton>
        )}
        {addLabel && onAdd && (
          <button
            type="button"
            onClick={onAdd}
            className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-1 text-xs font-medium text-white transition-colors hover:bg-brand-700"
          >
            <IconPlus className="h-5 w-5" />
            {addLabel}
          </button>
        )}
      </div>
    </div>
  )
}
