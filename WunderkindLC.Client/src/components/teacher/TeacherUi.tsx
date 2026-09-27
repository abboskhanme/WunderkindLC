import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ArrowLeft, ChevronRight, Clock, DoorOpen, Repeat, UserCheck, Users, type LucideIcon } from 'lucide-react'
import type { TeacherLesson } from '@/api/services/teacher'
import { cn } from '@/lib/utils'
import {
  PHASE_STYLE,
  canTakeAttendance,
  initials,
  lessonPhase,
  type LessonPhase,
} from '@/lib/teacherPortal'

/**
 * O'qituvchi portali — umumiy KO'RINISH bloklari (admin tizimi uslubida: oq karta, 12px radius,
 * `#dbe0e6` chegara, Nunito, brand ko'k). Ranglar `.teacher-app` tokenlaridan olinadi
 * (`text-ink`, `border-line`, ...) — tungi rejim shu tokenlar orqali o'zi moslashadi.
 */

/** Sahifa qobig'i: sarlavha (+ orqaga, amallar) va bir xil chekkalar. */
export function TPage({
  title,
  sub,
  back,
  actions,
  children,
  className,
}: {
  title: ReactNode
  sub?: ReactNode
  /** Orqaga tugmasi: manzil yoki `true` (brauzer tarixi) */
  back?: string | true
  actions?: ReactNode
  children: ReactNode
  className?: string
}) {
  const nav = useNavigate()
  return (
    <div className={cn('px-3 pb-6 pt-3 sm:px-4 lg:px-6 lg:pt-5', className)}>
      <div className="mb-3 flex flex-wrap items-center gap-2.5 lg:mb-4">
        {back && (
          <button
            type="button"
            onClick={() => (back === true ? nav(-1) : nav(back))}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-brand-600/20 bg-brand-600/10 text-brand-600 transition-colors hover:bg-brand-600/15"
            aria-label="Orqaga"
            title="Orqaga"
          >
            <ArrowLeft className="h-5 w-5" />
          </button>
        )}
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-[18px] font-bold leading-tight text-ink lg:text-[20px]">{title}</h1>
          {sub && <p className="mt-0.5 truncate text-[13px] text-mute">{sub}</p>}
        </div>
        {actions && <div className="flex w-full flex-wrap items-center gap-2 sm:w-auto">{actions}</div>}
      </div>
      {children}
    </div>
  )
}

/** Oq karta (admin `Card` bilan bir xil ko'rinish, lekin tokenlarda — tungi rejim uchun). */
export function TCard({
  title,
  sub,
  actions,
  children,
  className,
  bodyClassName,
  tight,
}: {
  title?: ReactNode
  sub?: ReactNode
  actions?: ReactNode
  children: ReactNode
  className?: string
  bodyClassName?: string
  /** Ichki paddingsiz (ro'yxat/jadval) */
  tight?: boolean
}) {
  const header = title != null || actions != null
  return (
    <section className={cn('rounded-xl border border-line bg-white shadow-[var(--shadow-card)]', className)}>
      {header && (
        <div className="flex items-center justify-between gap-2 border-b border-line-soft px-4 py-3">
          <div className="min-w-0">
            {title != null && <h2 className="truncate text-[15px] font-bold text-ink">{title}</h2>}
            {sub != null && <div className="mt-0.5 text-xs text-mute">{sub}</div>}
          </div>
          {actions != null && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={cn(!tight && 'p-4', bodyClassName)}>{children}</div>
    </section>
  )
}

const TONES = {
  brand: 'bg-brand-50 text-brand-600',
  green: 'bg-emerald-50 text-emerald-600',
  amber: 'bg-amber-50 text-amber-600',
  violet: 'bg-violet-50 text-violet-600',
  sky: 'bg-sky-50 text-sky-600',
  red: 'bg-red-50 text-red-600',
} as const

/** Ko'rsatkich plitkasi (admin StatCard uslubi, telefonda ixcham). */
export function StatTile({
  icon: Icon,
  label,
  value,
  hint,
  tone = 'brand',
  to,
}: {
  icon: LucideIcon
  label: string
  value: ReactNode
  hint?: ReactNode
  tone?: keyof typeof TONES
  to?: string
}) {
  const body = (
    <div className="flex h-full items-center gap-3 rounded-xl border border-line bg-white p-3 shadow-[var(--shadow-card)] transition-colors sm:p-4 lg:gap-4">
      <div className={cn('flex h-10 w-10 shrink-0 items-center justify-center rounded-xl sm:h-12 sm:w-12', TONES[tone])}>
        <Icon className="h-5 w-5 sm:h-6 sm:w-6" />
      </div>
      <div className="min-w-0">
        <p className="truncate text-[12px] text-mute sm:text-[13px]">{label}</p>
        <p className="truncate text-[19px] font-bold leading-tight text-ink sm:text-[22px]">{value}</p>
        {hint && <p className="truncate text-[11px] text-faint sm:text-xs">{hint}</p>}
      </div>
    </div>
  )
  return to ? (
    <Link to={to} className="block h-full rounded-xl hover:[&>div]:border-brand-300">
      {body}
    </Link>
  ) : (
    body
  )
}

/** Bo'sh holat — nima uchun bo'sh va nima qilish kerakligi bilan. */
export function EmptyState({
  icon: Icon,
  title,
  text,
  action,
  className,
}: {
  icon: LucideIcon
  title: string
  text?: ReactNode
  action?: ReactNode
  className?: string
}) {
  return (
    <div className={cn('flex flex-col items-center px-6 py-10 text-center', className)}>
      <div className="mb-3 flex h-14 w-14 items-center justify-center rounded-2xl bg-panel3 text-faint">
        <Icon className="h-7 w-7" />
      </div>
      <p className="text-[15px] font-bold text-ink">{title}</p>
      {text && <p className="mt-1 max-w-sm text-[13px] text-mute">{text}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

/** Yuklanish skeleti (qatorlar). */
export function SkeletonRows({ rows = 3, className }: { rows?: number; className?: string }) {
  return (
    <div className={cn('space-y-2.5', className)}>
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="skeleton h-[72px] rounded-xl" />
      ))}
    </div>
  )
}

export function PhaseChip({ phase, text }: { phase: LessonPhase; text?: string }) {
  const s = PHASE_STYLE[phase]
  return (
    <span className={cn('inline-flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-md border px-2 py-0.5 text-[11px] font-semibold', s.cls)}>
      <span className={cn('h-1.5 w-1.5 rounded-full', s.dot)} />
      {text ?? s.label}
    </span>
  )
}

/** O'quvchi/guruh avatari: rasm bo'lsa rasm, aks holda bosh harflar. */
export function Avatar({ name, photoUrl, size = 36, className }: { name: string; photoUrl?: string; size?: number; className?: string }) {
  const style = { width: size, height: size, fontSize: Math.max(11, Math.round(size * 0.36)) }
  if (photoUrl)
    return <img src={photoUrl} alt="" style={style} className={cn('shrink-0 rounded-full border border-line object-cover', className)} />
  return (
    <span style={style} className={cn('flex shrink-0 items-center justify-center rounded-full bg-brand-50 font-bold text-brand-600', className)}>
      {initials(name)}
    </span>
  )
}

/**
 * Bitta dars qatori — bosh sahifa, jadval va davomat sahifasida BIR XIL.
 * Chapda vaqt bloki, o'rtada guruh/kurs/xona, o'ngda holat + "Davomat" tugmasi.
 */
export function LessonRow({
  lesson,
  today,
  now,
  showDate,
  compact,
}: {
  lesson: TeacherLesson
  today: string
  now: string
  /** Sanani ham ko'rsatish (bir necha kunlik ro'yxatda) */
  showDate?: string
  compact?: boolean
}) {
  const phase = lessonPhase(lesson, today, now)
  const canAttend = canTakeAttendance(lesson, today)
  const chipText = phase === 'substituted' ? `O'rinbosar: ${lesson.substitutedBy}` : undefined
  const attendHref = `/teacher/attendance?group=${encodeURIComponent(lesson.groupId)}&date=${lesson.date}`
  return (
    <div
      className={cn(
        'flex items-stretch gap-3 rounded-xl border border-line bg-white p-3 transition-colors',
        phase === 'now' && 'border-brand-300 ring-1 ring-brand-200',
      )}
    >
      <div
        className={cn(
          'flex w-[58px] shrink-0 flex-col items-center justify-center rounded-lg px-1 py-1.5 text-center',
          phase === 'now' ? 'bg-brand-600 text-white' : 'bg-panel3 text-ink',
        )}
      >
        <Clock className={cn('mb-0.5 h-3.5 w-3.5', phase === 'now' ? 'text-white/80' : 'text-faint')} />
        <span className="text-[13px] font-bold leading-tight">{lesson.startTime || '—'}</span>
        {lesson.endTime && (
          <span className={cn('text-[11px] leading-tight', phase === 'now' ? 'text-white/80' : 'text-mute')}>
            {lesson.endTime}
          </span>
        )}
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-1.5">
          <Link to={`/teacher/groups/${lesson.groupId}`} className="truncate text-[15px] font-bold text-ink hover:text-brand-600">
            {lesson.groupName}
          </Link>
          {lesson.isSubstitute && (
            <span className="inline-flex items-center gap-1 rounded-md bg-violet-50 px-1.5 py-0.5 text-[10.5px] font-bold text-violet-700">
              <UserCheck className="h-3 w-3" /> O'rinbosar
            </span>
          )}
          {lesson.moved && (
            <span className="inline-flex items-center gap-1 rounded-md bg-sky-50 px-1.5 py-0.5 text-[10.5px] font-bold text-sky-700">
              <Repeat className="h-3 w-3" /> Ko'chirilgan
            </span>
          )}
        </div>
        <p className="truncate text-[12.5px] text-mute">
          {showDate ? `${showDate} · ` : ''}
          {lesson.courseName}
        </p>
        <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[12px] text-mute">
          {lesson.room && (
            <span className="inline-flex items-center gap-1">
              <DoorOpen className="h-3.5 w-3.5 text-faint" /> {lesson.room}
            </span>
          )}
          <span className="inline-flex items-center gap-1">
            <Users className="h-3.5 w-3.5 text-faint" /> {lesson.studentCount} o'quvchi
          </span>
        </div>
        {compact ? null : (
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <PhaseChip phase={phase} text={chipText} />
          </div>
        )}
      </div>

      {canAttend && (
        <div className="flex shrink-0 flex-col items-end justify-center gap-2">
          {compact && <PhaseChip phase={phase} text={chipText} />}
          <Link
            to={attendHref}
            className={cn(
              'inline-flex h-9 items-center gap-1 rounded-lg px-3 text-[13px] font-semibold transition-colors',
              phase === 'done'
                ? 'border border-line bg-white text-brand-600 hover:bg-brand-50'
                : 'bg-brand-600 text-white hover:bg-brand-700',
            )}
          >
            {phase === 'done' ? "Ko'rish" : 'Davomat'}
            <ChevronRight className="h-4 w-4" />
          </Link>
        </div>
      )}
    </div>
  )
}

/** Segmentli tanlov (tablar o'rniga — telefonda ham sig'adi). */
export function Segmented<T extends string>({
  value,
  onChange,
  options,
  className,
}: {
  value: T
  onChange: (v: T) => void
  options: { value: T; label: ReactNode; count?: number }[]
  className?: string
}) {
  return (
    <div className={cn('flex gap-1 overflow-x-auto rounded-lg border border-line bg-paper2 p-1', className)}>
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          onClick={() => onChange(o.value)}
          className={cn(
            'flex shrink-0 items-center gap-1.5 whitespace-nowrap rounded-md px-3 py-1.5 text-[13px] font-semibold transition-colors',
            value === o.value ? 'bg-white text-brand-600 shadow-sm' : 'text-mute hover:text-ink',
          )}
        >
          {o.label}
          {o.count !== undefined && (
            <span
              className={cn(
                'rounded px-1.5 text-[11px] font-bold',
                value === o.value ? 'bg-brand-50 text-brand-600' : 'bg-white text-mute',
              )}
            >
              {o.count}
            </span>
          )}
        </button>
      ))}
    </div>
  )
}
