import type { CSSProperties, ReactNode } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ChevronLeft, ChevronRight, RefreshCw, TriangleAlert, type LucideIcon } from 'lucide-react'
import { SECTIONS, isSectionActive, type SectionKey } from '@/pages/student/nav'
import { hasTelegramBackButton } from '@/pages/student/theme'
import { useBack } from '@/pages/student/hooks'
import { toneColors, type Tone } from '@/pages/student/format'
import { initials } from '@/pages/student/lib'

/* ============================================================
   Student cabinet — shared building blocks (components only; helpers live in
   format.ts / nav.ts / hooks.ts so Vite fast-refresh keeps working).
   ============================================================ */

/**
 * Screen header.
 * - `section` — a top-level group screen («Darslar», «Natijalar»): big title + section switch.
 * - `back`    — a sub-page: back arrow (hidden inside Telegram, whose native BackButton the layout
 *               shows instead) + compact title. `onBack` — an in-page back (e.g. a result view)
 *               that must always be visible.
 */
export function PageHeader({
  title,
  sub,
  back,
  onBack,
  section,
  right,
}: {
  title?: string
  sub?: ReactNode
  back?: string
  onBack?: () => void
  section?: SectionKey
  right?: ReactNode
}) {
  const isSub = !!back || !!onBack
  const text = title ?? (section ? SECTIONS[section].title : '')
  return (
    <div className={'st-hd' + (isSub ? ' sub' : '')}>
      <div className="st-hd-row">
        {onBack ? <BackButton onClick={onBack} /> : back ? <RouteBack to={back} /> : null}
        <div className="st-hd-title">{text}</div>
        {right}
      </div>
      {sub && <div className="st-hd-sub">{sub}</div>}
      {section && <SectionSwitch section={section} />}
    </div>
  )
}

function BackButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" className="st-back press" onClick={onClick} aria-label="Orqaga">
      <ChevronLeft size={22} strokeWidth={2.4} />
    </button>
  )
}

function RouteBack({ to }: { to: string }) {
  const back = useBack(to)
  // Inside Telegram the native BackButton (top bar) does the job — no duplicate arrow.
  if (hasTelegramBackButton()) return null
  return <BackButton onClick={back} />
}

/** Segmented switch between sibling screens of a group. Uses `replace` — switching is not "going deeper". */
export function SectionSwitch({ section }: { section: SectionKey }) {
  const { pathname } = useLocation()
  return (
    <nav className="st-seg" aria-label={SECTIONS[section].title}>
      {SECTIONS[section].items.map((it) => {
        const on = isSectionActive(it, pathname)
        return (
          <Link key={it.to} to={it.to} replace className={on ? 'on' : undefined} aria-current={on ? 'page' : undefined}>
            {it.label}
          </Link>
        )
      })}
    </nav>
  )
}

/** Section title row with an optional "Barchasi →" link. */
export function SectionTitle({
  title,
  sub,
  to,
  action = 'Barchasi',
  style,
}: {
  title: string
  sub?: string
  to?: string
  action?: string
  style?: CSSProperties
}) {
  return (
    <div className="st-sh" style={style}>
      <div style={{ minWidth: 0 }}>
        <div className="st-sh-title">{title}</div>
        {sub && <div className="st-sh-sub">{sub}</div>}
      </div>
      {to && (
        <Link to={to} className="st-sh-act">
          {action}
          <ChevronRight size={17} strokeWidth={2.4} />
        </Link>
      )}
    </div>
  )
}

export function IconBox({
  icon: Ic,
  tone = 'info',
  color,
  size = 40,
}: {
  icon: LucideIcon
  tone?: Tone
  /** Explicit colour (overrides tone) — e.g. a brand colour. */
  color?: string
  size?: number
}) {
  const t = toneColors(tone)
  const fg = color ?? t.fg
  const bg = color ? `color-mix(in srgb, ${color} 14%, transparent)` : t.bg
  return (
    <div className="st-ic" style={{ width: size, height: size, borderRadius: Math.round(size * 0.3), background: bg, color: fg }}>
      <Ic size={Math.round(size * 0.5)} strokeWidth={2.2} />
    </div>
  )
}

export function StatusChip({ tone = 'neutral', icon: Ic, children }: { tone?: Tone; icon?: LucideIcon; children: ReactNode }) {
  const cls = tone === 'neutral' ? '' : tone === 'violet' ? ' violet' : ` ${tone}`
  return (
    <span className={'st-chip' + cls}>
      {Ic && <Ic size={13} strokeWidth={2.6} />}
      {children}
    </span>
  )
}

export function EmptyState({
  icon: Ic,
  title,
  sub,
  tone = 'neutral',
  action,
  compact,
}: {
  icon: LucideIcon
  title: string
  sub?: ReactNode
  tone?: Tone
  action?: ReactNode
  compact?: boolean
}) {
  const t = toneColors(tone)
  return (
    <div className="empty" style={compact ? { padding: '24px 16px' } : undefined}>
      <div className="empty-ic" style={tone === 'neutral' ? undefined : { background: t.bg, color: t.fg }}>
        <Ic size={26} strokeWidth={2} />
      </div>
      <div className="st-empty-title">{title}</div>
      {sub && <div className="st-empty-sub">{sub}</div>}
      {action && <div style={{ marginTop: 16 }}>{action}</div>}
    </div>
  )
}

/** Load error with a retry button (`error-visibility.md` — the page's main data never fails silently). */
export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <EmptyState
      icon={TriangleAlert}
      tone="bad"
      title="Yuklab bo'lmadi"
      sub={message}
      action={
        onRetry && (
          <button type="button" className="btn btn-soft btn-sm press" onClick={onRetry}>
            <RefreshCw size={16} />
            Qayta urinish
          </button>
        )
      }
    />
  )
}

export function Skeleton({ h = 16, w = '100%', r = 10, style }: { h?: number; w?: number | string; r?: number; style?: CSSProperties }) {
  return <span className="st-skel" style={{ height: h, width: w, borderRadius: r, ...style }} aria-hidden />
}

/** Card-shaped placeholders while a list loads. */
export function SkeletonList({ rows = 3, card = true }: { rows?: number; card?: boolean }) {
  const body = (
    <div aria-busy="true" aria-label="Yuklanmoqda">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="st-li" style={{ cursor: 'default' }}>
          <Skeleton h={40} w={40} r={12} />
          <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 8 }}>
            <Skeleton h={14} w="62%" />
            <Skeleton h={12} w="40%" />
          </div>
        </div>
      ))}
    </div>
  )
  return card ? <div className="card flush">{body}</div> : body
}

export function Avatar({ name, size = 48, gradient = 'linear-gradient(135deg,#2563eb,#4338ca)' }: { name: string; size?: number; gradient?: string }) {
  return (
    <div
      className="subj"
      aria-hidden
      style={{ width: size, height: size, borderRadius: '50%', flex: 'none', background: gradient, color: '#fff', fontSize: Math.round(size * 0.38), letterSpacing: '-0.5px' }}
    >
      {initials(name)}
    </div>
  )
}

/** Pressable list row (link or button) inside a `.card.flush`. */
export function ListRow({
  to,
  onClick,
  icon,
  iconTone,
  iconColor,
  title,
  sub,
  end,
  chevron = !!(to || onClick),
  replace,
}: {
  to?: string
  onClick?: () => void
  icon?: LucideIcon
  iconTone?: Tone
  iconColor?: string
  title: ReactNode
  sub?: ReactNode
  end?: ReactNode
  chevron?: boolean
  replace?: boolean
}) {
  const inner = (
    <>
      {icon && <IconBox icon={icon} tone={iconTone} color={iconColor} />}
      <div className="st-li-main">
        <div className="st-li-title">{title}</div>
        {sub && <div className="st-li-sub">{sub}</div>}
      </div>
      {end && <div className="st-li-end">{end}</div>}
      {chevron && <ChevronRight size={18} color="var(--faint)" style={{ flex: 'none' }} />}
    </>
  )
  if (to)
    return (
      <Link to={to} replace={replace} className="st-li press">
        {inner}
      </Link>
    )
  if (onClick)
    return (
      <button type="button" onClick={onClick} className="st-li press">
        {inner}
      </button>
    )
  return <div className="st-li">{inner}</div>
}

export function Toast({ text }: { text: string | null }) {
  if (!text) return null
  return (
    <div className="toast" role="status" aria-live="polite">
      {text}
    </div>
  )
}
