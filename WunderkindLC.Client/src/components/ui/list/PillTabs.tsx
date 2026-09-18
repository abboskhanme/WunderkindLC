import type { ComponentType, ReactNode } from 'react'
import { NavLink } from 'react-router-dom'
import { cn } from '@/lib/utils'

type IconType = ComponentType<{ className?: string }>

export interface PillTabItem<K extends string = string> {
  key: K
  label: ReactNode
  icon?: IconType
  /** Berilsa tab chizilmaydi (masalan ruxsat yo'q). */
  hidden?: boolean
}

const pill =
  'inline-flex h-8 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-lg px-3 text-[13px] font-medium transition-colors'
const pillIdle = 'bg-[#f0f2f2] text-[#333] hover:bg-[#e6eaea]'
const pillActive = 'bg-brand-600 text-white shadow-[0_1px_2px_rgba(0,0,0,0.08)]'

/**
 * edutizimdagi tablar (guruh sahifasi, Xonalar): oq quti ichida kulrang "pill" tugmalar,
 * tanlangani — ko'k (primary) to'ldirilgan, oq matn. Ikonka ixtiyoriy (Tabler, 16px).
 * Tor ekranda qator gorizontal aylanadi (sahifa emas).
 */
export function PillTabs<K extends string>({
  items,
  value,
  onChange,
  className,
}: {
  items: PillTabItem<K>[]
  value: K
  onChange: (key: K) => void
  className?: string
}) {
  return (
    <div
      role="tablist"
      className={cn(
        'flex gap-1.5 overflow-x-auto rounded-xl border border-[#dbe0e6] bg-white p-1.5 shadow-[0_1px_2px_rgba(0,0,0,0.05)]',
        className,
      )}
    >
      {items
        .filter((i) => !i.hidden)
        .map((i) => {
          const Icon = i.icon
          const active = i.key === value
          return (
            <button
              key={i.key}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => onChange(i.key)}
              className={cn(pill, active ? pillActive : pillIdle)}
            >
              {Icon && <Icon className="h-4 w-4" />}
              {i.label}
            </button>
          )
        })}
    </div>
  )
}

/**
 * Xuddi shu ko'rinish, lekin har tab ALOHIDA marshrut (`NavLink`) — deep-link va "orqaga"
 * tugmasi ishlaydi (masalan Xonalar · Analitika).
 */
export function PillNavTabs({
  items,
  className,
}: {
  items: { label: ReactNode; to: string; end?: boolean; icon?: IconType; hidden?: boolean }[]
  className?: string
}) {
  return (
    <nav
      className={cn(
        'flex gap-1.5 overflow-x-auto rounded-xl border border-[#dbe0e6] bg-white p-1.5 shadow-[0_1px_2px_rgba(0,0,0,0.05)]',
        className,
      )}
    >
      {items
        .filter((i) => !i.hidden)
        .map((i) => {
          const Icon = i.icon
          return (
            <NavLink
              key={i.to}
              to={i.to}
              end={i.end}
              className={({ isActive }) => cn(pill, isActive ? pillActive : pillIdle)}
            >
              {Icon && <Icon className="h-4 w-4" />}
              {i.label}
            </NavLink>
          )
        })}
    </nav>
  )
}
