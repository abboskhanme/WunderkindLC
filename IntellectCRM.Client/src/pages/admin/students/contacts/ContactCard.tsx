import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Link } from 'react-router-dom'
import {
  AlertTriangle, CalendarClock, Clock, History, MessageSquarePlus, Phone, PhoneCall,
  RotateCcw, Sun, Trash2, UserPlus,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { ContactRequestItem } from '@/api/services/contacts'
import { bucketOf } from '@/lib/contactDue'
import { avatarColor, initials } from '@/lib/avatar'
import { DropdownMenu, type DropdownMenuItem } from '@/components/ui/DropdownMenu'
import { cn, formatDate } from '@/lib/utils'

/* ======================================================================================
 *  KARTA RANGI — HAR DOIM SHOSHILINCHLIK bo'yicha, ustunga qarab EMAS
 * ====================================================================================== */

interface Tone {
  /** Chap chiziq (to'q) */
  accent: string
  /** Karta foni (yumshoq tint) */
  bg: string
  /** Muddat yorlig'i */
  chip: string
  label: string
  icon: LucideIcon
}

/**
 * ⚠️ Rang USTUNDAN emas, talabning o'zidan olinadi. Shuning uchun "Bosqich" rejimida ham
 * "Qayta qo'ng'iroq" ustunidagi KECHIKKAN karta qizil bo'lib ko'zga tashlanadi — aks holda
 * muddati o'tganlar bosqich rejimida ko'rinmay qolardi.
 */
const TONES: Record<string, Tone> = {
  overdue: { accent: '#e11d48', bg: '#fff1f2', chip: 'bg-rose-100 text-rose-700', label: "Muddati o'tgan", icon: AlertTriangle },
  today: { accent: '#f59e0b', bg: '#fffbeb', chip: 'bg-amber-100 text-amber-700', label: 'Bugun', icon: Sun },
  nodate: { accent: '#8b5cf6', bg: '#faf5ff', chip: 'bg-violet-100 text-violet-700', label: "Yangi talab", icon: UserPlus },
  tomorrow: { accent: '#0284c7', bg: '#f0f9ff', chip: 'bg-sky-100 text-sky-700', label: 'Ertaga', icon: CalendarClock },
  week: { accent: '#06b6d4', bg: '#ecfeff', chip: 'bg-cyan-100 text-cyan-700', label: 'Shu hafta', icon: CalendarClock },
  later: { accent: '#94a3b8', bg: '#f8fafc', chip: 'bg-slate-100 text-slate-600', label: 'Keyinroq', icon: CalendarClock },
  done: { accent: '#10b981', bg: '#ecfdf5', chip: 'bg-emerald-100 text-emerald-700', label: "Hal bo'ldi", icon: History },
  failed: { accent: '#64748b', bg: '#f8fafc', chip: 'bg-slate-200 text-slate-600', label: "Bog'lanib bo'lmadi", icon: History },
}

/** Talab qaysi rang guruhiga tushadi (yakuniylar — bosqichi bo'yicha, ochiqlar — muddati bo'yicha). */
function toneKeyOf(r: ContactRequestItem, today: string): string {
  if (r.status === 'done' || r.status === 'failed') return r.status
  return bucketOf(r.status, r.dueDate, today) || 'nodate'
}

/** Talab ochilganidan beri necha kun kutmoqda (`null` — sana buzuq). */
function waitingDays(createdAt: string): number | null {
  const t = Date.parse(createdAt)
  if (Number.isNaN(t)) return null
  const days = Math.floor((Date.now() - t) / 86_400_000)
  return days < 0 ? 0 : days
}

/* ======================================================================================
 *  KARTA
 * ====================================================================================== */

export interface ContactCardActions {
  /** "Bog'lanildi" oynasini ochish (faqat ochiq talab uchun). */
  onAttempt: (r: ContactRequestItem) => void
  onDetail: (r: ContactRequestItem) => void
  onNote: (r: ContactRequestItem) => void
  onReopen: (r: ContactRequestItem) => void
  onDelete: (r: ContactRequestItem) => void
  canWrite: boolean
  canDelete: boolean
}

/**
 * Faqat KO'RINISH — sudrash paytidagi `DragOverlay` ham shuni chizadi (lidlar taxtasidagi
 * `LeadCardContent` bilan bir xil naqsh).
 */
export function ContactCardContent({
  r,
  today,
  actions,
  dragging,
}: {
  r: ContactRequestItem
  today: string
  /** Berilmasa karta "o'lik" bo'ladi (drag overlay). */
  actions?: ContactCardActions
  dragging?: boolean
}) {
  const t = TONES[toneKeyOf(r, today)] ?? TONES.later
  const Icon = t.icon
  const open = r.status === 'new' || r.status === 'callback'
  const waited = waitingDays(r.createdAt)

  const menu: DropdownMenuItem[] = []
  if (actions) {
    menu.push({ label: 'Tarix', icon: History, onClick: () => actions.onDetail(r) })
    if (actions.canWrite) {
      menu.push({ label: "Izoh qo'shish", icon: MessageSquarePlus, onClick: () => actions.onNote(r) })
      if (!open) menu.push({ label: 'Qayta ochish', icon: RotateCcw, onClick: () => actions.onReopen(r) })
    }
    if (actions.canDelete) {
      menu.push({ label: "O'chirish", icon: Trash2, onClick: () => actions.onDelete(r), danger: true })
    }
  }

  return (
    <div
      className={cn(
        'flex flex-col gap-2 rounded-lg border border-slate-200 p-3 shadow-[var(--shadow-1)] transition-shadow',
        dragging ? 'rotate-2 cursor-grabbing opacity-70 shadow-[var(--shadow-pop)]' : 'hover:shadow-[var(--shadow-2)]',
      )}
      style={{ background: t.bg, borderLeftWidth: 3, borderLeftColor: t.accent }}
    >
      {/* Ism + sabab */}
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-center gap-2">
          <span
            className="avatar h-7 w-7 shrink-0 text-[11px]"
            style={{ background: avatarColor(r.studentName) }}
          >
            {initials(r.studentName)}
          </span>
          <div className="min-w-0">
            {actions ? (
              <Link
                to={`/admin/students/${r.studentId}`}
                onClick={(e) => e.stopPropagation()}
                onPointerDown={(e) => e.stopPropagation()}
                className="block truncate text-[13.5px] font-semibold leading-tight tracking-tight text-slate-800 hover:text-brand-600 hover:underline"
              >
                {r.studentName}
              </Link>
            ) : (
              <div className="truncate text-[13.5px] font-semibold leading-tight tracking-tight text-slate-800">
                {r.studentName}
              </div>
            )}
            <div className="mt-0.5 truncate text-[11.5px] text-slate-500">
              {r.reasonLabel || '— sababsiz —'}
            </div>
          </div>
        </div>

        {menu.length > 0 && (
          <span onPointerDown={(e) => e.stopPropagation()} onClick={(e) => e.stopPropagation()}>
            <DropdownMenu items={menu} />
          </span>
        )}
      </div>

      {/* Muddat + urinishlar + kutish */}
      <div className="flex flex-wrap items-center gap-1.5">
        <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10.5px] font-semibold', t.chip)}>
          <Icon className="h-3 w-3" />
          {t.label}
          {r.dueDate && r.status === 'callback' && ` · ${formatDate(r.dueDate)}`}
        </span>
        {r.attemptCount > 0 && (
          <span
            title="Nechta bog'lanish urinishi bo'lgan"
            className="inline-flex items-center gap-1 rounded-full bg-white/70 px-2 py-0.5 text-[10.5px] font-semibold text-slate-500"
          >
            <PhoneCall className="h-3 w-3" /> {r.attemptCount}
          </span>
        )}
        {open && waited != null && waited >= 3 && (
          <span
            title={`Navbatda ${waited} kundan beri turibdi`}
            className="inline-flex items-center gap-1 rounded-full bg-white/70 px-2 py-0.5 text-[10.5px] font-semibold text-slate-500"
          >
            <Clock className="h-3 w-3" /> {waited} kun
          </span>
        )}
      </div>

      {/* Oxirgi javob — operator shu yerdan davom etadi */}
      {(r.lastResponse || r.note) && (
        <p className="line-clamp-2 text-[12px] leading-snug text-slate-600">
          {r.lastResponse ? (
            <>
              <span className="text-slate-400">Javobi: </span>
              {r.lastResponse}
            </>
          ) : (
            r.note
          )}
        </p>
      )}

      {/* Telefonlar — jurnaldan darhol qo'ng'iroq qilish uchun */}
      {r.phones.length > 0 && (
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-t border-slate-200/70 pt-2">
          {r.phones.slice(0, 2).map((p) =>
            actions ? (
              <a
                key={p}
                href={`tel:${p}`}
                onClick={(e) => e.stopPropagation()}
                onPointerDown={(e) => e.stopPropagation()}
                className="inline-flex items-center gap-1 font-mono text-[11.5px] font-semibold text-emerald-600 hover:underline"
              >
                <Phone className="h-3 w-3" /> {p}
              </a>
            ) : (
              <span key={p} className="inline-flex items-center gap-1 font-mono text-[11.5px] font-semibold text-emerald-600">
                <Phone className="h-3 w-3" /> {p}
              </span>
            ),
          )}
        </div>
      )}

      {/* Asosiy amal */}
      {actions?.canWrite && (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation()
            if (open) actions.onAttempt(r)
            else actions.onReopen(r)
          }}
          onPointerDown={(e) => e.stopPropagation()}
          className={cn(
            'inline-flex w-full items-center justify-center gap-1.5 rounded-lg px-3 py-1.5 text-[12px] font-semibold transition-colors',
            open
              ? 'bg-brand-600 text-white hover:bg-brand-500'
              : 'border border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
          )}
        >
          {open ? (
            <>
              <PhoneCall className="h-3.5 w-3.5" /> Bog'lanildi
            </>
          ) : (
            <>
              <RotateCcw className="h-3.5 w-3.5" /> Qayta ochish
            </>
          )}
        </button>
      )}
    </div>
  )
}

/**
 * SUDRALADIGAN karta.
 *
 * ⚠️ Yakuniy (done/failed) talab SUDRALMAYDI: serverda `attempt` faqat OCHIQ talabga
 * yoziladi ("Talab yakunlangan — avval uni qayta oching"). Sudrab bo'lmasligi bu qoidani
 * foydalanuvchiga darhol ko'rsatadi — u xato oynasiga urilib qolmaydi.
 */
export function ContactCard({
  r,
  today,
  actions,
  onClick,
}: {
  r: ContactRequestItem
  today: string
  actions: ContactCardActions
  onClick: () => void
}) {
  const open = r.status === 'new' || r.status === 'callback'
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: r.id,
    disabled: !open || !actions.canWrite,
  })

  return (
    <div
      ref={setNodeRef}
      style={transform ? { transform: CSS.Translate.toString(transform) } : undefined}
      {...listeners}
      {...attributes}
      onClick={onClick}
      className={cn('touch-none', open && actions.canWrite && 'cursor-grab', isDragging && 'opacity-40')}
    >
      <ContactCardContent r={r} today={today} actions={actions} />
    </div>
  )
}
