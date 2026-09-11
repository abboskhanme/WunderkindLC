import type { ReactNode } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { Phone, Cake, GraduationCap, Repeat2, UserRound } from 'lucide-react'
import type { Lead } from '@/types'
import { genderLabels } from '@/config/constants'
import { formatDate, cn } from '@/lib/utils'

/** Lid yaratilganidan beri o'tgan kun (createdAt "yyyy-MM-ddTHH:mm:ss"). */
function leadAgeDays(createdAt?: string): number | null {
  if (!createdAt) return null
  const d = new Date(createdAt)
  if (isNaN(d.getTime())) return null
  const days = Math.floor((Date.now() - d.getTime()) / 86_400_000)
  return days < 0 ? 0 : days
}

/**
 * Lidning "yoshi" — qancha uzoq lidlar bo'limida qolib ketgan bo'lsa shuncha qizaradi
 * (yangi → kulrang, qolib ketgan → qizil), aylantirilgan bo'lsa yashil.
 *
 * ⚠️ KO'RINISH QOIDASI: kartaning O'ZI har doim OQ qoladi (LMS dizayn uslubi — taxta
 * tinti ustunda, karta esa toza oq). Yosh FAQAT o'ng yuqoridagi chip bilan beriladi;
 * chap chekkadagi ingichka rangli chiziq esa faqat E'TIBOR KERAK bo'lgan kartalarda
 * (7 kundan oshgan yoki aylantirilgan) chiziladi. Ilgari butun karta bo'yalar edi —
 * o'nlab rangli karta yonma-yon turganda "shoshilinch" belgisi ma'nosini yo'qotardi.
 */
function leadAging(lead: Lead): {
  /** Chap chekka chizig'i (faqat e'tibor kerak bo'lganda) */
  accent: string | null
  chip: string
  days: number | null
  converted: boolean
} {
  const converted = !!lead.convertedStudentId
  const days = leadAgeDays(lead.createdAt)
  if (converted) return { accent: '#10b981', chip: 'bg-emerald-50 text-emerald-600', days, converted }
  if (days == null) return { accent: null, chip: 'bg-slate-100 text-slate-500', days, converted }
  if (days >= 14) return { accent: '#dc2626', chip: 'bg-red-50 text-red-600', days, converted }
  if (days >= 7) return { accent: '#ea580c', chip: 'bg-orange-50 text-orange-600', days, converted }
  if (days >= 3) return { accent: null, chip: 'bg-amber-50 text-amber-700', days, converted }
  return { accent: null, chip: 'bg-slate-100 text-slate-500', days, converted }
}

function ageLabel(days: number): string {
  if (days <= 0) return 'Bugun'
  return `${days} kun`
}

/** Kichik rangli chip (manba, mas'ul, takroriy, davomat) */
function Chip({ className, title, children }: { className: string; title?: string; children: ReactNode }) {
  return (
    <span
      title={title}
      className={cn(
        'inline-flex max-w-full items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-medium',
        className,
      )}
    >
      {children}
    </span>
  )
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
  const parent = lead.fatherFullName || lead.motherFullName || ''
  const aging = leadAging(lead)
  const ageTitle = aging.converted
    ? "O'quvchiga aylantirilgan"
    : aging.days != null
      ? `Lidlar bo'limida ${ageLabel(aging.days)} (${lead.createdAt ? formatDate(lead.createdAt) : '—'})`
      : undefined

  const attendance =
    lead.firstLessonAttendance === 'attended'
      ? { text: '✓ Keldi', cls: 'bg-emerald-50 text-emerald-600' }
      : lead.firstLessonAttendance === 'absent'
        ? { text: '✗ Kelmadi', cls: 'bg-rose-50 text-rose-600' }
        : null

  return (
    <div
      className={cn('lead-card', dragging && 'dragging')}
      style={
        aging.accent
          ? { borderLeft: `3px solid ${aging.accent}` }
          : undefined
      }
    >
      <div className="lead-top">
        <p className="lead-name min-w-0">{lead.fullName}</p>
        <span
          title={ageTitle}
          className={cn(
            'shrink-0 rounded-md px-1.5 py-0.5 text-[11px] font-medium',
            aging.chip,
          )}
        >
          {aging.converted ? "O'quvchi" : aging.days != null ? ageLabel(aging.days) : '—'}
        </span>
      </div>

      <div className="lead-rows">
        <p>
          <GraduationCap className="h-3.5 w-3.5 shrink-0" />
          <span className="truncate">
            {genderLabels[lead.gender]}
            {lead.interestSubject ? ` · ${lead.interestSubject}` : ''}
          </span>
        </p>
        {lead.birthDate && (
          <p>
            <Cake className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate">{formatDate(lead.birthDate)}</span>
          </p>
        )}
        {onCall && phone ? (
          <button
            type="button"
            title="Qo'ng'iroq qilish"
            onClick={(e) => {
              e.stopPropagation()
              onCall(lead)
            }}
            onPointerDown={(e) => e.stopPropagation()}
            className="-mx-1 rounded-md px-1 text-emerald-600 transition-colors hover:bg-emerald-50"
          >
            <Phone className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate">{phone}</span>
          </button>
        ) : (
          <p>
            <Phone className="h-3.5 w-3.5 shrink-0" />
            <span className="truncate">{phone || '—'}</span>
          </p>
        )}
        {parent && <p className="truncate text-slate-400">{parent}</p>}
      </div>

      {/* Qo'shimcha belgilar — FAQAT bor bo'lganda chiziladi. Bo'sh holatda karta LMS
          namunasidagidek toza qoladi (bot orqali kelgan lidlarda ko'pchilik maydon bo'sh). */}
      {(lead.source || attendance || lead.assigneeName || !!lead.repeatCount) && (
        <div className="lead-tags">
          {lead.source && <Chip className="bg-brand-50 text-brand-700">{lead.source}</Chip>}
          {attendance && <Chip className={attendance.cls}>{attendance.text}</Chip>}
          {/* MAS'UL XODIM — bo'sh bo'lsa chip UMUMAN chizilmaydi: "biriktirilmagan" belgisi
              har bir bot lidida takrorlanib, kartani shovqinga to'ldirardi. */}
          {!!lead.assigneeName && (
            <Chip className="bg-indigo-50 text-indigo-700" title={`Mas'ul: ${lead.assigneeName}`}>
              <UserRound className="h-3 w-3 shrink-0" />
              <span className="truncate">{lead.assigneeName}</span>
            </Chip>
          )}
          {/* TAKRORIY MUROJAAT — odam ommaviy forma yoki daraja testi orqali YANA yozilgan.
              Bunda lidning bosqichi ATAYIN o'zgarmaydi (birinchi teginish saqlanadi), shuning
              uchun "yo'qotilgan" ustunidagi karta ham shu belgi bilan ko'zga tashlanadi. */}
          {!!lead.repeatCount && lead.repeatCount > 0 && (
            <Chip className="bg-fuchsia-50 text-fuchsia-700" title="Yana murojaat qildi">
              <Repeat2 className="h-3 w-3 shrink-0" /> Takroriy
              {lead.repeatCount > 1 && ` ×${lead.repeatCount}`}
            </Chip>
          )}
        </div>
      )}

      {lead.note && <p className="lead-note line-clamp-2">{lead.note}</p>}
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
