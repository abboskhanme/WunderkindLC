import { useCallback, useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight, Plus } from 'lucide-react'
import { getTasks, type WorkTask } from '@/api/services/workTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage } from '@/lib/utils'
import { TaskModal } from './TaskModal'
import {
  EMPTY_FILTERS, dayIso, priorityOf, toQuery, todayIso, useBoardParam, useTasksMeta,
  type TaskFilters,
} from './model'
import { TaskFilterBar, TasksShell } from './shared'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]
/** Hafta DUSHANBADAN boshlanadi (O'zbekistondagi ish haftasi). */
const WEEKDAYS = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

/** Oyning kalendar TO'RI: oldingi/keyingi oydan to'ldirilgan 6 hafta (42 kun). */
function monthGrid(anchor: Date): Date[] {
  const first = new Date(anchor.getFullYear(), anchor.getMonth(), 1)
  // getDay(): 0 = yakshanba → dushanbadan boshlanadigan indeksga o'giramiz.
  const shift = (first.getDay() + 6) % 7
  const start = new Date(first)
  start.setDate(first.getDate() - shift)
  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(start)
    d.setDate(start.getDate() + i)
    return d
  })
}

/**
 * KALENDAR ko'rinishi — topshiriqlar MUDDATI bo'yicha oylik to'rda.
 *
 * <p>Nima uchun kerak: doska "qaysi bosqichda" savoliga javob beradi, kalendar esa "qaysi kunga
 * qancha ish yig'ilib qolgan" degan savolga. Yuklamani oldindan ko'rib, topshiriqni bo'sh kunga
 * surish oson bo'ladi. Bo'sh kunga bosilsa — o'sha muddat bilan yangi topshiriq oynasi ochiladi.</p>
 */
export function TasksCalendarPage() {
  const { can } = usePerm()
  const canCreate = can('tasks.board', 'create')
  const canEdit = can('tasks.board', 'edit')

  const { boards, assignees, loading, reload } = useTasksMeta()
  const [boardId, setBoardId] = useBoardParam(boards)
  const [filters, setFilters] = useState<TaskFilters>(EMPTY_FILTERS)
  const [anchor, setAnchor] = useState(() => new Date())
  const [tasks, setTasks] = useState<WorkTask[]>([])
  const [busy, setBusy] = useState(false)
  const [modal, setModal] = useState<{ open: boolean; taskId: string | null; due?: string }>({
    open: false,
    taskId: null,
  })

  const today = todayIso()
  const board = boards.find((b) => b.id === boardId) ?? null
  const grid = useMemo(() => monthGrid(anchor), [anchor])

  const load = useCallback(() => {
    if (!boardId || grid.length === 0) return
    setBusy(true)
    getTasks(toQuery(boardId, filters, { from: dayIso(grid[0]), to: dayIso(grid[41]) }))
      .then(setTasks)
      .catch((err) => alert(apiErrorMessage(err, "Topshiriqlarni yuklab bo'lmadi")))
      .finally(() => setBusy(false))
  }, [boardId, filters, grid])

  useEffect(() => {
    const id = setTimeout(load, filters.q ? 300 : 0)
    return () => clearTimeout(id)
  }, [load, filters.q])

  const byDay = useMemo(() => {
    const map = new Map<string, WorkTask[]>()
    for (const t of tasks) {
      if (!t.dueDate) continue
      const list = map.get(t.dueDate)
      if (list) list.push(t)
      else map.set(t.dueDate, [t])
    }
    for (const list of map.values()) list.sort((a, b) => b.priority - a.priority)
    return map
  }, [tasks])

  const shiftMonth = (delta: number) =>
    setAnchor((d) => new Date(d.getFullYear(), d.getMonth() + delta, 1))

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <TasksShell
      sub="Topshiriqlar muddati bo'yicha oylik ko'rinish — kun yuklamasi bir qarashda"
      actions={
        canCreate &&
        boards.length > 0 && (
          <Button onClick={() => setModal({ open: true, taskId: null })}>
            <Plus className="h-4 w-4" /> Yangi topshiriq
          </Button>
        )
      }
    >
      <TaskFilterBar
        boards={boards}
        boardId={boardId}
        onBoard={setBoardId}
        assignees={assignees}
        filters={filters}
        onChange={setFilters}
        hideStatus
      />

      <Card
        tight
        title={`${MONTHS[anchor.getMonth()]} ${anchor.getFullYear()}`}
        actions={
          <div className="flex items-center gap-1">
            <button
              type="button"
              title="Oldingi oy"
              onClick={() => shiftMonth(-1)}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <Button variant="secondary" onClick={() => setAnchor(new Date())}>
              Bugun
            </Button>
            <button
              type="button"
              title="Keyingi oy"
              onClick={() => shiftMonth(1)}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        }
      >
        <div className="grid grid-cols-7 border-b border-slate-100 bg-slate-50/70">
          {WEEKDAYS.map((w) => (
            <div
              key={w}
              className="px-2 py-2 text-center text-[11px] font-bold uppercase tracking-wide text-slate-400"
            >
              {w}
            </div>
          ))}
        </div>

        <div className="grid grid-cols-7">
          {grid.map((d) => {
            const iso = dayIso(d)
            const inMonth = d.getMonth() === anchor.getMonth()
            const isToday = iso === today
            const items = byDay.get(iso) ?? []
            const weekend = d.getDay() === 0

            return (
              <div
                key={iso}
                className={cn(
                  'min-h-[104px] border-b border-r border-slate-100 p-1.5',
                  !inMonth && 'bg-slate-50/60',
                  weekend && inMonth && 'bg-rose-50/30',
                )}
              >
                <div className="mb-1 flex items-center justify-between">
                  <span
                    className={cn(
                      'inline-flex h-6 min-w-6 items-center justify-center rounded-full px-1.5 text-[11.5px] font-semibold',
                      isToday
                        ? 'bg-brand-600 text-white'
                        : inMonth
                          ? 'text-slate-600'
                          : 'text-slate-300',
                    )}
                  >
                    {d.getDate()}
                  </span>
                  {canCreate && (
                    <button
                      type="button"
                      title="Shu kunga topshiriq"
                      onClick={() => setModal({ open: true, taskId: null, due: iso })}
                      className="rounded p-0.5 text-slate-300 opacity-50 transition-colors hover:bg-brand-50 hover:text-brand-600 hover:opacity-100"
                    >
                      <Plus className="h-3.5 w-3.5" />
                    </button>
                  )}
                </div>

                <div className="space-y-1">
                  {items.slice(0, 3).map((t) => {
                    const p = priorityOf(t.priority)
                    return (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => setModal({ open: true, taskId: t.id })}
                        title={`${t.title}${t.assigneeName ? ` — ${t.assigneeName}` : ''}`}
                        className={cn(
                          'flex w-full items-center gap-1 rounded px-1.5 py-1 text-left text-[11px] font-medium transition-colors hover:bg-slate-100',
                          t.isDone ? 'text-slate-400 line-through' : 'text-slate-700',
                          t.isOverdue && 'bg-rose-50',
                        )}
                      >
                        <span
                          className="h-1.5 w-1.5 shrink-0 rounded-full"
                          style={{ background: p.accent }}
                        />
                        <span className="truncate">{t.title}</span>
                      </button>
                    )
                  })}
                  {items.length > 3 && (
                    <p className="px-1.5 text-[10.5px] font-medium text-slate-400">
                      +{items.length - 3} ta
                    </p>
                  )}
                </div>
              </div>
            )
          })}
        </div>

        {busy && <p className="px-4 py-2 text-xs text-slate-400">Yangilanmoqda...</p>}
      </Card>

      <TaskModal
        open={modal.open}
        onClose={() => setModal({ open: false, taskId: null })}
        board={board}
        assignees={assignees}
        taskId={modal.taskId}
        defaultDueDate={modal.due}
        canWrite={modal.taskId ? canEdit : canCreate}
        onSaved={() => {
          load()
          reload()
        }}
      />
    </TasksShell>
  )
}
