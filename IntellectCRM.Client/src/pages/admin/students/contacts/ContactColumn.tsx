import { useDroppable } from '@dnd-kit/core'
import { Flame } from 'lucide-react'
import type { ContactRequestItem } from '@/api/services/contacts'
import type { BoardColumn } from '@/lib/contactDue'
import { stageColors } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import { ContactCard, type ContactCardActions } from './ContactCard'

/**
 * Taxtaning BITTA ustuni (lidlar taxtasidagi `LeadColumn` bilan bir xil naqsh va CSS).
 *
 * <p>Ustun karta qabul qilishi `column.drop` bilan belgilanadi — qabul qilmaydigan ustun
 * (masalan "Bog'lanish kerak") sudralayotgan kartani umuman "yoqmaydi", ya'ni foydalanuvchi
 * server rad etadigan amalni boshlay olmaydi.</p>
 */
export function ContactColumn({
  column,
  items,
  today,
  actions,
  onCardClick,
  /** "Bugun qilish kerak" ga kiradigan ustun — sarlavhasida alanga belgisi. */
  urgent,
  dragging,
}: {
  column: BoardColumn
  items: ContactRequestItem[]
  today: string
  actions: ContactCardActions
  onCardClick: (r: ContactRequestItem) => void
  urgent?: boolean
  /** Hozir biror karta sudralyaptimi — qabul qilmaydigan ustunni xiralashtirish uchun. */
  dragging?: boolean
}) {
  const c = stageColors[column.color]
  const accepts = !!column.drop && actions.canWrite
  const { setNodeRef, isOver } = useDroppable({ id: column.key, disabled: !accepts })

  return (
    <div className="kanban-col w-[300px] shrink-0 overflow-hidden">
      {/* Ustun rangi — tepadagi ingichka chiziq */}
      <div className={cn('h-1 w-full', c.bar)} />

      <div className="kanban-col-head" title={column.hint}>
        <div className="name min-w-0">
          <span className={cn('stage-dot', c.swatch)} />
          <span className="truncate">{column.label}</span>
        </div>
        <span className="count">{items.length}</span>
        <span className="spacer flex-1" />
        {urgent && (
          <span title="«Bugun qilish kerak» ga kiradi" className="text-amber-500">
            <Flame className="h-3.5 w-3.5" />
          </span>
        )}
      </div>

      <div
        ref={setNodeRef}
        className={cn(
          'kanban-col-body max-h-[calc(100vh-22rem)] min-h-[120px] overflow-y-auto',
          isOver && 'drag-over',
          isOver && `ring-2 ring-inset ${c.ring}`,
          // Qabul qilmaydigan ustun sudrash paytida xiralashadi — "bu yerga bo'lmaydi".
          dragging && !accepts && 'opacity-40',
        )}
      >
        {items.map((r) => (
          <ContactCard
            key={r.id}
            r={r}
            today={today}
            actions={actions}
            onClick={() => onCardClick(r)}
          />
        ))}
        {items.length === 0 && (
          <p className="px-2 py-8 text-center text-xs text-slate-400">Bo'sh</p>
        )}
      </div>
    </div>
  )
}
