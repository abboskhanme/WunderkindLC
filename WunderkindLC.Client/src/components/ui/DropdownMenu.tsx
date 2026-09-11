import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { MoreVertical, type LucideIcon } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface DropdownMenuItem {
  label: string
  icon: LucideIcon
  onClick: () => void
  danger?: boolean
}

interface DropdownMenuProps {
  items: DropdownMenuItem[]
  align?: 'left' | 'right'
  /** Trigger tugmasi uchun qo'shimcha class (masalan hajm/rang moslashtirish). */
  triggerClassName?: string
}

const PANEL_WIDTH = 224 // w-56

/** "⋮" tugma — bosilganda amallar ro'yxatini ochadi (Topbar profil menyusi bilan bir xil konventsiya).
 *  Panel `document.body`ga PORTAL qilinadi (position: fixed) — shu bilan `overflow-y-auto` bo'lgan
 *  ota elementlar (masalan a'zolar ro'yxati) ichida QIRQILMAYDI/o'ralib qolmaydi. */
export function DropdownMenu({ items, align = 'right', triggerClassName }: DropdownMenuProps) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  const updatePosition = () => {
    const el = triggerRef.current
    if (!el) return
    const r = el.getBoundingClientRect()
    setPos({
      top: r.bottom + 4,
      left: align === 'right' ? r.right - PANEL_WIDTH : r.left,
    })
  }

  useLayoutEffect(() => {
    if (open) updatePosition()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  useEffect(() => {
    if (!open) return
    const onClick = (e: MouseEvent) => {
      if (
        triggerRef.current?.contains(e.target as Node) ||
        panelRef.current?.contains(e.target as Node)
      )
        return
      setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    // Trigger biror scrollable ota (masalan ro'yxat) ichida scroll bo'lsa — panel eskirib qolmasin,
    // shunchaki yopamiz (standart dropdown konventsiyasi). `capture: true` — ichki scroll ham ushlanadi.
    const onScroll = () => setOpen(false)
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onScroll)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onScroll)
    }
  }, [open])

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((o) => !o)}
        aria-haspopup="menu"
        aria-expanded={open}
        className={cn(
          'flex h-9 w-9 items-center justify-center rounded-xl border border-slate-200 text-slate-500 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700',
          triggerClassName,
        )}
      >
        <MoreVertical className="h-4 w-4" />
      </button>

      {open &&
        pos &&
        createPortal(
          <div
            ref={panelRef}
            role="menu"
            style={{ position: 'fixed', top: pos.top, left: pos.left, width: PANEL_WIDTH }}
            className="z-50 overflow-hidden rounded-xl border border-slate-200 bg-white py-1 shadow-lg"
          >
            {items.map((it) => (
              <button
                key={it.label}
                type="button"
                role="menuitem"
                onClick={() => {
                  setOpen(false)
                  it.onClick()
                }}
                className={cn(
                  'flex w-full items-center gap-2.5 px-4 py-2.5 text-left text-sm font-medium transition-colors',
                  it.danger ? 'text-red-600 hover:bg-red-50' : 'text-slate-700 hover:bg-slate-50',
                )}
              >
                <it.icon className={cn('h-4 w-4', it.danger ? 'text-red-500' : 'text-slate-400')} />
                {it.label}
              </button>
            ))}
          </div>,
          document.body,
        )}
    </>
  )
}
