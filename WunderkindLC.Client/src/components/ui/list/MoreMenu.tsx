import { useEffect, useLayoutEffect, useRef, useState, type ComponentType, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { IconDotsVertical } from '@tabler/icons-react'
import { cn } from '@/lib/utils'
import { TintedIconButton } from './TintedIconButton'

export interface MoreMenuItem {
  label: string
  icon?: ComponentType<{ className?: string }>
  onClick: () => void
  danger?: boolean
  /** Berilsa band chizilmaydi (ruxsat yo'q va h.k.). */
  hidden?: boolean
}

const PANEL_WIDTH = 240

/**
 * edutizim asboblar qatoridagi och-ko'k "⋮" tugma + ochiladigan menyu (edutizim flyout
 * ko'rinishi: oq, radius 12, yumshoq soya, bandlar 36px). Panel `document.body` ga PORTAL —
 * `overflow` li ota elementlar ichida qirqilmaydi.
 */
export function MoreMenu({
  items,
  label = "Ko'proq",
  trigger,
  align = 'right',
}: {
  items: MoreMenuItem[]
  label?: string
  /** Standart "⋮" o'rniga boshqa ikonka. */
  trigger?: ReactNode
  align?: 'left' | 'right'
}) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null)
  const wrapRef = useRef<HTMLSpanElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const visible = items.filter((i) => !i.hidden)

  useLayoutEffect(() => {
    if (!open || !wrapRef.current) return
    const r = wrapRef.current.getBoundingClientRect()
    const left = align === 'right' ? r.right - PANEL_WIDTH : r.left
    setPos({ top: r.bottom + 4, left: Math.max(8, left) })
  }, [open, align])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node
      if (wrapRef.current?.contains(t) || panelRef.current?.contains(t)) return
      setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    const onScroll = () => setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onScroll)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onScroll)
    }
  }, [open])

  if (visible.length === 0) return null

  return (
    <>
      <span ref={wrapRef} className="inline-flex">
        <TintedIconButton label={label} pressed={open} onClick={() => setOpen((o) => !o)}>
          {trigger ?? <IconDotsVertical className="h-5 w-5" />}
        </TintedIconButton>
      </span>
      {open &&
        pos &&
        createPortal(
          <div
            ref={panelRef}
            role="menu"
            style={{ position: 'fixed', top: pos.top, left: pos.left, width: PANEL_WIDTH }}
            className="z-50 rounded-xl border border-[#dbe0e6] bg-white py-1.5 shadow-[0_6px_20px_-4px_rgba(24,39,75,0.08),0_12px_48px_-4px_rgba(24,39,75,0.10)]"
          >
            {visible.map((it) => {
              const Icon = it.icon
              return (
                <button
                  key={it.label}
                  type="button"
                  role="menuitem"
                  onClick={() => {
                    setOpen(false)
                    it.onClick()
                  }}
                  className={cn(
                    'mx-1.5 flex h-9 w-[calc(100%-12px)] items-center gap-2.5 rounded-lg px-2.5 text-left text-[14px] font-semibold transition-colors',
                    it.danger
                      ? 'text-[#d32f2f] hover:bg-red-50'
                      : 'text-[#333] hover:bg-[#e6eaea] hover:text-brand-600',
                  )}
                >
                  {Icon && <Icon className="h-[18px] w-[18px] shrink-0" />}
                  <span className="truncate">{it.label}</span>
                </button>
              )
            })}
          </div>,
          document.body,
        )}
    </>
  )
}
