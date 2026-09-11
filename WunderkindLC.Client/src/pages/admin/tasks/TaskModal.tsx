import { useEffect, useState } from 'react'
import { Clock, ListChecks, MessageSquare, Plus, Send, Trash2 } from 'lucide-react'
import {
  addComment, addStep, createTask, deleteStep, getTask, updateStep, updateTask,
  type WorkTask, type WorkTaskAssignee, type WorkTaskBoard, type WorkTaskDetail,
} from '@/api/services/workTasks'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'
import { cn, apiErrorMessage, formatDateTime } from '@/lib/utils'
import { PRIORITIES, REPEATS } from './model'
import { Avatar } from './shared'

interface FormState {
  title: string
  description: string
  columnId: string
  assigneeId: string
  priority: number
  dueDate: string
  dueTime: string
  repeat: string
  tags: string
}

const EMPTY: FormState = {
  title: '', description: '', columnId: '', assigneeId: '',
  priority: 1, dueDate: '', dueTime: '', repeat: 'none', tags: '',
}

/**
 * TOPSHIRIQ OYNASI — yaratish ham, tahrirlash ham, DETAL ham shu yerda.
 *
 * <p>Nega bitta oyna: ClickUp/Trello'dagi kabi topshiriq ustidagi ish bir joyda bo'lishi kerak —
 * mas'ulni almashtirish uchun alohida "tahrirlash" oynasini qidirish, izoh yozish uchun boshqasini
 * ochish kundalik ishni sekinlashtiradi. Yangi topshiriqda faqat forma ko'rinadi (qadam/izoh/tarix
 * hali yo'q), mavjudida esa formadan keyin uchala bo'lim qo'shiladi.</p>
 */
export interface TaskModalProps {
  open: boolean
  onClose: () => void
  board: WorkTaskBoard | null
  assignees: WorkTaskAssignee[]
  /** null — YANGI topshiriq. */
  taskId: string | null
  defaultColumnId?: string
  /** Yangi topshiriq uchun oldindan to'ldiriladigan muddat ("yyyy-MM-dd") — kalendarda kun bosilganda. */
  defaultDueDate?: string
  canWrite: boolean
  /** Saqlangach chaqiriladi — sahifa ro'yxatni yangilaydi. */
  onSaved: (task: WorkTask) => void
}

/**
 * Oyna YOPIQ bo'lsa umuman chizilmaydi, ochilganda esa `key` bilan YANGIDAN yaratiladi.
 *
 * ⚠️ Shu sababdan forma boshlang'ich qiymatlari `useState` initializer'ida beriladi — effekt
 * ichida setState chaqirish (React qoidasi) kerak emas va oyna qayta ochilganda eski topshiriq
 * ma'lumoti "yopishib" qolmaydi.
 */
export function TaskModal(props: TaskModalProps) {
  if (!props.open) return null
  return <TaskModalInner key={props.taskId ?? `new:${props.defaultColumnId ?? ''}:${props.defaultDueDate ?? ''}`} {...props} />
}

function TaskModalInner({
  onClose,
  board,
  assignees,
  taskId,
  defaultColumnId,
  defaultDueDate,
  canWrite,
  onSaved,
}: TaskModalProps) {
  const columns = board?.columns ?? []

  const [form, setForm] = useState<FormState>(() => ({
    ...EMPTY,
    columnId: defaultColumnId || columns[0]?.id || '',
    dueDate: defaultDueDate ?? '',
  }))
  const [detail, setDetail] = useState<WorkTaskDetail | null>(null)
  const [loading, setLoading] = useState(!!taskId)
  const [saving, setSaving] = useState(false)

  // Mavjud topshiriq — detalni serverdan olamiz (javob kelgach setState, effekt tanasida emas).
  useEffect(() => {
    if (!taskId) return
    let alive = true
    getTask(taskId)
      .then((d) => {
        if (!alive) return
        setDetail(d)
        setForm({
          title: d.task.title,
          description: d.task.description,
          columnId: d.task.columnId,
          assigneeId: d.task.assigneeId,
          priority: d.task.priority,
          dueDate: d.task.dueDate ?? '',
          dueTime: d.task.dueTime ?? '',
          repeat: d.task.repeat,
          tags: d.task.tags.join(', '),
        })
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setLoading(false)
        alert(apiErrorMessage(err, "Topshiriqni yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [taskId])

  const set = (patch: Partial<FormState>) => setForm((f) => ({ ...f, ...patch }))

  const handleSave = async () => {
    if (!board || !form.title.trim() || saving) return
    setSaving(true)
    try {
      const payload = {
        boardId: board.id,
        columnId: form.columnId || null,
        title: form.title.trim(),
        description: form.description.trim(),
        assigneeId: form.assigneeId,
        priority: form.priority,
        dueDate: form.dueDate || null,
        dueTime: form.dueTime || null,
        tags: form.tags.split(',').map((t) => t.trim()).filter(Boolean),
        repeat: form.repeat,
      }
      const saved = taskId ? await updateTask(taskId, payload) : await createTask(payload)
      onSaved(saved)
      onClose()
    } catch (err) {
      alert(apiErrorMessage(err, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const reloadDetail = () => {
    if (!taskId) return
    getTask(taskId).then(setDetail).catch(() => undefined)
  }

  return (
    <Modal
      open
      onClose={onClose}
      title={taskId ? 'Topshiriq' : 'Yangi topshiriq'}
      size="lg"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
          {canWrite && (
            <Button onClick={handleSave} disabled={saving || !form.title.trim()}>
              Saqlash
            </Button>
          )}
        </>
      }
    >
      {loading ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-5">
          <Input
            label="Topshiriq"
            required
            placeholder="Nima qilinishi kerak?"
            value={form.title}
            onChange={(e) => set({ title: e.target.value })}
            disabled={!canWrite}
            autoFocus={!taskId}
          />

          <Textarea
            label="Tavsif"
            rows={3}
            placeholder="Batafsil izoh, havolalar, talablar..."
            value={form.description}
            onChange={(e) => set({ description: e.target.value })}
            disabled={!canWrite}
          />

          <div className="grid gap-4 sm:grid-cols-3">
            <Select
              label="Holat (ustun)"
              value={form.columnId}
              onChange={(e) => set({ columnId: e.target.value })}
              disabled={!canWrite}
            >
              {columns.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.title}
                </option>
              ))}
            </Select>

            <Select
              label="Mas'ul"
              value={form.assigneeId}
              onChange={(e) => set({ assigneeId: e.target.value })}
              disabled={!canWrite}
            >
              <option value="">— biriktirilmagan —</option>
              {assignees.map((a) => (
                <option key={a.userId} value={a.userId}>
                  {a.fullName}
                  {a.hasTelegram ? '' : ' (Telegram ulanmagan)'}
                </option>
              ))}
            </Select>

            <Select
              label="Muhimlik"
              value={String(form.priority)}
              onChange={(e) => set({ priority: Number(e.target.value) })}
              disabled={!canWrite}
            >
              {PRIORITIES.map((p) => (
                <option key={p.value} value={String(p.value)}>
                  {p.label}
                </option>
              ))}
            </Select>
          </div>

          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Muddat (sana)"
              type="date"
              value={form.dueDate}
              onChange={(e) => set({ dueDate: e.target.value })}
              disabled={!canWrite}
            />
            <Input
              label="Muddat (soat)"
              type="time"
              value={form.dueTime}
              onChange={(e) => set({ dueTime: e.target.value })}
              disabled={!canWrite || !form.dueDate}
            />
            <Select
              label="Takroriylik"
              value={form.repeat}
              onChange={(e) => set({ repeat: e.target.value })}
              disabled={!canWrite}
            >
              {REPEATS.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </Select>
          </div>

          <Input
            label="Teglar (vergul bilan)"
            placeholder="hisobot, shoshilinch, filial-2"
            value={form.tags}
            onChange={(e) => set({ tags: e.target.value })}
            disabled={!canWrite}
          />

          {taskId && detail && (
            <>
              <StepsSection detail={detail} canWrite={canWrite} onChanged={reloadDetail} />
              <CommentsSection detail={detail} canWrite={canWrite} onChanged={reloadDetail} />
              <HistorySection detail={detail} />
            </>
          )}
        </div>
      )}
    </Modal>
  )
}

/* ---------------------------------------------------------------------------------------
 *  QADAMLAR (checklist)
 * ------------------------------------------------------------------------------------- */

function StepsSection({
  detail,
  canWrite,
  onChanged,
}: {
  detail: WorkTaskDetail
  canWrite: boolean
  onChanged: () => void
}) {
  const [title, setTitle] = useState('')
  const [busy, setBusy] = useState(false)

  const done = detail.steps.filter((s) => s.done).length

  const add = async () => {
    if (!title.trim() || busy) return
    setBusy(true)
    try {
      await addStep(detail.task.id, title.trim())
      setTitle('')
      onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, "Qadam qo'shib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const toggle = async (id: string, value: boolean) => {
    try {
      await updateStep(id, { done: value })
      onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, "Belgilab bo'lmadi"))
    }
  }

  return (
    <section className="rounded-xl border border-slate-100 p-3">
      <h4 className="mb-2 flex items-center gap-2 text-sm font-bold text-slate-700">
        <ListChecks className="h-4 w-4 text-slate-400" />
        Qadamlar
        {detail.steps.length > 0 && (
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-500">
            {done}/{detail.steps.length}
          </span>
        )}
      </h4>

      {detail.steps.length > 0 && (
        <ul className="mb-3 space-y-1">
          {detail.steps.map((s) => (
            <li key={s.id} className="flex items-center gap-2 rounded-lg px-1 py-1 hover:bg-slate-50">
              <input
                type="checkbox"
                checked={s.done}
                disabled={!canWrite}
                onChange={(e) => toggle(s.id, e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
              />
              <span className={cn('flex-1 text-sm', s.done ? 'text-slate-400 line-through' : 'text-slate-700')}>
                {s.title}
              </span>
              {canWrite && (
                <button
                  type="button"
                  title="O'chirish"
                  onClick={() => deleteStep(s.id).then(onChanged)}
                  className="rounded p-1 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600"
                >
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {canWrite && (
        <div className="flex gap-2">
          <Input
            placeholder="Yangi qadam"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                void add()
              }
            }}
          />
          <Button variant="secondary" onClick={add} disabled={!title.trim() || busy} className="shrink-0">
            <Plus className="h-4 w-4" />
          </Button>
        </div>
      )}
    </section>
  )
}

/* ---------------------------------------------------------------------------------------
 *  IZOHLAR
 * ------------------------------------------------------------------------------------- */

function CommentsSection({
  detail,
  canWrite,
  onChanged,
}: {
  detail: WorkTaskDetail
  canWrite: boolean
  onChanged: () => void
}) {
  const [text, setText] = useState('')
  const [busy, setBusy] = useState(false)

  const send = async () => {
    if (!text.trim() || busy) return
    setBusy(true)
    try {
      await addComment(detail.task.id, text.trim())
      setText('')
      onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, "Izohni yuborib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="rounded-xl border border-slate-100 p-3">
      <h4 className="mb-2 flex items-center gap-2 text-sm font-bold text-slate-700">
        <MessageSquare className="h-4 w-4 text-slate-400" />
        Izohlar
        {detail.comments.length > 0 && (
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-semibold text-slate-500">
            {detail.comments.length}
          </span>
        )}
      </h4>

      {detail.comments.length === 0 ? (
        <p className="mb-3 text-xs text-slate-400">Hali izoh yo'q.</p>
      ) : (
        <ul className="mb-3 space-y-2">
          {detail.comments.map((c) => (
            <li key={c.id} className="flex gap-2">
              <Avatar name={c.authorName} size={26} />
              <div className="min-w-0 flex-1 rounded-lg bg-slate-50 px-3 py-2">
                <div className="flex items-baseline justify-between gap-2">
                  <span className="truncate text-[12.5px] font-semibold text-slate-700">
                    {c.authorName}
                  </span>
                  <span className="shrink-0 text-[11px] text-slate-400">
                    {formatDateTime(c.createdAt)}
                  </span>
                </div>
                <p className="whitespace-pre-wrap text-[13px] leading-snug text-slate-600">{c.text}</p>
              </div>
            </li>
          ))}
        </ul>
      )}

      {canWrite && (
        <div className="flex gap-2">
          <Input
            placeholder="Izoh yozing"
            value={text}
            onChange={(e) => setText(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault()
                void send()
              }
            }}
          />
          <Button onClick={send} disabled={!text.trim() || busy} className="shrink-0">
            <Send className="h-4 w-4" />
          </Button>
        </div>
      )}
    </section>
  )
}

/* ---------------------------------------------------------------------------------------
 *  HARAKATLAR TARIXI — nazoratning asosiy dalili
 * ------------------------------------------------------------------------------------- */

function HistorySection({ detail }: { detail: WorkTaskDetail }) {
  if (detail.events.length === 0) return null
  return (
    <section className="rounded-xl border border-slate-100 p-3">
      <h4 className="mb-2 flex items-center gap-2 text-sm font-bold text-slate-700">
        <Clock className="h-4 w-4 text-slate-400" />
        Harakatlar tarixi
      </h4>
      <ul className="space-y-1.5">
        {detail.events.map((e) => (
          <li key={e.id} className="flex items-start gap-2 text-[12.5px]">
            <span className="mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full bg-slate-300" />
            <span className="flex-1 text-slate-600">
              {e.text}
              <span className="text-slate-400"> — {e.actorName}</span>
            </span>
            <span className="shrink-0 text-[11px] text-slate-400">{formatDateTime(e.createdAt)}</span>
          </li>
        ))}
      </ul>
    </section>
  )
}
