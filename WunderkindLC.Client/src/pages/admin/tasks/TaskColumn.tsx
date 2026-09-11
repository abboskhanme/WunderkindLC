import { useDroppable } from '@dnd-kit/core'
import { Plus } from 'lucide-react'
import type { WorkTask, WorkTaskColumn as Column } from '@/api/services/workTasks'
import { stageColors } from '@/config/stageColors'
import { cn } from '@/lib/utils'
import { TaskCard, type TaskCardActions } from './TaskCard'

/**
 * Doskaning BITTA ustuni — lidlar va "Bog'lanish kerak" taxtalaridagi bilan AYNI CSS
 * (`.kanban-col*`), ya'ni uch taxta ham bir xil ko'rinadi.
 *
 * <p>Ustun sarlavhasidagi "+" — AYNAN shu ustunga topshiriq qo'shadi (yangi topshiriq oynasi
 * shu ustun tanlangan holda ochiladi): "Tekshiruvda" ustuniga topshiriq qo'shish uchun avval
 * yaratib, keyin sudrash kerak bo'lmasin.</p>
 */
export function TaskColumn({
  column,
  tasks,
  today,
  actions,
  accepts,
  dragging,
  onAdd,
  onCardClick,
}: {
  column: Column
  tasks: WorkTask[]
  today: string
  actions: TaskCardActions
  /** Hozir sudralayotgan karta uchun ustun ochiqmi. */
  accepts: boolean
  dragging?: boolean
  onAdd?: (column: Column) => void
  onCardClick: (t: WorkTask) => void
}) {
  const c = stageColors[column.color] ?? stageColors.slate
  const { setNodeRef, isOver } = useDroppable({ id: column.id, disabled: !accepts })

  return (
    <div className={cn('kanban-col w-[300px] shrink-0', c.tint)}>
      <div className={cn('h-1 w-full', c.bar)} />

      <div className="kanban-col-head">
        <div className="name min-w-0">
          <span className={cn('stage-dot', c.swatch)} />
          <span className="truncate">{column.title}</span>
        </div>
        <span className={cn('count', c.badge)}>{tasks.length}</span>
        <span className="spacer flex-1" />
        {onAdd && (
          <button
            type="button"
            title="Shu ustunga topshiriq qo'shish"
            onClick={() => onAdd(column)}
            className="rounded-md p-1 text-slate-400 transition-colors hover:bg-white hover:text-brand-600"
          >
            <Plus className="h-4 w-4" />
          </button>
        )}
      </div>

      <div
        ref={setNodeRef}
        className={cn(
          'kanban-col-body max-h-[calc(100vh-24rem)] min-h-[120px] overflow-y-auto',
          isOver && 'drag-over',
          isOver && `ring-2 ring-inset ${c.ring}`,
          // Qabul qilmaydigan ustun sudrash paytida xiralashadi — "bu yerga bo'lmaydi".
          dragging && !accepts && 'opacity-40',
        )}
      >
        {tasks.map((t) => (
          <TaskCard
            key={t.id}
            task={t}
            today={today}
            actions={actions}
            onClick={() => onCardClick(t)}
          />
        ))}
        {tasks.length === 0 && (
          <p className="px-2 py-8 text-center text-xs text-slate-400">Bo'sh</p>
        )}
      </div>
    </div>
  )
}
