import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Phone, Clock, Repeat2 } from 'lucide-react'
import type { Lead } from '@/types'
import { genderLabels } from '@/config/constants'
import { Badge } from '@/components/ui/Badge'
import { formatDate, formatDateTime, cn } from '@/lib/utils'
import { avatarColor, initials } from '@/lib/avatar'

/** Lid yaratilganidan beri o'tgan kun (createdAt "yyyy-MM-ddTHH:mm:ss"). */
function leadAgeDays(createdAt?: string): number | null {
  if (!createdAt) return null
  const d = new Date(createdAt)
  if (isNaN(d.getTime())) return null
  const days = Math.floor((Date.now() - d.getTime()) / 86_400_000)
  return days < 0 ? 0 : days
}

/**
 * Lid rangi: o'quvchiga AYLANTIRILGAN bo'lsa YASHIL; aks holda lidlar bo'limida qancha uzoq
 * qolib ketgan bo'lsa shuncha QIZARADI (yangi → kulrang, eski/qolib ketgan → qizil) — qaysi
 * lidlarga zudlik bilan e'tibor kerakligini ko'rsatadi.
 */
function leadAging(lead: Lead): {
  /** Chap chiziq/ramka rangi (to'q) */
  accent: string
  /** BUTUN karta foni (yumshoq tint) */
  bg: string
  chipBg: string
  chipText: string
  days: number | null
  converted: boolean
} {
  const converted = !!lead.convertedStudentId
  const days = leadAgeDays(lead.createdAt)
  if (converted) return { accent: '#10b981', bg: '#ecfdf5', chipBg: '', chipText: '', days, converted }
  if (days == null) return { accent: '#cbd5e1', bg: '#f8fafc', chipBg: 'bg-slate-100', chipText: 'text-slate-500', days, converted }
  if (days >= 14) return { accent: '#dc2626', bg: '#fef2f2', chipBg: 'bg-red-50', chipText: 'text-red-600', days, converted }
  if (days >= 7) return { accent: '#ea580c', bg: '#fff7ed', chipBg: 'bg-orange-50', chipText: 'text-orange-600', days, converted }
  if (days >= 3) return { accent: '#f59e0b', bg: '#fffbeb', chipBg: 'bg-amber-50', chipText: 'text-amber-700', days, converted }
  return { accent: '#94a3b8', bg: '#f8fafc', chipBg: 'bg-slate-100', chipText: 'text-slate-500', days, converted }
}

function ageLabel(days: number): string {
  if (days <= 0) return 'Bugun'
  return `${days} kun`
}

/** Faqat ko'rinish (drag overlay uchun ham ishlatiladi) */
export function LeadCardContent({
  lead,
  dragging,
  onCall,
}: {
  lead: Lead
  dragging?: boolean
  /** Berilsa telefon raqami bosiladigan bo'ladi (drag overlay'da berilmaydi) */
  onCall?: (lead: Lead) => void
}) {
  const phone = lead.phone || lead.fatherPhone || lead.motherPhone || ''
  const meta = lead.interestSubject || genderLabels[lead.gender]

  // Birinchi dars davomat rangi
  const attendanceColor =
    lead.firstLessonAttendance === 'attended'
      ? 'bg-emerald-50'
      : lead.firstLessonAttendance === 'absent'
        ? 'bg-rose-50'
        : 'bg-slate-50'

  const attendanceText =
    lead.firstLessonAttendance === 'attended'
      ? '✓ Keldi'
      : lead.firstLessonAttendance === 'absent'
        ? '✗ Kelmadi'
        : lead.convertedStudentId
          ? '— Dars yo\'q'
          : ''

  const attendanceTextColor =
    lead.firstLessonAttendance === 'attended'
      ? 'text-emerald-600'
      : lead.firstLessonAttendance === 'absent'
        ? 'text-rose-600'
        : 'text-slate-400'

  // Lid yoshi/holatiga qarab rang (yashil = aylantirilgan, qizil = uzoq qolib ketgan).
  const aging = leadAging(lead)
  const ageTitle = aging.converted
    ? 'O\'quvchiga aylantirilgan'
    : aging.days != null
      ? `Lidlar bo'limida ${ageLabel(aging.days)} (${lead.createdAt ? formatDate(lead.createdAt) : '—'})`
      : undefined

  return (
    <div
      className={cn('lead-card', dragging && 'dragging')}
      style={{
        background: aging.bg,
        borderColor: aging.accent,
        borderLeftWidth: 3,
        borderLeftStyle: 'solid',
        borderLeftColor: aging.accent,
      }}
      title={ageTitle}
    >
      <div className="lead-top">
        <div className="flex min-w-0 items-center gap-2">
          <span
            className="avatar h-7 w-7 shrink-0 text-[11px]"
            style={{ background: avatarColor(lead.fullName) }}
          >
            {initials(lead.fullName)}
          </span>
          <div className="min-w-0">
            <div className="lead-name truncate">{lead.fullName}</div>
            <div className="lead-meta truncate">{meta}</div>
          </div>
        </div>
      </div>

      <div className="lead-tags">
        <Badge tone="violet">{genderLabels[lead.gender]}</Badge>
        {lead.source && <Badge tone="blue">{lead.source}</Badge>}
        {lead.convertedStudentId && <Badge tone="green">Aylantirilgan</Badge>}
        {!aging.converted && aging.days != null && (
          <div
            className={cn(
              'inline-flex items-center gap-1 rounded-full px-2 py-1 text-[10px] font-medium',
              aging.chipBg,
              aging.chipText,
            )}
          >
            <Clock className="h-3 w-3" /> {ageLabel(aging.days)}
          </div>
        )}
        {attendanceText && (
          <div
            className={cn(
              'inline-flex items-center gap-1 rounded-full px-2 py-1 text-[10px] font-medium',
              attendanceColor,
              attendanceTextColor,
            )}
          >
            {attendanceText}
          </div>
        )}
        {/* TAKRORIY MUROJAAT — odam ommaviy forma yoki daraja testi orqali YANA yozilgan.
            Bunda lidning bosqichi ATAYIN o'zgarmaydi (birinchi teginish saqlanadi), shuning
            uchun "yo'qotilgan" ustunidagi karta ham shu belgi bilan ko'zga tashlanadi —
            aks holda qayta murojaat faqat izohda qolib ketardi. */}
        {!!lead.repeatCount && lead.repeatCount > 0 && (
          <div
            title={
              lead.lastRepeatAt
                ? `Yana murojaat qildi: ${formatDateTime(lead.lastRepeatAt)}`
                : 'Yana murojaat qildi'
            }
            className="inline-flex items-center gap-1 rounded-full bg-fuchsia-50 px-2 py-1 text-[10px] font-semibold text-fuchsia-700"
          >
            <Repeat2 className="h-3 w-3" /> Takroriy
            {lead.repeatCount > 1 && ` ×${lead.repeatCount}`}
          </div>
        )}
      </div>

      <div className="lead-foot">
        {onCall && phone ? (
          <button
            type="button"
            title="Qo'ng'iroq qilish"
            onClick={(e) => {
              e.stopPropagation()
              onCall(lead)
            }}
            onPointerDown={(e) => e.stopPropagation()}
            className="flex min-w-0 items-center gap-1.5 rounded-md px-1 -mx-1 text-emerald-600 transition-colors hover:bg-emerald-50"
          >
            <Phone className="h-3 w-3 shrink-0" />
            <span className="truncate font-mono">{phone}</span>
          </button>
        ) : (
          <span className="flex min-w-0 items-center gap-1.5">
            <Phone className="h-3 w-3 shrink-0" />
            <span className="truncate font-mono">{phone || '—'}</span>
          </span>
        )}
        {lead.createdAt && (
          <span className="lead-value" title="Lid qo'shilgan sana">
            {formatDateTime(lead.createdAt)}
          </span>
        )}
      </div>
    </div>
  )
}

/** Sudraladigan (draggable) kartochka */
export function LeadCard({
  lead,
  onClick,
  onCall,
}: {
  lead: Lead
  onClick?: () => void
  /** Telefon raqami bosilganda qo'ng'iroq oynasini ochish */
  onCall?: (lead: Lead) => void
}) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: lead.id,
  })

  const style = transform ? { transform: CSS.Translate.toString(transform) } : undefined

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={onClick}
      className={cn('touch-none', isDragging && 'opacity-40')}
    >
      <LeadCardContent lead={lead} onCall={onCall} />
    </div>
  )
}
