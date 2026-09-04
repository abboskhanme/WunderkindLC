import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors,
  type DragEndEvent, type DragStartEvent,
} from '@dnd-kit/core'
import { ListChecks, Plus, Settings } from 'lucide-react'
import {
  archiveTask, deleteTask, getTasks, moveTask,
  type WorkTask, type WorkTaskColumn,
} from '@/api/services/workTasks'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage } from '@/lib/utils'
import { BoardSettingsModal } from './BoardSettingsModal'
import { TaskCardContent, type TaskCardActions } from './TaskCard'
import { TaskColumn } from './TaskColumn'
import { TaskModal } from './TaskModal'
import {
  EMPTY_FILTERS, toQuery, todayIso, useBoardParam, useTasksMeta, type TaskFilters,
} from './model'
import { TaskFilterBar, TasksShell } from './shared'

/**
 * TOPSHIRIQLAR DOSKASI (Kanban) — bo'limning asosiy ekrani.
 *
 * <p>Topshiriq ustundan ustunga SUDRAB ko'chiriladi va bu darhol serverga yoziladi. Lidlar
 * taxtasidan farqli o'laroq bu yerda tasdiqlash oynasi YO'Q: holat o'zgarishi hech qanday
 * qo'shimcha ma'lumot talab qilmaydi (kim va qachon ko'chirgani topshiriq tarixiga o'zi
 * yoziladi), ya'ni ortiqcha bosish faqat ishni sekinlashtirardi.</p>
 */
export function TasksBoardPage() {
  const { can } = usePerm()
  const canCreate = can('tasks.board', 'create')
  const canEdit = can('tasks.board', 'edit')
  const canDelete = can('tasks.board', 'delete')
  const canSettings = can('tasks.settings', 'edit')

  const { boards, assignees, loading, error, reload } = useTasksMeta()
  const [boardId, setBoardId] = useBoardParam(boards)
  const [filters, setFilters] = useState<TaskFilters>(EMPTY_FILTERS)

  const [tasks, setTasks] = useState<WorkTask[]>([])
  const [tasksLoading, setTasksLoading] = useState(false)
  const [activeId, setActiveId] = useState<string | null>(null)

  const [modal, setModal] = useState<{ open: boolean; taskId: string | null; columnId?: string }>({
    open: false,
    taskId: null,
  })
  const [settingsOpen, setSettingsOpen] = useState(false)

  const today = todayIso()
  const board = boards.find((b) => b.id === boardId) ?? null

  const loadTasks = useCallback(() => {
    if (!boardId) {
      setTasks([])
      return
    }
    setTasksLoading(true)
    getTasks(toQuery(boardId, filters))
      .then(setTasks)
      .catch((err) => alert(apiErrorMessage(err, "Topshiriqlarni yuklab bo'lmadi")))
      .finally(() => setTasksLoading(false))
  }, [boardId, filters])

  // Qidiruv matni har harfda so'rov yubormasin — kichik kechikish bilan.
  useEffect(() => {
    const id = setTimeout(loadTasks, filters.q ? 300 : 0)
    return () => clearTimeout(id)
  }, [loadTasks, filters.q])

  const grouped = useMemo(() => {
    const map = new Map<string, WorkTask[]>((board?.columns ?? []).map((c) => [c.id, []]))
    for (const t of tasks) map.get(t.columnId)?.push(t)
    return map
  }, [board, tasks])

  const activeTask = activeId ? (tasks.find((t) => t.id === activeId) ?? null) : null

  const actions: TaskCardActions = {
    onOpen: (t) => setModal({ open: true, taskId: t.id }),
    onArchive: async (t) => {
      try {
        await archiveTask(t.id, !t.isArchived)
        loadTasks()
        reload()
      } catch (err) {
        alert(apiErrorMessage(err, "Arxivlab bo'lmadi"))
      }
    },
    onDelete: async (t) => {
      if (!confirm(`«${t.title}» butunlay o'chirilsinmi? Izohlari va tarixi ham o'chadi.`)) return
      try {
        await deleteTask(t.id)
        loadTasks()
        reload()
      } catch (err) {
        alert(apiErrorMessage(err, "O'chirib bo'lmadi"))
      }
    },
    canWrite: canEdit,
    canDelete,
  }

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const handleDragEnd = async (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const task = tasks.find((t) => t.id === String(active.id))
    const columnId = String(over.id)
    if (!task || task.columnId === columnId) return

    // OPTIMISTIK: karta darhol yangi ustunga o'tadi, so'ng server javobi bilan aniqlanadi
    // (xatoda ro'yxat qayta yuklanib eski holatga qaytadi).
    setTasks((prev) => prev.map((t) => (t.id === task.id ? { ...t, columnId } : t)))
    try {
      await moveTask(task.id, columnId, 0)
      loadTasks()
      reload()
    } catch (err) {
      alert(apiErrorMessage(err, "Ko'chirib bo'lmadi"))
      loadTasks()
    }
  }

  const openNew = (column?: WorkTaskColumn) =>
    setModal({ open: true, taskId: null, columnId: column?.id })

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <TasksShell
      sub="Xodimlarga topshiriq bering, bosqichma-bosqich kuzating va muddatni nazorat qiling"
      actions={
        <>
          {canSettings && boards.length > 0 && (
            <Button variant="secondary" onClick={() => setSettingsOpen(true)}>
              <Settings className="h-4 w-4" /> Sozlamalar
            </Button>
          )}
          {canCreate && boards.length > 0 && (
            <Button onClick={() => openNew()}>
              <Plus className="h-4 w-4" /> Yangi topshiriq
            </Button>
          )}
        </>
      }
    >
      {error && (
        <Card className="mb-4 border-red-200 bg-red-50">
          <p className="text-sm text-red-600">{error}</p>
        </Card>
      )}

      {boards.length === 0 ? (
        <EmptyBoards canSettings={canSettings} onOpen={() => setSettingsOpen(true)} />
      ) : (
        <>
          <TaskFilterBar
            boards={boards}
            boardId={boardId}
            onBoard={setBoardId}
            assignees={assignees}
            filters={filters}
            onChange={setFilters}
          />

          {tasksLoading && tasks.length === 0 ? (
            <Loader label="Topshiriqlar yuklanmoqda..." />
          ) : (
            <DndContext
              sensors={sensors}
              onDragStart={(e: DragStartEvent) => setActiveId(String(e.active.id))}
              onDragEnd={handleDragEnd}
              onDragCancel={() => setActiveId(null)}
            >
              <div className="kanban">
                {(board?.columns ?? []).map((c) => (
                  <TaskColumn
                    key={c.id}
                    column={c}
                    tasks={grouped.get(c.id) ?? []}
                    today={today}
                    actions={actions}
                    accepts={canEdit && (!activeTask || activeTask.columnId !== c.id)}
                    dragging={!!activeId}
                    onAdd={canCreate ? openNew : undefined}
                    onCardClick={(t) => setModal({ open: true, taskId: t.id })}
                  />
                ))}
              </div>

              <DragOverlay dropAnimation={null}>
                {activeTask && <TaskCardContent task={activeTask} today={today} dragging />}
              </DragOverlay>
            </DndContext>
          )}
        </>
      )}

      <TaskModal
        open={modal.open}
        onClose={() => setModal({ open: false, taskId: null })}
        board={board}
        assignees={assignees}
        taskId={modal.taskId}
        defaultColumnId={modal.columnId}
        canWrite={modal.taskId ? canEdit : canCreate}
        onSaved={() => {
          loadTasks()
          reload()
        }}
      />

      <BoardSettingsModal
        open={settingsOpen}
        onClose={() => setSettingsOpen(false)}
        boards={boards}
        boardId={boardId}
        onChanged={() => {
          reload()
          loadTasks()
        }}
      />
    </TasksShell>
  )
}

/** Doska umuman yo'q holat — server birinchi ochilishda "Umumiy" doskasini o'zi yaratadi,
 *  ya'ni bu ekran faqat hamma doska arxivlangan bo'lsa ko'rinadi. */
function EmptyBoards({ canSettings, onOpen }: { canSettings: boolean; onOpen: () => void }) {
  return (
    <Card className="py-12 text-center">
      <div className="mx-auto mb-3 flex h-12 w-12 items-center justify-center rounded-2xl bg-brand-50 text-brand-600">
        <ListChecks className="h-6 w-6" />
      </div>
      <p className="text-sm text-slate-500">Faol doska yo'q — barchasi arxivlangan.</p>
      {canSettings && (
        <Button className="mt-4" onClick={onOpen}>
          <Settings className="h-4 w-4" /> Doska yaratish
        </Button>
      )}
    </Card>
  )
}
