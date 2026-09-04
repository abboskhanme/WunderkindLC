import type { ReactNode } from 'react'
import { Search, TriangleAlert } from 'lucide-react'
import { usePerm } from '@/lib/permissions'
import { avatarColor, initials } from '@/lib/avatar'
import { taskTabs } from '@/config/sectionTabs'
import { CardTabs } from '@/components/ui/CardTabs'
import { PageHeader } from '@/components/ui/PageHeader'
import { Input, Select } from '@/components/ui/Input'
import { cn } from '@/lib/utils'
import type { WorkTask, WorkTaskAssignee, WorkTaskBoard } from '@/api/services/workTasks'
import { PRIORITIES, dueLabel, type TaskFilters } from './model'

/* =====================================================================================
 *  TOPSHIRIQLAR bo'limining UMUMIY KO'RINISH bo'laklari
 * =====================================================================================
 *  Sarlavha, ko'rinishlar qatori, filtr paneli va kichik yorliqlar — doska, ro'yxat va
 *  kalendarda AYNAN bir xil bo'lishi kerak. Mantiq (sana/muhimlik/filtr) `model.ts` da.
 */

/**
 * Bo'limning UMUMIY qobig'i: sarlavha + ko'rinishlar qatori (CardTabs). Har sahifa faqat o'z
 * mazmunini yozadi — sarlavha va tablar bir xil bo'lib qoladi.
 */
export function TasksShell({
  sub,
  actions,
  children,
}: {
  sub?: ReactNode
  actions?: ReactNode
  children: ReactNode
}) {
  const { can } = usePerm()
  return (
    <div>
      <PageHeader title="Topshiriqlar" sub={sub} actions={actions} />
      <CardTabs
        items={taskTabs((perm) => can(perm, 'view'))}
        className="mb-5"
      />
      {children}
    </div>
  )
}

/**
 * Uchala ko'rinish uchun UMUMIY filtr paneli: doska · mas'ul · muhimlik · holat · qidiruv.
 * <paramref name="hideStatus"/> — kalendarda holat filtri ortiqcha (u muddat bo'yicha
 * ishlaydi), shuning uchun yashiriladi.
 */
export function TaskFilterBar({
  boards,
  boardId,
  onBoard,
  assignees,
  filters,
  onChange,
  hideStatus,
  right,
}: {
  boards: WorkTaskBoard[]
  boardId: string
  onBoard: (id: string) => void
  assignees: WorkTaskAssignee[]
  filters: TaskFilters
  onChange: (f: TaskFilters) => void
  hideStatus?: boolean
  right?: ReactNode
}) {
  const set = (patch: Partial<TaskFilters>) => onChange({ ...filters, ...patch })

  return (
    <div className="mb-4 flex flex-wrap items-center gap-2">
      <Select value={boardId} onChange={(e) => onBoard(e.target.value)} className="w-auto">
        {boards.map((b) => (
          <option key={b.id} value={b.id}>
            {b.title}
          </option>
        ))}
      </Select>

      <Select
        value={filters.assigneeId}
        onChange={(e) => set({ assigneeId: e.target.value })}
        className="w-auto"
      >
        <option value="">Barcha mas'ullar</option>
        {assignees.map((a) => (
          <option key={a.userId} value={a.userId}>
            {a.fullName}
          </option>
        ))}
      </Select>

      <Select
        value={filters.priority}
        onChange={(e) => set({ priority: e.target.value })}
        className="w-auto"
      >
        <option value="">Barcha muhimlik</option>
        {PRIORITIES.map((p) => (
          <option key={p.value} value={String(p.value)}>
            {p.label}
          </option>
        ))}
      </Select>

      {!hideStatus && (
        <Select
          value={filters.status}
          onChange={(e) => set({ status: e.target.value })}
          className="w-auto"
        >
          <option value="">Barcha holat</option>
          <option value="open">Ochiq</option>
          <option value="today">Bugungi</option>
          <option value="overdue">Kechikkan</option>
          <option value="done">Bajarilgan</option>
        </Select>
      )}

      <div className="relative min-w-[12rem] flex-1">
        <Search className="pointer-events-none absolute left-3 top-1/2 z-10 h-4 w-4 -translate-y-1/2 text-slate-400" />
        <Input
          placeholder="Topshiriq nomi bo'yicha qidirish"
          value={filters.q}
          onChange={(e) => set({ q: e.target.value })}
          className="pl-9"
        />
      </div>

      {right}
    </div>
  )
}

/** Muddat yorlig'i — kechikkanda qizil, bugun sariq, aks holda betaraf. */
export function DueChip({ task, today }: { task: WorkTask; today: string }) {
  if (!task.dueDate) return null
  const late = task.isOverdue
  const now = !late && task.dueDate === today && !task.isDone
  return (
    <span
      title={`Muddat: ${task.dueDate}${task.dueTime ? ` ${task.dueTime}` : ''}`}
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-semibold',
        late
          ? 'bg-rose-100 text-rose-700'
          : now
            ? 'bg-amber-100 text-amber-700'
            : 'bg-slate-100 text-slate-500',
      )}
    >
      {late && <TriangleAlert className="h-3 w-3" />}
      {dueLabel(task.dueDate, today)}
      {task.dueTime ? ` ${task.dueTime}` : ''}
    </span>
  )
}

/** Mas'ul avatari + ismi (biriktirilmagan bo'lsa — kulrang eslatma). */
export function AssigneeChip({ task }: { task: WorkTask }) {
  if (!task.assigneeId)
    return <span className="text-[11px] font-medium text-slate-400">Biriktirilmagan</span>
  return (
    <span className="inline-flex min-w-0 items-center gap-1.5">
      <Avatar name={task.assigneeName} url={task.assigneeAvatarUrl} />
      <span className="truncate text-[11.5px] font-medium text-slate-600">{task.assigneeName}</span>
    </span>
  )
}

/** Kichik avatar — rasm bo'lsa rasm, aks holda ismdan bosh harflar (lidlar taxtasi bilan bir xil). */
export function Avatar({
  name,
  url,
  size = 22,
}: {
  name: string
  url?: string | null
  size?: number
}) {
  if (url)
    return (
      <img
        src={url}
        alt={name}
        style={{ width: size, height: size }}
        className="shrink-0 rounded-full object-cover"
      />
    )
  return (
    <span
      className="avatar shrink-0 text-[10px]"
      style={{ width: size, height: size, background: avatarColor(name) }}
    >
      {initials(name)}
    </span>
  )
}
