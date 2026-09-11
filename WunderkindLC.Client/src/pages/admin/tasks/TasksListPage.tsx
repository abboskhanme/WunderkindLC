import { useCallback, useEffect, useMemo, useState } from 'react'
import { Archive, ArrowRight, Inbox, Plus, Trash2 } from 'lucide-react'
import { archiveTask, deleteTask, getTasks, type WorkTask } from '@/api/services/workTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Select } from '@/components/ui/Input'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage, exportToCsv } from '@/lib/utils'
import { TaskModal } from './TaskModal'
import {
  EMPTY_FILTERS, priorityOf, toQuery, todayIso, useBoardParam, useTasksMeta, type TaskFilters,
} from './model'
import { AssigneeChip, DueChip, TaskFilterBar, TasksShell } from './shared'

type SortKey = 'due' | 'priority' | 'created' | 'assignee'

/**
 * RO'YXAT (jadval) ko'rinishi — doska bilan AYNI ma'lumot, lekin bir ekranda ko'proq topshiriq
 * ko'rinadi va saralash bor. Kunlik nazorat uchun qulay: "kimda nima kechikkan" savoliga
 * doskadan ko'ra tezroq javob beradi. CSV eksporti — yig'ilishga chiqarish uchun.
 */
export function TasksListPage() {
  const { can } = usePerm()
  const canCreate = can('tasks.board', 'create')
  const canEdit = can('tasks.board', 'edit')
  const canDelete = can('tasks.board', 'delete')

  const { boards, assignees, loading, reload } = useTasksMeta()
  const [boardId, setBoardId] = useBoardParam(boards)
  const [filters, setFilters] = useState<TaskFilters>(EMPTY_FILTERS)
  const [sort, setSort] = useState<SortKey>('due')
  const [archived, setArchived] = useState(false)

  const [tasks, setTasks] = useState<WorkTask[]>([])
  const [busy, setBusy] = useState(false)
  const [modal, setModal] = useState<{ open: boolean; taskId: string | null }>({
    open: false,
    taskId: null,
  })

  const today = todayIso()
  const board = boards.find((b) => b.id === boardId) ?? null
  const columnTitle = useMemo(() => {
    const map = new Map<string, string>()
    for (const b of boards) for (const c of b.columns) map.set(c.id, c.title)
    return map
  }, [boards])

  const load = useCallback(() => {
    if (!boardId) return
    setBusy(true)
    getTasks(toQuery(boardId, filters, { archived }))
      .then(setTasks)
      .catch((err) => alert(apiErrorMessage(err, "Topshiriqlarni yuklab bo'lmadi")))
      .finally(() => setBusy(false))
  }, [boardId, filters, archived])

  useEffect(() => {
    const id = setTimeout(load, filters.q ? 300 : 0)
    return () => clearTimeout(id)
  }, [load, filters.q])

  const rows = useMemo(() => {
    const copy = [...tasks]
    copy.sort((a, b) => {
      switch (sort) {
        // Muddatsizlar ATAYIN oxirida: ular "hozir qilinadigan ish" emas.
        case 'due':
          return (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999')
        case 'priority':
          return b.priority - a.priority
        case 'assignee':
          return a.assigneeName.localeCompare(b.assigneeName)
        default:
          return b.createdAt.localeCompare(a.createdAt)
      }
    })
    return copy
  }, [tasks, sort])

  const exportCsv = () => {
    exportToCsv(
      `topshiriqlar-${today}.csv`,
      ['Topshiriq', "Mas'ul", 'Holat', 'Muhimlik', 'Muddat', 'Qadamlar', 'Bajarildi'],
      rows.map((t) => [
        t.title,
        t.assigneeName || '—',
        columnTitle.get(t.columnId) ?? '—',
        priorityOf(t.priority).label,
        t.dueDate ?? '',
        t.stepsTotal ? `${t.stepsDone}/${t.stepsTotal}` : '',
        t.isDone ? 'ha' : "yo'q",
      ]),
    )
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <TasksShell
      sub="Barcha topshiriqlar bitta jadvalda — saralash, filtr va CSV eksporti bilan"
      actions={
        <>
          <Button variant="secondary" onClick={exportCsv} disabled={rows.length === 0}>
            Excel (CSV)
          </Button>
          {canCreate && boards.length > 0 && (
            <Button onClick={() => setModal({ open: true, taskId: null })}>
              <Plus className="h-4 w-4" /> Yangi topshiriq
            </Button>
          )}
        </>
      }
    >
      <TaskFilterBar
        boards={boards}
        boardId={boardId}
        onBoard={setBoardId}
        assignees={assignees}
        filters={filters}
        onChange={setFilters}
        right={
          <>
            <Select
              value={sort}
              onChange={(e) => setSort(e.target.value as SortKey)}
              className="w-auto"
            >
              <option value="due">Muddat bo'yicha</option>
              <option value="priority">Muhimlik bo'yicha</option>
              <option value="created">Yangi qo'shilgan</option>
              <option value="assignee">Mas'ul bo'yicha</option>
            </Select>
            <label className="flex cursor-pointer items-center gap-2 whitespace-nowrap text-sm text-slate-600">
              <input
                type="checkbox"
                checked={archived}
                onChange={(e) => setArchived(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
              />
              Arxiv
            </label>
          </>
        }
      />

      <Card tight>
        {busy && rows.length === 0 ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <div className="flex flex-col items-center gap-3 py-14 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
              <Inbox className="h-6 w-6" />
            </div>
            <p className="text-sm text-slate-500">Bu kesimda topshiriq topilmadi.</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Topshiriq</th>
                  <th>Mas'ul</th>
                  <th>Holat</th>
                  <th>Muhimlik</th>
                  <th>Muddat</th>
                  <th className="num">Qadamlar</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {rows.map((t) => {
                  const p = priorityOf(t.priority)
                  return (
                    <tr
                      key={t.id}
                      onClick={() => setModal({ open: true, taskId: t.id })}
                      className="cursor-pointer"
                    >
                      <td>
                        <div className="flex items-center gap-2">
                          <span
                            className="h-6 w-1 shrink-0 rounded-full"
                            style={{ background: p.accent }}
                          />
                          <div className="min-w-0">
                            <p
                              className={cn(
                                'truncate font-semibold text-slate-800',
                                t.isDone && 'text-slate-400 line-through',
                              )}
                            >
                              {t.title}
                            </p>
                            {t.tags.length > 0 && (
                              <p className="truncate text-[11px] text-slate-400">
                                {t.tags.map((x) => `#${x}`).join(' ')}
                              </p>
                            )}
                          </div>
                        </div>
                      </td>
                      <td>
                        <AssigneeChip task={t} />
                      </td>
                      <td>
                        <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-600">
                          {columnTitle.get(t.columnId) ?? '—'}
                        </span>
                      </td>
                      <td>
                        <span
                          className={cn(
                            'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold',
                            p.chip,
                          )}
                        >
                          <span className={cn('h-1.5 w-1.5 rounded-full', p.dot)} />
                          {p.label}
                        </span>
                      </td>
                      <td>
                        {t.dueDate ? (
                          <DueChip task={t} today={today} />
                        ) : (
                          <span className="text-xs text-slate-300">—</span>
                        )}
                      </td>
                      <td className="num">
                        {t.stepsTotal > 0 ? `${t.stepsDone}/${t.stepsTotal}` : '—'}
                      </td>
                      <td>
                        <div
                          className="flex items-center justify-end gap-1"
                          onClick={(e) => e.stopPropagation()}
                        >
                          {canEdit && (
                            <button
                              type="button"
                              title={t.isArchived ? 'Arxivdan qaytarish' : 'Arxivlash'}
                              onClick={() =>
                                archiveTask(t.id, !t.isArchived)
                                  .then(load)
                                  .catch((err) => alert(apiErrorMessage(err, "Bo'lmadi")))
                              }
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-amber-50 hover:text-amber-600"
                            >
                              <Archive className="h-4 w-4" />
                            </button>
                          )}
                          {canDelete && (
                            <button
                              type="button"
                              title="O'chirish"
                              onClick={() => {
                                if (!confirm(`«${t.title}» o'chirilsinmi?`)) return
                                deleteTask(t.id)
                                  .then(load)
                                  .catch((err) => alert(apiErrorMessage(err, "Bo'lmadi")))
                              }}
                              className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600"
                            >
                              <Trash2 className="h-4 w-4" />
                            </button>
                          )}
                          <ArrowRight className="h-4 w-4 text-slate-300" />
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <TaskModal
        open={modal.open}
        onClose={() => setModal({ open: false, taskId: null })}
        board={board}
        assignees={assignees}
        taskId={modal.taskId}
        canWrite={modal.taskId ? canEdit : canCreate}
        onSaved={() => {
          load()
          reload()
        }}
      />
    </TasksShell>
  )
}
