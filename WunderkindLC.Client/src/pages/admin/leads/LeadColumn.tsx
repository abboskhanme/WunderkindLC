import { useDroppable } from '@dnd-kit/core'
import type { LucideIcon } from 'lucide-react'
import { ChevronLeft, ChevronRight, Pencil, Trash2 } from 'lucide-react'
import type { Lead, Stage } from '@/types'
import { stageColors } from '@/config/stageColors'
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
  /** Telefon raqami bosilganda qo'ng'iroq oynasini ochish */
  onCall?: (lead: Lead) => void
}

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
  // Noma'lum rang (eski yoki qo'lda kiritilgan qiymat) butun sahifani buzmasin.
  const c = stageColors[stage.color] ?? stageColors.slate
  const { setNodeRef, isOver } = useDroppable({ id: stage.id })

  return (
    // Ustun foni — BOSQICH rangidagi och tint (`c.tint`), tepasida to'q rangli chiziq.
    // Fon shu yerda beriladi, `.kanban-col` CSS'ida emas (izoh: index.css → Kanban).
    <div className={cn('kanban-col w-72 shrink-0', c.tint)}>
      <div className={cn('h-1 w-full', c.bar)} />

      <div className="kanban-col-head">
        <div className="name min-w-0">
          <span className={cn('stage-dot', c.dot)} />
          <span className="truncate">{stage.title}</span>
        </div>
        <span className={cn('count', c.badge)}>{leads.length}</span>
        <span className="flex-1" />
        <div className="flex items-center">
          <HBtn icon={ChevronLeft} title="Chapga" disabled={isFirst} onClick={() => onMove(stage.id, -1)} />
          <HBtn icon={ChevronRight} title="O'ngga" disabled={isLast} onClick={() => onMove(stage.id, 1)} />
          <HBtn icon={Pencil} title="Tahrirlash" onClick={() => onEdit(stage)} />
          <HBtn icon={Trash2} title="O'chirish" danger onClick={() => onDelete(stage)} />
        </div>
      </div>

      <div ref={setNodeRef} className={cn('kanban-col-body', isOver && 'drag-over')}>
        {leads.map((lead) => (
          <LeadCard key={lead.id} lead={lead} onClick={() => onCardClick(lead)} onCall={onCall} />
        ))}
        {leads.length === 0 && (
          <p className="px-2 py-6 text-center text-xs text-slate-400">Bo'sh</p>
        )}
      </div>
    </div>
  )
}

interface HBtnProps {
  icon: LucideIcon
  title: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}

function HBtn({ icon: Icon, title, onClick, disabled, danger }: HBtnProps) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-md p-1 transition-colors disabled:opacity-30 disabled:hover:bg-transparent',
        danger
          ? 'text-slate-500 hover:bg-red-100 hover:text-red-600'
          : 'text-slate-500 hover:bg-white hover:text-slate-700',
      )}
    >
      <Icon className="h-3.5 w-3.5" />
    </button>
  )
}
