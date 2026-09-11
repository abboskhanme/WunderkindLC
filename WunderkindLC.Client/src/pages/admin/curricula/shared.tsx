import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import {
  Video, FileText, Music, BookOpen, ClipboardCheck, FileType, Blocks, Trash2, AlertTriangle, Loader2, Plus,
} from 'lucide-react'
import type { LessonType } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'

export const control =
  'w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400'

/** Topshiriq turlari — har topshiriq yaratishda (yoki keyin tahrirlashda) shulardan birini tanlaydi. */
export const LESSON_TYPES: { type: LessonType; label: string; icon: typeof Video }[] = [
  { type: 'video', label: 'Video', icon: Video },
  { type: 'text', label: 'Matn', icon: FileText },
  { type: 'audio', label: 'Audio', icon: Music },
  { type: 'pdf', label: 'PDF', icon: FileType },
  { type: 'vocab', label: "Lug'at", icon: BookOpen },
  { type: 'test', label: 'Test', icon: ClipboardCheck },
  // Interaktiv mashq — topshiriq konstruktori (gap tuzish, bo'sh joy, reading, matching, writing...).
  { type: 'exercise', label: 'Mashq', icon: Blocks },
]

export function typeMeta(type: LessonType) {
  return LESSON_TYPES.find((t) => t.type === type) ?? LESSON_TYPES[1]
}

export function genId() {
  return `q_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`
}

// ============================ Nom kiritish modali (Dastur/Bo'lim/Mavzu/Sub-mavzu) ============================

interface NameModalProps {
  open: boolean
  title: string
  label: string
  placeholder: string
  hint?: string
  initialValue?: string
  submitLabel?: string
  busy: boolean
  onClose: () => void
  onSubmit: (name: string) => void
}

export function NameModal({
  open, title, label, placeholder, hint, initialValue = '', submitLabel = "Qo'shish", busy, onClose, onSubmit,
}: NameModalProps) {
  const [value, setValue] = useState(initialValue)
  useEffect(() => {
    if (open) setValue(initialValue)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const v = value.trim()
    if (!v || busy) return
    onSubmit(v)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={title}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button type="submit" form="curriculum-name-form" disabled={busy || !value.trim()}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
            {submitLabel}
          </Button>
        </>
      }
    >
      <form id="curriculum-name-form" onSubmit={submit}>
        <label className="mb-1.5 block text-xs font-semibold text-slate-500">{label}</label>
        <input
          autoFocus
          value={value}
          onChange={(e) => setValue(e.target.value)}
          placeholder={placeholder}
          className={control}
        />
        {hint && <p className="mt-2 text-xs leading-relaxed text-slate-400">{hint}</p>}
      </form>
    </Modal>
  )
}

// ============================ O'chirishni tasdiqlash modali ============================

interface ConfirmDeleteModalProps {
  open: boolean
  title: string
  message: ReactNode
  busy: boolean
  onClose: () => void
  onConfirm: () => void
}

export function ConfirmDeleteModal({ open, title, message, busy, onClose, onConfirm }: ConfirmDeleteModalProps) {
  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={title}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={onConfirm} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
            O'chirish
          </Button>
        </>
      }
    >
      <div className="flex items-start gap-3">
        <div className="flex h-10 w-10 flex-shrink-0 items-center justify-center rounded-full bg-red-50 text-red-600">
          <AlertTriangle className="h-5 w-5" />
        </div>
        <p className="text-sm leading-relaxed text-slate-600">{message}</p>
      </div>
    </Modal>
  )
}
