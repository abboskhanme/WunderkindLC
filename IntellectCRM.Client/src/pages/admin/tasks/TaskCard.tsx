import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Archive, Eye, ListChecks, MessageSquare, Repeat, Trash2 } from 'lucide-react'
import type { WorkTask } from '@/api/services/workTasks'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { cn } from '@/lib/utils'
import { priorityOf, repeatLabel } from './model'
import { AssigneeChip, DueChip } from './shared'

export interface TaskCardActions {
  onOpen: (t: WorkTask) => void
  onArchive: (t: WorkTask) => void
  onDelete: (t: WorkTask) => void
  canWrite: boolean
  canDelete: boolean
}

/**
 * KARTOCHKA KO'RINISHI — sudrash paytidagi `DragOverlay` ham AYNAN shuni chizadi
 * ("Bog'lanish kerak" taxtasidagi `ContactCardContent` bilan bir xil naqsh).
 *
 * <p>Chap chiziq rangi — MUHIMLIK bo'yicha, ustunga qarab emas: shunda "Jarayonda" ustunidagi
 * shoshilinch topshiriq ham ko'zga tashlanadi. Kechikkan topshiriq esa qo'shimcha ravishda
 * qizil muddat yorlig'i bilan belgilanadi.</p>
 */
export function TaskCardContent({
  task,
  today,
  actions,
  dragging,
}: {
  task: WorkTask
  today: string
  /** Berilmasa karta "o'lik" bo'ladi (drag overlay). */
  actions?: TaskCardActions
  dragging?: boolean
}) {
  const p = priorityOf(task.priority)

  const menu: DropdownMenuItem[] = []
  if (actions) {
    menu.push({ label: 'Ochish', icon: Eye, onClick: () => actions.onOpen(task) })
    if (actions.canWrite)
      menu.push({
        label: task.isArchived ? 'Arxivdan qaytarish' : 'Arxivlash',
        icon: Archive,
        onClick: () => actions.onArchive(task),
      })
    if (actions.canDelete)
      menu.push({ label: "O'chirish", icon: Trash2, onClick: () => actions.onDelete(task), danger: true })
  }

  return (
    <div
      className={cn(
        'flex flex-col gap-2 rounded-lg border border-slate-200 bg-white p-3 shadow-[var(--shadow-1)] transition-shadow',
        dragging
          ? 'rotate-2 cursor-grabbing opacity-70 shadow-[var(--shadow-pop)]'
          : 'hover:shadow-[var(--shadow-2)]',
        task.isOverdue && 'bg-rose-50/60',
        task.isDone && 'opacity-75',
      )}
      style={{ borderLeftWidth: 3, borderLeftColor: p.accent }}
    >
      <div className="flex items-start justify-between gap-2">
        <p
          className={cn(
            'min-w-0 flex-1 text-[13.5px] font-semibold leading-snug tracking-tight text-slate-800',
            task.isDone && 'line-through decoration-slate-300',
          )}
        >
          {task.title}
        </p>
        {menu.length > 0 && (
          <span onPointerDown={(e) => e.stopPropagation()} onClick={(e) => e.stopPropagation()}>
            <DropdownMenu items={menu} />
          </span>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-1.5">
        <span
          className={cn(
            'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-semibold',
            p.chip,
          )}
        >
          <span className={cn('h-1.5 w-1.5 rounded-full', p.dot)} />
          {p.label}
        </span>

        <DueChip task={task} today={today} />

        {task.stepsTotal > 0 && (
          <span
            title="Bajarilgan qadamlar"
            className={cn(
              'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-semibold',
              task.stepsDone === task.stepsTotal
                ? 'bg-emerald-50 text-emerald-600'
                : 'bg-slate-100 text-slate-500',
            )}
          >
            <ListChecks className="h-3 w-3" />
            {task.stepsDone}/{task.stepsTotal}
          </span>
        )}

        {task.commentCount > 0 && (
          <span
            title="Izohlar"
            className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[10.5px] font-semibold text-slate-500"
          >
            <MessageSquare className="h-3 w-3" />
            {task.commentCount}
          </span>
        )}

        {task.repeat !== 'none' && (
          <span
            title={repeatLabel(task.repeat)}
            className="inline-flex items-center gap-1 rounded-full bg-violet-50 px-2 py-0.5 text-[10.5px] font-semibold text-violet-600"
          >
            <Repeat className="h-3 w-3" />
          </span>
        )}
      </div>

      {task.tags.length > 0 && (
        <div className="flex flex-wrap gap-1">
          {task.tags.slice(0, 4).map((tag) => (
            <span
              key={tag}
              className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium text-slate-500"
            >
              #{tag}
            </span>
          ))}
        </div>
      )}

      <div className="flex items-center justify-between gap-2 border-t border-slate-100 pt-2">
        <AssigneeChip task={task} />
      </div>
    </div>
  )
}

/** SUDRALADIGAN kartochka (doska ko'rinishi uchun). */
export function TaskCard({
  task,
  today,
  actions,
  onClick,
}: {
  task: WorkTask
  today: string
  actions: TaskCardActions
  onClick: () => void
}) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: task.id,
    disabled: !actions.canWrite,
  })

  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: CSS.Translate.toString(transform) } : undefined}
      {...listeners}
      {...attributes}
      onClick={onClick}
      className={cn('touch-none', actions.canWrite && 'cursor-grab', isDragging && 'opacity-40')}
    >
      <TaskCardContent task={task} today={today} actions={actions} />
    </div>
  )
}
