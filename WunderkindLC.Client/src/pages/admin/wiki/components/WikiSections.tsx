import { useRef, useState, type ReactNode } from 'react'
import { ChevronDown, ChevronRight, FolderPlus, Lock, Plus } from 'lucide-react'
import type { WikiTreeNode } from '@/api/services/wiki'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * Bo'lim nomini yozish maydoni (yangi bo'lim ham, qayta nomlash ham). Enter — saqlash, Esc — bekor;
 * maydondan chiqilsa: bo'sh bo'lsa bekor, aks holda saqlanadi. Xato bo'lsa maydon yopilmaydi va matn qoladi.
 */
function SectionNameInput({ initial = '', onSubmit, onCancel }: {
  initial?: string
  onSubmit: (name: string) => Promise<void>
  onCancel: () => void
}) {
  const [value, setValue] = useState(initial)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  // Enter → maydon `disabled` bo'ladi → brauzer `blur` beradi → ikkinchi submit. State hali eski bo'lgani uchun
  // qulf ref'da (aks holda bitta bo'lim IKKI marta yaratilardi).
  const sending = useRef(false)

  const submit = async () => {
    if (sending.current) return
    const name = value.trim()
    if (!name || name === initial.trim()) return onCancel()
    sending.current = true
    setBusy(true)
    setError('')
    try {
      await onSubmit(name)
    } catch (err) {
      setError(apiErrorMessage(err, "Bo'limni saqlab bo'lmadi"))
      setBusy(false)
      sending.current = false
    }
  }

  return (
    <div className="px-1 py-1">
      <input
        autoFocus
        value={value}
        disabled={busy}
        maxLength={200}
        placeholder="Bo'lim nomi, masalan: Ish rejalari"
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') void submit()
          if (e.key === 'Escape') onCancel()
        }}
        onBlur={() => {
          if (!error) void submit()
        }}
        className="h-8 w-full rounded-md border border-brand-300 bg-white px-2 text-[13px] text-slate-800 outline-none ring-2 ring-brand-100 placeholder:text-slate-400"
      />
      {error && <div className="mt-1 text-[12px] text-[#d32f2f]">{error}</div>}
    </div>
  )
}

/**
 * Chap paneldagi «+ Yangi bo'lim» tugmasi — bosilsa shu joyning o'zida nom maydoniga aylanadi.
 * `compact` — xodimlar ro'yxatidagi kichik varianti.
 */
export function NewSectionButton({ onCreate, compact }: { onCreate: (name: string) => Promise<void>; compact?: boolean }) {
  const [open, setOpen] = useState(false)
  if (open)
    return (
      <SectionNameInput
        onSubmit={async (name) => {
          await onCreate(name)
          setOpen(false)
        }}
        onCancel={() => setOpen(false)}
      />
    )
  return (
    <button
      type="button"
      onClick={() => setOpen(true)}
      className={cn(
        'flex w-full items-center gap-1.5 rounded-md border border-dashed border-slate-300 text-slate-500 transition-colors hover:border-brand-400 hover:bg-brand-50/50 hover:text-brand-600',
        compact ? 'mt-0.5 h-7 px-2 text-[12px]' : 'mt-1 h-9 justify-center text-[13px] font-medium',
      )}
    >
      <FolderPlus size={compact ? 13 : 15} />
      Yangi bo'lim
    </button>
  )
}

interface SectionBlockProps {
  node: WikiTreeNode
  open: boolean
  onToggle: () => void
  /** Ichiga yangi sahifa (bo'lmasa — «+» chizilmaydi, masalan qulflangan yoki faqat o'qish) */
  onAdd?: () => void
  menu: DropdownMenuItem[]
  /** Qayta nomlash rejimi — sarlavha o'rnida nom maydoni */
  renaming?: boolean
  onRename?: (name: string) => Promise<void>
  onRenameCancel?: () => void
  /** Bo'lim ichidagi sahifalar (yoki bo'shligi haqida yozuv) */
  children: ReactNode
}

/**
 * BO'LIM — «Mening eslatmalarim» bilan bir xil uslubdagi sarlavha (papka), ostida o'z sahifalari.
 * Yig'ish/ochish, «+» (ichiga sahifa) va ⋯ menyusi (nomi, tartib, qulf, o'chirish — chaqiruvchi beradi).
 */
export function SectionBlock({ node, open, onToggle, onAdd, menu, renaming, onRename, onRenameCancel, children }: SectionBlockProps) {
  return (
    <div className="mb-2">
      {renaming && onRename && onRenameCancel ? (
        <SectionNameInput initial={node.title} onSubmit={onRename} onCancel={onRenameCancel} />
      ) : (
        <div className="group flex h-8 items-center gap-1 rounded-md px-1 text-[12.5px] font-semibold uppercase tracking-wide text-slate-500 hover:bg-slate-50">
          <button type="button" onClick={onToggle} className="flex min-w-0 flex-1 items-center gap-1 text-left" aria-expanded={open}>
            {open ? <ChevronDown size={13} className="shrink-0 text-slate-400" /> : <ChevronRight size={13} className="shrink-0 text-slate-400" />}
            <span className="shrink-0">{node.icon || '📁'}</span>
            <span className="truncate">{node.title}</span>
            {node.locked && <Lock size={12} className="shrink-0 text-amber-600" aria-label="Qulflangan" />}
          </button>
          {menu.length > 0 && (
            <div className="opacity-0 focus-within:opacity-100 group-hover:opacity-100">
              <DropdownMenu items={menu} triggerClassName="h-6 w-6" />
            </div>
          )}
          {onAdd && (
            <button
              type="button"
              title="Bo'limga yangi eslatma"
              onClick={onAdd}
              className="flex h-6 w-6 shrink-0 items-center justify-center rounded text-slate-400 hover:bg-slate-200 hover:text-brand-600"
            >
              <Plus size={15} />
            </button>
          )}
        </div>
      )}
      {open && children}
    </div>
  )
}
