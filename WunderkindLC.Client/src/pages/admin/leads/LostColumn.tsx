import { Link } from 'react-router-dom'
import { useDroppable } from '@dnd-kit/core'
import { IconChartPie } from '@tabler/icons-react'
import type { Lead } from '@/types'
import { LOST_COLUMN_ID } from '@/api/services/leadLoss'
import { cn } from '@/lib/utils'
import { LeadCard } from './LeadCard'

interface Props {
  leads: Lead[]
  /** Filtr "Yo'qotilganlarni yashirish" tanlangan — ustun turadi, kartalari esa chizilmaydi. */
  hidden: boolean
  /** Hisobot havolasi ko'rsatilsinmi (`leads.stats` ruxsati) */
  showReportLink: boolean
  onCardClick: (lead: Lead) => void
  onCall?: (lead: Lead) => void
}

/**
 * KANBANNING DOIMIY «YO'QOTILGAN» USTUNI — har doim ENG OXIRIDA, foydalanuvchi uni
 * tahrirlay/o'chira/ko'chira olmaydi (bazada bunday `LeadStage` YO'Q — `LeadLoss` izohiga qarang).
 *
 * - Karta SHU YERGA tashlansa — sabab oynasi ochiladi (holat jimgina o'zgarmaydi).
 * - Bu yerdagi karta BOSHQA ustunga tashlansa — lid tiklanadi va o'sha bosqichga o'tadi.
 *
 * Rang — `#e11d48` (rose): kurs analitikasidagi "ketgan" bilan bir xil, kanbandagi foydalanuvchi
 * ranglaridan (`LeadColumn.ruleColors`) ajralib turadi.
 */
export function LostColumn({ leads, hidden, showReportLink, onCardClick, onCall }: Props) {
  const { setNodeRef, isOver } = useDroppable({ id: LOST_COLUMN_ID })

  return (
    <div className="flex w-[350px] shrink-0 flex-col">
      <div className="relative px-2 pb-1.5 text-center">
        <p className="truncate text-[13px] font-bold uppercase text-[#be123c]">Yo'qotilgan</p>
        <p className="text-[11px] text-[#757575]">{leads.length} Lidlar</p>
        {showReportLink && (
          <Link
            to="/admin/hisobotlar/yoqotilgan-lidlar"
            title="Yo'qotilgan lidlar hisoboti"
            aria-label="Yo'qotilgan lidlar hisoboti"
            className="absolute right-0 top-0 rounded-md p-1 text-[#757575] transition-colors hover:bg-[#F0F2F2] hover:text-black"
          >
            <IconChartPie className="h-3.5 w-3.5" />
          </Link>
        )}
      </div>
      <div className="h-0.5 w-full rounded-full bg-[#e11d48]" />

      <div
        ref={setNodeRef}
        className={cn(
          'mt-2 flex min-h-[200px] flex-1 flex-col gap-2 rounded-lg bg-[#F7F0F2] p-1.5 transition-shadow',
          isOver && 'shadow-[inset_0_0_0_2px_rgba(225,29,72,0.4)]',
        )}
      >
        {hidden ? (
          <p className="px-2 py-6 text-center text-[12px] text-[#757575]">
            Yashirilgan (filtr). Kartani shu yerga tashlab yo'qotilgan deb belgilash mumkin.
          </p>
        ) : leads.length === 0 ? (
          <p className="px-2 py-6 text-center text-[12px] text-[#757575]">
            Kartani shu yerga tashlang — sabab so'raladi.
          </p>
        ) : (
          leads.map((lead) => (
            <LeadCard key={lead.id} lead={lead} onClick={() => onCardClick(lead)} onCall={onCall} />
          ))
        )}
      </div>
    </div>
  )
}
