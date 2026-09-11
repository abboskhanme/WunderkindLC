import { useDroppable } from '@dnd-kit/core'
import { ChevronLeft, ChevronRight, Flame, Lock, Pencil, Trash2 } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { ContactRequestItem } from '@/api/services/contacts'
import type { BoardColumn } from '@/lib/contactDue'
import { stageColors } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import { ContactCard, type ContactCardActions } from './ContactCard'

/** Ustun sarlavhasidagi ustunlarni boshqarish amallari (faqat "Bosqich" rejimida). */
export interface ContactColumnAdmin {
  onEdit: (column: BoardColumn) => void
  onDelete: (column: BoardColumn) => void
  onMove: (column: BoardColumn, dir: -1 | 1) => void
  isFirst: boolean
  isLast: boolean
}

/**
 * Taxtaning BITTA ustuni (lidlar taxtasidagi `LeadColumn` bilan bir xil naqsh va CSS).
 *
 * <p>Ustun karta qabul qilishi <c>accepts</c> bilan belgilanadi — qaror BOARD tomonida,
 * ayni paytda sudralayotgan kartaga qarab beriladi (o'sha holatdagi karta uchun ochiq,
 * boshqa holatdagi uchun yopiq bo'lishi mumkin). Qabul qilmaydigan ustun sudrash paytida
 * xiralashadi, ya'ni foydalanuvchi server rad etadigan amalni BOSHLAY olmaydi.</p>
 */
export function ContactColumn({
  column,
  items,
  today,
  actions,
  onCardClick,
  accepts,
  urgent,
  dragging,
  admin,
}: {
  column: BoardColumn
  items: ContactRequestItem[]
  today: string
  actions: ContactCardActions
  onCardClick: (r: ContactRequestItem) => void
  /** Hozirgi sudralayotgan karta uchun bu ustun ochiqmi. */
  accepts: boolean
  /** "Bugun qilish kerak" ga kiradigan ustun — sarlavhasida alanga belgisi. */
  urgent?: boolean
  /** Hozir biror karta sudralyaptimi. */
  dragging?: boolean
  /** Berilsa — sarlavhada ustunni tahrirlash/o'chirish/ko'chirish tugmalari chiqadi. */
  admin?: ContactColumnAdmin
}) {
  const c = stageColors[column.color]
  const { setNodeRef, isOver } = useDroppable({ id: column.key, disabled: !accepts })
  const system = column.stage?.isSystem

  return (
    <div className={cn('kanban-col w-[300px] shrink-0', c.tint)}>
      {/* Ustun rangi — tepadagi ingichka chiziq */}
      <div className={cn('h-1 w-full', c.bar)} />

      <div className="kanban-col-head" title={column.hint}>
        <div className="name min-w-0">
          <span className={cn('stage-dot', c.swatch)} />
          <span className="truncate">{column.label}</span>
        </div>
        <span className={cn('count', c.badge)}>{items.length}</span>
        <span className="spacer flex-1" />

        {urgent && (
          <span title="«Bugun qilish kerak» ga kiradi" className="text-amber-500">
            <Flame className="h-3.5 w-3.5" />
          </span>
        )}

        {admin && (
          <div className="flex items-center">
            <HBtn icon={ChevronLeft} title="Chapga" disabled={admin.isFirst}
              onClick={() => admin.onMove(column, -1)} />
            <HBtn icon={ChevronRight} title="O'ngga" disabled={admin.isLast}
              onClick={() => admin.onMove(column, 1)} />
            <HBtn icon={Pencil} title="Tahrirlash" onClick={() => admin.onEdit(column)} />
            {system ? (
              // Tizim ustuni o'chirilmaydi — u o'z holatidagi kartalar uchun "uy".
              <span
                title="Tizim ustuni — o'chirib bo'lmaydi"
                className="p-1 text-slate-300"
              >
                <Lock className="h-3.5 w-3.5" />
              </span>
            ) : (
              <HBtn icon={Trash2} title="O'chirish" danger onClick={() => admin.onDelete(column)} />
            )}
          </div>
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

function HBtn({
  icon: Icon, title, onClick, disabled, danger,
}: {
  icon: LucideIcon
  title: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-md p-1 transition-colors disabled:opacity-30 disabled:hover:bg-transparent',
        danger
          ? 'text-slate-400 hover:bg-red-100 hover:text-red-600'
          : 'text-slate-400 hover:bg-white hover:text-slate-700',
      )}
    >
      <Icon className="h-3.5 w-3.5" />
    </button>
  )
}
