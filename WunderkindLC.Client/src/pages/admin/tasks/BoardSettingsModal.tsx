import { useEffect, useState } from 'react'
import { Archive, ChevronLeft, ChevronRight, Lock, Pencil, Plus, Trash2 } from 'lucide-react'
import {
  createBoard, createColumn, deleteBoard, deleteColumn, getWorkTaskSettings, moveColumn,
  setWorkTaskSettings, updateBoard, updateColumn,
  type WorkTaskBoard, type WorkTaskColumn,
} from '@/api/services/workTasks'
import type { StageColor } from '@/types'
import { Button } from '@/components/ui/Button'
import { Input, Time24Input } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { stageColorKeys, stageColors } from '@/config/stageColors'
import { cn, apiErrorMessage } from '@/lib/utils'

/**
 * DOSKA VA USTUN SOZLAMALARI — `tasks.settings` ruxsati bilan.
 *
 * <p>Bir oynada uch narsa: doskalar (loyihalar), tanlangan doskaning ustunlari (Kanban
 * bosqichlari) va kunlik Telegram eslatmasi vaqti. Uchalasi ham "sozlash" ishi bo'lgani uchun
 * alohida sahifa ochish shart emas — doska ustidan chiqmasdan tuzatiladi.</p>
 */
export function BoardSettingsModal({
  open,
  onClose,
  boards,
  boardId,
  onChanged,
}: {
  open: boolean
  onClose: () => void
  boards: WorkTaskBoard[]
  boardId: string
  onChanged: () => void | Promise<void>
}) {
  const board = boards.find((b) => b.id === boardId) ?? boards[0] ?? null

  const [newBoard, setNewBoard] = useState('')
  const [newColumn, setNewColumn] = useState('')
  const [busy, setBusy] = useState(false)

  // Kunlik eslatma
  const [enabled, setEnabled] = useState(true)
  const [time, setTime] = useState('09:30')
  const [savedHint, setSavedHint] = useState(false)

  useEffect(() => {
    if (!open) return
    getWorkTaskSettings()
      .then((s) => {
        setEnabled(s.enabled)
        setTime(`${String(s.hour).padStart(2, '0')}:${String(s.minute).padStart(2, '0')}`)
      })
      .catch(() => undefined)
  }, [open])

  const run = async (fn: () => Promise<unknown>, fallback: string) => {
    if (busy) return
    setBusy(true)
    try {
      await fn()
      await onChanged()
    } catch (err) {
      alert(apiErrorMessage(err, fallback))
    } finally {
      setBusy(false)
    }
  }

  const saveSettings = async () => {
    const [h, m] = time.split(':')
    try {
      await setWorkTaskSettings({ enabled, hour: Number(h) || 0, minute: Number(m) || 0 })
      setSavedHint(true)
      setTimeout(() => setSavedHint(false), 1600)
    } catch (err) {
      alert(apiErrorMessage(err, "Sozlamani saqlab bo'lmadi"))
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Doska sozlamalari"
      size="lg"
      footer={
        <Button variant="secondary" onClick={onClose}>
          Yopish
        </Button>
      }
    >
      <div className="space-y-6">
        {/* ---------- Doskalar ---------- */}
        <section>
          <h4 className="mb-2 text-sm font-bold text-slate-700">Doskalar</h4>
          <div className="mb-3 divide-y divide-slate-100 rounded-xl border border-slate-100">
            {boards.map((b) => (
              <BoardRow
                key={b.id}
                board={b}
                active={b.id === board?.id}
                onSave={(title, color) =>
                  run(() => updateBoard(b.id, title, color), "Saqlab bo'lmadi")
                }
                onArchive={() => {
                  if (!confirm(`«${b.title}» doskasi arxivlansinmi? Topshiriqlari saqlanadi.`)) return
                  void run(() => deleteBoard(b.id), "Arxivlab bo'lmadi")
                }}
              />
            ))}
            {boards.length === 0 && (
              <p className="px-3 py-6 text-center text-sm text-slate-400">Doska yo'q.</p>
            )}
          </div>

          <div className="flex gap-2">
            <Input
              placeholder="Yangi doska nomi"
              value={newBoard}
              onChange={(e) => setNewBoard(e.target.value)}
            />
            <Button
              className="shrink-0"
              disabled={!newBoard.trim() || busy}
              onClick={() =>
                run(async () => {
                  await createBoard(newBoard.trim(), 'violet')
                  setNewBoard('')
                }, "Doska yaratib bo'lmadi")
              }
            >
              <Plus className="h-4 w-4" /> Doska
            </Button>
          </div>
        </section>

        {/* ---------- Ustunlar ---------- */}
        {board && (
          <section>
            <h4 className="mb-2 text-sm font-bold text-slate-700">
              «{board.title}» ustunlari
            </h4>
            <p className="mb-2 text-xs text-slate-400">
              «Bajarildi» belgisi qo'yilgan ustunga tushgan topshiriq YOPILGAN hisoblanadi —
              statistika, muddat eslatmasi va botdagi «Bajardim» tugmasi shunga qaraydi.
            </p>

            <div className="mb-3 divide-y divide-slate-100 rounded-xl border border-slate-100">
              {board.columns.map((c, i) => (
                <ColumnRow
                  key={c.id}
                  column={c}
                  isFirst={i === 0}
                  isLast={i === board.columns.length - 1}
                  onSave={(title, color, isDone) =>
                    run(() => updateColumn(c.id, board.id, title, color, isDone), "Saqlab bo'lmadi")
                  }
                  onMove={(dir) => run(() => moveColumn(c.id, dir), "Ko'chirib bo'lmadi")}
                  onDelete={() => {
                    if (!confirm(`«${c.title}» ustuni o'chirilsinmi? Topshiriqlari birinchi ustunga ko'chadi.`))
                      return
                    void run(() => deleteColumn(c.id), "O'chirib bo'lmadi")
                  }}
                />
              ))}
            </div>

            <div className="flex gap-2">
              <Input
                placeholder="Yangi ustun nomi"
                value={newColumn}
                onChange={(e) => setNewColumn(e.target.value)}
              />
              <Button
                variant="secondary"
                className="shrink-0"
                disabled={!newColumn.trim() || busy}
                onClick={() =>
                  run(async () => {
                    await createColumn(board.id, newColumn.trim(), 'slate', false)
                    setNewColumn('')
                  }, "Ustun qo'shib bo'lmadi")
                }
              >
                <Plus className="h-4 w-4" /> Ustun
              </Button>
            </div>
          </section>
        )}

        {/* ---------- Kunlik eslatma ---------- */}
        <section className="rounded-xl border border-slate-100 p-3">
          <h4 className="mb-2 text-sm font-bold text-slate-700">Kunlik Telegram eslatmasi</h4>
          <p className="mb-3 text-xs text-slate-400">
            Belgilangan vaqtda har bir mas'ulga BUGUN muddati keladigan va MUDDATI O'TGAN
            topshiriqlari «Bajardim» tugmasi bilan yuboriladi.
          </p>
          <div className="flex flex-wrap items-center gap-4">
            <label className="flex cursor-pointer items-center gap-2">
              <input
                type="checkbox"
                checked={enabled}
                onChange={(e) => setEnabled(e.target.checked)}
                className="h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-200"
              />
              <span className="text-sm font-medium text-slate-700">Yoqilgan</span>
            </label>
            <div className="flex items-center gap-2">
              <span className="text-sm text-slate-500">Vaqt:</span>
              <Time24Input value={time} onChange={setTime} disabled={!enabled} />
            </div>
            <Button variant="secondary" onClick={saveSettings}>
              {savedHint ? 'Saqlandi' : 'Saqlash'}
            </Button>
          </div>
        </section>
      </div>
    </Modal>
  )
}

/* ---------------------------------------------------------------------------------------
 *  Qatorlar
 * ------------------------------------------------------------------------------------- */

function ColorPicker({
  value,
  onChange,
}: {
  value: StageColor
  onChange: (c: StageColor) => void
}) {
  return (
    <div className="flex items-center gap-1">
      {stageColorKeys.map((k) => (
        <button
          key={k}
          type="button"
          title={k}
          onClick={() => onChange(k)}
          className={cn(
            'h-4 w-4 rounded-full transition-transform',
            stageColors[k].swatch,
            value === k ? 'scale-125 ring-2 ring-slate-300 ring-offset-1' : 'opacity-60 hover:opacity-100',
          )}
        />
      ))}
    </div>
  )
}

function BoardRow({
  board,
  active,
  onSave,
  onArchive,
}: {
  board: WorkTaskBoard
  active: boolean
  onSave: (title: string, color: StageColor) => void
  onArchive: () => void
}) {
  const [edit, setEdit] = useState(false)
  const [title, setTitle] = useState(board.title)
  const [color, setColor] = useState<StageColor>(board.color)

  return (
    <div className={cn('flex items-center gap-2 px-3 py-2.5', active && 'bg-brand-50/50')}>
      <span className={cn('h-2.5 w-2.5 shrink-0 rounded-full', stageColors[board.color]?.swatch)} />
      {edit ? (
        <>
          <Input value={title} onChange={(e) => setTitle(e.target.value)} className="py-1" />
          <ColorPicker value={color} onChange={setColor} />
          <Button
            className="shrink-0 py-1"
            onClick={() => {
              onSave(title.trim() || board.title, color)
              setEdit(false)
            }}
          >
            OK
          </Button>
        </>
      ) : (
        <>
          <span className="min-w-0 flex-1 truncate text-sm font-semibold text-slate-700">
            {board.title}
          </span>
          <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-500">
            {board.taskCount}
          </span>
          <button
            type="button"
            title="Tahrirlash"
            onClick={() => setEdit(true)}
            className="rounded-md p-1.5 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-700"
          >
            <Pencil className="h-4 w-4" />
          </button>
          <button
            type="button"
            title="Arxivlash"
            onClick={onArchive}
            className="rounded-md p-1.5 text-slate-400 transition-colors hover:bg-amber-50 hover:text-amber-600"
          >
            <Archive className="h-4 w-4" />
          </button>
        </>
      )}
    </div>
  )
}

function ColumnRow({
  column,
  isFirst,
  isLast,
  onSave,
  onMove,
  onDelete,
}: {
  column: WorkTaskColumn
  isFirst: boolean
  isLast: boolean
  onSave: (title: string, color: StageColor, isDone: boolean) => void
  onMove: (dir: -1 | 1) => void
  onDelete: () => void
}) {
  const [title, setTitle] = useState(column.title)
  const [color, setColor] = useState<StageColor>(column.color)
  const [isDone, setIsDone] = useState(column.isDone)
  const dirty = title !== column.title || color !== column.color || isDone !== column.isDone

  return (
    <div className="flex flex-wrap items-center gap-2 px-3 py-2.5">
      <Input value={title} onChange={(e) => setTitle(e.target.value)} className="w-auto flex-1 py-1" />
      <ColorPicker value={color} onChange={setColor} />

      <label className="flex shrink-0 cursor-pointer items-center gap-1.5" title="Yopiluvchi ustun">
        <input
          type="checkbox"
          checked={isDone}
          onChange={(e) => setIsDone(e.target.checked)}
          className="h-4 w-4 rounded border-slate-300 text-emerald-600 focus:ring-emerald-200"
        />
        <span className="text-xs font-medium text-slate-600">Bajarildi</span>
      </label>

      <div className="flex shrink-0 items-center">
        <IconBtn icon={ChevronLeft} title="Chapga" disabled={isFirst} onClick={() => onMove(-1)} />
        <IconBtn icon={ChevronRight} title="O'ngga" disabled={isLast} onClick={() => onMove(1)} />
        {column.isSystem ? (
          <span title="Tizim ustuni — o'chirilmaydi" className="p-1.5 text-slate-300">
            <Lock className="h-4 w-4" />
          </span>
        ) : (
          <IconBtn icon={Trash2} title="O'chirish" danger onClick={onDelete} />
        )}
      </div>

      {dirty && (
        <Button className="shrink-0 py-1" onClick={() => onSave(title.trim() || column.title, color, isDone)}>
          Saqlash
        </Button>
      )}
    </div>
  )
}

function IconBtn({
  icon: Icon,
  title,
  onClick,
  disabled,
  danger,
}: {
  icon: typeof ChevronLeft
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
        'rounded-md p-1.5 transition-colors disabled:opacity-30 disabled:hover:bg-transparent',
        danger
          ? 'text-slate-400 hover:bg-red-50 hover:text-red-600'
          : 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
      )}
    >
      <Icon className="h-4 w-4" />
    </button>
  )
}
