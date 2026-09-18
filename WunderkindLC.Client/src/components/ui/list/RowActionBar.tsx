import { useEffect, useLayoutEffect, useRef, useState, type ComponentType } from 'react'
import { createPortal } from 'react-dom'
import { IconDotsVertical, IconX } from '@tabler/icons-react'
import { cn } from '@/lib/utils'

export interface RowAction {
  /** Maslahat (tooltip) matni — edutizimdagi qora tooltip. */
  label: string
  icon: ComponentType<{ className?: string }>
  onClick: () => void
  danger?: boolean
  hidden?: boolean
}

/**
 * edutizim jadval qatoridagi "⋮": bosilganda qator USTIDA suzuvchi oq "pill" asboblar paneli
 * ochiladi — har amal ikonka tugma (ustiga kelganda qora tooltip), oxirida "X" (yopish).
 *
 * Panel `document.body` ga PORTAL (position: fixed) — jadvalning `overflow` konteyneri uni
 * qirqmaydi. Trigger'ning O'NG chetiga tekislanadi va qatorning o'rtasida turadi.
 * Tashqariga bosish, scroll yoki Escape — yopadi.
 */
export function RowActionBar({ actions, label = 'Amallar' }: { actions: RowAction[]; label?: string }) {
  const [open, setOpen] = useState(false)
  const [pos, setPos] = useState<{ top: number; right: number } | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const barRef = useRef<HTMLDivElement>(null)
  const visible = actions.filter((a) => !a.hidden)

  useLayoutEffect(() => {
    if (!open || !triggerRef.current) return
    const r = triggerRef.current.getBoundingClientRect()
    setPos({ top: r.top + r.height / 2, right: Math.max(8, window.innerWidth - r.right) })
  }, [open])

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node
      if (triggerRef.current?.contains(t) || barRef.current?.contains(t)) return
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
      <button
        ref={triggerRef}
        type="button"
        title={label}
        aria-label={label}
        aria-expanded={open}
        onClick={(e) => {
          e.stopPropagation()
          setOpen((o) => !o)
        }}
        className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-[#333] transition-colors hover:bg-black/[0.06]"
      >
        <IconDotsVertical className="h-5 w-5" />
      </button>
      {open &&
        pos &&
        createPortal(
          <div
            ref={barRef}
            role="toolbar"
            style={{ position: 'fixed', top: pos.top, right: pos.right, transform: 'translateY(-50%)' }}
            className="z-50 flex items-center gap-0.5 rounded-xl border border-[#dbe0e6] bg-white px-1.5 py-1 shadow-[0_4px_16px_rgba(0,0,0,0.12)]"
          >
            {visible.map((a) => {
              const Icon = a.icon
              return (
                <span key={a.label} className="group relative">
                  <button
                    type="button"
                    aria-label={a.label}
                    onClick={() => {
                      setOpen(false)
                      a.onClick()
                    }}
                    className={cn(
                      'inline-flex h-8 w-8 items-center justify-center rounded-lg transition-colors',
                      a.danger ? 'text-[#e34a29] hover:bg-red-50' : 'text-brand-600 hover:bg-brand-600/10',
                    )}
                  >
                    <Icon className="h-5 w-5" />
                  </button>
                  <span className="pointer-events-none absolute left-1/2 top-full z-10 mt-1.5 -translate-x-1/2 whitespace-nowrap rounded bg-[#616161] px-2 py-1 text-[11px] font-medium text-white opacity-0 transition-opacity group-hover:opacity-100">
                    {a.label}
                  </span>
                </span>
              )
            })}
            <span className="mx-0.5 h-5 w-px bg-[#dbe0e6]" />
            <button
              type="button"
              aria-label="Yopish"
              title="Yopish"
              onClick={() => setOpen(false)}
              className="inline-flex h-8 w-8 items-center justify-center rounded-lg bg-[#f0f2f2] text-[#333] transition-colors hover:bg-[#e6eaea]"
            >
              <IconX className="h-4 w-4" />
            </button>
          </div>,
          document.body,
        )}
    </>
  )
}
