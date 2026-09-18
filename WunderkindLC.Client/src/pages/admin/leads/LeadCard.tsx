import type { ReactNode } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { CSS } from '@dnd-kit/utilities'
import { IconPhone, IconRepeat } from '@tabler/icons-react'
import type { Lead } from '@/types'
import { formatDate, formatDateTime, cn } from '@/lib/utils'

/** Lid yaratilganidan beri o'tgan kun (createdAt "yyyy-MM-ddTHH:mm:ss"). */
function leadAgeDays(createdAt?: string): number | null {
  if (!createdAt) return null
  const d = new Date(createdAt)
  if (isNaN(d.getTime())) return null
  const days = Math.floor((Date.now() - d.getTime()) / 86_400_000)
  return days < 0 ? 0 : days
}

/**
 * Lidning "yoshi" — chap chekkadagi ingichka chiziq FAQAT e'tibor kerak bo'lgan kartada:
 * 7+ kun qolib ketgan (to'q sariq), 14+ kun (qizil). Karta o'zi doim OQ (edutizim uslubi);
 * o'nlab rangli karta yonma-yon turganda "shoshilinch" belgisi ma'nosini yo'qotardi.
 */
function agingAccent(lead: Lead): string | null {
  if (lead.convertedStudentId) return null
  const days = leadAgeDays(lead.createdAt)
  if (days == null) return null
  if (days >= 14) return '#dc2626'
  if (days >= 7) return '#ea580c'
  return null
}

/** "18.09 15:30" — kartadagi qisqa sinov vaqti. */
function shortAt(iso: string): string {
  const m = /^\d{4}-(\d{2})-(\d{2})T(\d{2}:\d{2})/.exec(iso)
  return m ? `${m[2]}.${m[1]} ${m[3]}` : formatDate(iso)
}

/** Bugungi sana "yyyy-MM-dd" (mahalliy). */
function todayIso(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

/**
 * Karta pastidagi o'ng "ishora" (edutizimdagi to'q sariq «Topshiriq yo'q» o'rnida) — bizda lid
 * uchun keyingi qadam SINOV DARSI:
 * - o'quvchiga aylangan → yashil «O'quvchi»;
 * - kutilayotgan sinov bor → «Sinov: 18.09 15:30» (sanasi o'tib ketgan bo'lsa qizil);
 * - yo'q → to'q sariq «Sinov yo'q».
 */
function footHint(lead: Lead): { text: string; color: string } {
  if (lead.convertedStudentId) return { text: "O'quvchi", color: '#00A35C' }
  if (lead.firstLessonAt) {
    const past = lead.firstLessonAt.slice(0, 10) < todayIso()
    return { text: `Sinov: ${shortAt(lead.firstLessonAt)}`, color: past ? '#DE4141' : '#3D68FF' }
  }
  return { text: "Sinov yo'q", color: '#ED6C02' }
}

/** Kichik belgi (takroriy murojaat, birinchi dars davomati). */
function Tag({ className, title, children }: { className: string; title?: string; children: ReactNode }) {
  return (
    <span
      title={title}
      className={cn('inline-flex items-center gap-1 rounded px-1.5 py-px text-[10.5px] font-semibold', className)}
    >
      {children}
    </span>
  )
}

/**
 * Faqat ko'rinish (drag overlay uchun ham ishlatiladi). edutizim kanban kartasi: oq, radius 8,
 * kichik soya; 1-qator "Ism , Kurs" (kurs kulrang), 2-qator mas'ul (moderator), chiziq, pastda
 * sana va o'ngda ishora.
 */
export function LeadCardContent({
  lead,
  dragging,
  onCall,
}: {
  lead: Lead
  dragging?: boolean
  /** Berilsa telefon ikonkasi chiqadi — qo'ng'iroq oynasi (drag overlay'da berilmaydi) */
  onCall?: (lead: Lead) => void
}) {
  const phone = lead.phone || lead.fatherPhone || lead.motherPhone || ''
  const accent = agingAccent(lead)
  const hint = footHint(lead)
  const attendance =
    lead.firstLessonAttendance === 'attended'
      ? { text: '✓ Keldi', cls: 'bg-emerald-50 text-emerald-700' }
      : lead.firstLessonAttendance === 'absent'
        ? { text: '✗ Kelmadi', cls: 'bg-rose-50 text-rose-700' }
        : null

  return (
    <div
      className={cn(
        'cursor-pointer rounded-lg bg-white px-2.5 pb-1.5 pt-2 shadow-[0_1px_3px_rgba(0,0,0,0.12)] transition-shadow hover:shadow-[0_2px_6px_rgba(0,0,0,0.16)]',
        dragging && 'rotate-1 shadow-lg',
      )}
      style={accent ? { borderLeft: `3px solid ${accent}` } : undefined}
    >
      <div className="flex items-start gap-1.5">
        <p className="min-w-0 flex-1 text-[12.5px] font-semibold leading-snug text-black">
          {lead.fullName || '—'}
          {lead.interestSubject && (
            <span className="font-normal text-[#757575]"> , {lead.interestSubject}</span>
          )}
        </p>
        {onCall && phone && (
          <button
            type="button"
            title={`Qo'ng'iroq qilish: ${phone}`}
            onClick={(e) => {
              e.stopPropagation()
              onCall(lead)
            }}
            onPointerDown={(e) => e.stopPropagation()}
            className="-mr-1 -mt-0.5 shrink-0 rounded p-0.5 text-[#9e9e9e] transition-colors hover:bg-emerald-50 hover:text-emerald-600"
          >
            <IconPhone className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
      <p className="mt-0.5 truncate text-[12px] text-[#333]" title={lead.assigneeName ? `Mas'ul: ${lead.assigneeName}` : undefined}>
        {lead.assigneeName || ' '}
      </p>

      {/* TAKRORIY MUROJAAT — odam ommaviy forma yoki daraja testi orqali YANA yozilgan. Bosqich
          ATAYIN o'zgarmaydi (birinchi teginish saqlanadi), shuning uchun "yo'qotilgan" ustunidagi
          karta ham shu belgi bilan ko'zga tashlanadi (`crm-leads.md`). */}
      {(attendance || (!!lead.repeatCount && lead.repeatCount > 0)) && (
        <div className="mt-1 flex flex-wrap gap-1">
          {!!lead.repeatCount && lead.repeatCount > 0 && (
            <Tag
              className="bg-fuchsia-50 text-fuchsia-700"
              title={lead.lastRepeatAt ? `Yana murojaat qildi: ${formatDateTime(lead.lastRepeatAt)}` : 'Yana murojaat qildi'}
            >
              <IconRepeat className="h-3 w-3" /> Takroriy{lead.repeatCount > 1 && ` ×${lead.repeatCount}`}
            </Tag>
          )}
          {attendance && <Tag className={attendance.cls}>{attendance.text}</Tag>}
        </div>
      )}

      <div className="mt-1.5 flex items-center justify-between gap-2 border-t border-[#eeeeee] pt-1">
        <span className="text-[11px] text-[#757575]">{lead.createdAt ? formatDateTime(lead.createdAt) : '—'}</span>
        <span className="inline-flex items-center gap-1 text-[11px] font-medium" style={{ color: hint.color }}>
          {hint.text}
          <span className="h-1.5 w-1.5 rounded-full" style={{ background: hint.color }} />
        </span>
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
  /** Telefon ikonkasi bosilganda qo'ng'iroq oynasini ochish */
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
