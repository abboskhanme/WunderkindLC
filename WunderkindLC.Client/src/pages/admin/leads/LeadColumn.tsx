import { useDroppable } from '@dnd-kit/core'
import type { Icon } from '@tabler/icons-react'
import { IconChevronLeft, IconChevronRight, IconPencil, IconTrash } from '@tabler/icons-react'
import type { Lead, Stage } from '@/types'
import { cn } from '@/lib/utils'
import { LeadCard } from './LeadCard'

interface Props {
  stage: Stage
  leads: Lead[]
  isFirst: boolean
  isLast: boolean
  onCardClick: (lead: Lead) => void
  onEdit: (stage: Stage) => void
  onDelete: (stage: Stage) => void
  onMove: (id: string, dir: -1 | 1) => void
  /** Telefon ikonkasi bosilganda qo'ng'iroq oynasini ochish */
  onCall?: (lead: Lead) => void
}

/**
 * Bosqich RANGI → edutizim sarlavhasi ostidagi 2px chiziq rangi. Tailwind klassi emas, HEX:
 * chiziq ingichka, `stageColors[...].bar` esa kartochkadagi boshqa elementlar uchun.
 * Noma'lum rang (eski yoki qo'lda kiritilgan qiymat) kulrangga tushadi.
 */
const ruleColors: Record<string, string> = {
  slate: '#9e9e9e',
  blue: '#3D68FF',
  emerald: '#00D66F',
  amber: '#FBC400',
  violet: '#9747FF',
  rose: '#DE4141',
  cyan: '#019EF7',
  orange: '#ED6C02',
}

/**
 * edutizim kanban ustuni: 350px, sarlavha — qalin KATTA HARFLI nom + ostida kulrang "N Lidlar" va
 * bosqich rangidagi 2px chiziq; tanasi kulrang (`#F0F2F2`) radius 8.
 * Ustunni boshqarish (ko'chirish/tahrirlash/o'chirish) — sarlavhaga kursor olib borilganda.
 */
export function LeadColumn({
  stage,
  leads,
  isFirst,
  isLast,
  onCardClick,
  onEdit,
  onDelete,
  onMove,
  onCall,
}: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: stage.id })
  const rule = ruleColors[stage.color] ?? ruleColors.slate

  return (
    <div className="flex w-[350px] shrink-0 flex-col">
      <div className="group relative px-2 pb-1.5 text-center">
        <p className="truncate text-[13px] font-bold uppercase text-black" title={stage.title}>
          {stage.title}
        </p>
        <p className="text-[11px] text-[#757575]">{leads.length} Lidlar</p>
        <div className="absolute right-0 top-0 flex items-center rounded-md bg-white/90 opacity-0 shadow-sm transition-opacity focus-within:opacity-100 group-hover:opacity-100">
          <HBtn icon={IconChevronLeft} title="Chapga" disabled={isFirst} onClick={() => onMove(stage.id, -1)} />
          <HBtn icon={IconChevronRight} title="O'ngga" disabled={isLast} onClick={() => onMove(stage.id, 1)} />
          <HBtn icon={IconPencil} title="Tahrirlash" onClick={() => onEdit(stage)} />
          <HBtn icon={IconTrash} title="O'chirish" danger onClick={() => onDelete(stage)} />
        </div>
      </div>
      <div className="h-0.5 w-full rounded-full" style={{ background: rule }} />

      <div
        ref={setNodeRef}
        className={cn(
          'mt-2 flex min-h-[200px] flex-1 flex-col gap-2 rounded-lg bg-[#F0F2F2] p-1.5 transition-shadow',
          isOver && 'shadow-[inset_0_0_0_2px_rgba(61,104,255,0.35)]',
        )}
      >
        {leads.map((lead) => (
          <LeadCard key={lead.id} lead={lead} onClick={() => onCardClick(lead)} onCall={onCall} />
        ))}
      </div>
    </div>
  )
}

interface HBtnProps {
  icon: Icon
  title: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}

function HBtn({ icon: I, title, onClick, disabled, danger }: HBtnProps) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-md p-1 transition-colors disabled:opacity-30 disabled:hover:bg-transparent',
        danger ? 'text-[#757575] hover:bg-red-50 hover:text-red-600' : 'text-[#757575] hover:bg-[#F0F2F2] hover:text-black',
      )}
    >
      <I className="h-3.5 w-3.5" />
    </button>
  )
}
