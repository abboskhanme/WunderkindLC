import type { ComponentType, ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  IconArchive,
  IconArchiveOff,
  IconBook,
  IconBuilding,
  IconCalendarEvent,
  IconCalendarTime,
  IconChartLine,
  IconClock,
  IconHourglass,
  IconLanguage,
  IconLayoutGrid,
  IconLayoutSidebarLeftCollapse,
  IconPencil,
  IconUserCircle,
  IconUsers,
  IconWallet,
  IconCalendarDue,
} from '@tabler/icons-react'
import type { Group } from '@/types'
import type { GroupJournalInfo } from '@/api/services/journal'
import { languageLabels } from '@/config/constants'
import { formatDate, formatMoney } from '@/lib/utils'
import { WEEKDAYS, formatLessonTime, lessonDurationLabel } from '@/lib/groupDisplay'
import { MoreMenu, type MoreMenuItem } from '@/components/ui/list/MoreMenu'
import { uzMonths } from './shared'

type IconType = ComponentType<{ className?: string }>

/** "2026-09-02" → "2-Sentabr 2026" (edutizimdagi "2-September 2026" ning o'zbekchasi). */
function longDate(iso?: string | null): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '')
  if (!m) return '—'
  return `${Number(m[3])}-${uzMonths[Number(m[2]) - 1] ?? m[2]} ${m[1]}`
}

function Row({ icon: Icon, label, children }: { icon: IconType; label: string; children: ReactNode }) {
  return (
    <div className="flex min-h-[38px] items-center justify-between gap-3 py-1">
      <span className="inline-flex shrink-0 items-center gap-2 text-[14px] text-[#6b7280]">
        <Icon className="h-5 w-5 text-[#6b7280]" />
        {label}
      </span>
      <span className="min-w-0 text-right text-[13px] font-semibold text-black">{children}</span>
    </div>
  )
}

function Section({ icon: Icon, title }: { icon: IconType; title: string }) {
  return (
    <div className="mt-1 flex items-center gap-2 border-t border-[#e0e0e0] pb-1 pt-3 text-[15px] font-semibold text-black">
      <Icon className="h-5 w-5" />
      {title}
    </div>
  )
}

const chip = 'inline-flex items-center rounded-full bg-[#ebebeb] px-2 py-0.5 text-[11px] font-medium text-black'

/**
 * Guruh sahifasining CHAP kartasi — edutizimdagi "Guruh ma'lumotlari" (~350px): ikonka + kulrang
 * yorliq chapda, qalin qiymat o'ngda; bo'limlar "Dars jadvali", "Akademik ma'lumot", "Guruh
 * faoliyat muddati". Pastda "Guruhni arxivlash" (qizil) va "Tahrirlash" (ko'k).
 *
 * Qolgan guruh amallari (SMS, talabalarni boshqarish, sertifikat bilan tugatish, guruhni yopish)
 * sarlavhadagi "⋮" da — ular edutizimda yo'q, bizda bor.
 */
export function GroupInfoCard({
  g,
  group,
  teacherId,
  activeMembers,
  menu,
  onCollapse,
  onEdit,
  onArchive,
  onUnarchive,
}: {
  g: GroupJournalInfo
  group: Group | null
  teacherId: string
  /** Guruhdagi joriy a'zolar soni (faol + sinov + muzlatilgan). */
  activeMembers: number
  menu: MoreMenuItem[]
  onCollapse: () => void
  onEdit?: () => void
  onArchive?: () => void
  onUnarchive?: () => void
}) {
  const days = [...(g.days ?? [])].sort((a, b) => a - b)
  const duration = lessonDurationLabel(g.startTime, g.endTime)
  const endIso = group?.endDate || (group?.isArchived ? group.archivedAt : '') || ''

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 text-[16px] font-semibold text-black">
          <IconLayoutGrid className="h-5 w-5" /> Guruh ma'lumotlari
        </h2>
        <div className="flex items-center gap-1">
          <MoreMenu items={menu} label="Guruh amallari" />
          <button
            type="button"
            onClick={onCollapse}
            title="Panelni yig'ish"
            aria-label="Panelni yig'ish"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-brand-600 transition-colors hover:bg-brand-600/10"
          >
            <IconLayoutSidebarLeftCollapse className="h-5 w-5" />
          </button>
        </div>
      </div>

      {/* Holat belgilari — tugatilgan (arxiv) yoki vaqtincha bloklangan guruh. */}
      {(group?.isArchived || group?.isBlocked) && (
        <div className="mb-2 flex flex-wrap gap-1.5">
          {group?.isArchived && (
            <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-semibold text-amber-700">
              Tugatilgan{group.archivedAt ? ` · ${formatDate(group.archivedAt)}` : ''}
            </span>
          )}
          {group?.isBlocked && (
            <span
              className="rounded-full bg-orange-100 px-2 py-0.5 text-[11px] font-semibold text-orange-700"
              title={group.blockNote || undefined}
            >
              Bloklangan — o'qituvchida ko'rinmaydi{group.blockedAt ? ` · ${formatDate(group.blockedAt)}` : ''}
            </span>
          )}
        </div>
      )}

      <Row icon={IconUsers} label="Guruh nomi">
        <span className="break-words">{g.name}</span>
      </Row>
      <Row icon={IconChartLine} label="Daraja">
        {group && group.grade > 0 ? group.grade : '—'}
      </Row>
      <Row icon={IconLanguage} label="Til">
        {group ? (
          <span className="inline-flex rounded-full bg-[#2e7d32] px-2 py-0.5 text-[11px] font-semibold text-white">
            {languageLabels[group.language] ?? group.language}
          </span>
        ) : (
          '—'
        )}
      </Row>
      <Row icon={IconBuilding} label="Xona">
        {g.room || '—'}
      </Row>
      <Row icon={IconUserCircle} label="O'quvchilar">
        {activeMembers}
        {group && (group.capacity ?? 0) > 0 ? ` / ${group.capacity}` : ''}
      </Row>

      <Section icon={IconCalendarEvent} title="Dars jadvali" />
      <Row icon={IconClock} label="Dars vaqti">
        {g.startTime || g.endTime ? <span className={chip}>{formatLessonTime(g.startTime, g.endTime)}</span> : '—'}
      </Row>
      <Row icon={IconCalendarTime} label="Dars kunlari">
        {days.length ? (
          <span className="flex flex-wrap justify-end gap-1">
            {days.map((d) => (
              <span key={d} className={chip}>
                {WEEKDAYS[d] ?? d}
              </span>
            ))}
          </span>
        ) : (
          '—'
        )}
      </Row>
      <Row icon={IconHourglass} label="Dars davomiyligi">
        {duration || '—'}
      </Row>

      <Section icon={IconBook} title="Akademik ma'lumot" />
      <Row icon={IconBook} label="Kurs / Fan">
        {g.courseName || '—'}
      </Row>
      <Row icon={IconUserCircle} label="O'qituvchi">
        {g.teacherName && teacherId ? (
          <Link to={`/admin/teachers/${teacherId}`} className="text-black hover:text-brand-600 hover:underline">
            {g.teacherName}
          </Link>
        ) : (
          g.teacherName || '—'
        )}
      </Row>
      <Row icon={IconWallet} label="Oylik to'lov">
        {formatMoney(g.monthlyFee)}
      </Row>

      <Section icon={IconCalendarDue} title="Guruh faoliyat muddati" />
      <Row icon={IconCalendarEvent} label="Boshlanish sanasi">
        <span className="text-[14px] font-bold">{longDate(g.startDate || group?.startDate)}</span>
      </Row>
      <Row icon={IconCalendarEvent} label="Tugash kuni">
        <span className="text-[14px] font-bold">{longDate(endIso)}</span>
      </Row>

      {(onArchive || onUnarchive || onEdit) && (
        <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
          {onArchive && (
            <button
              type="button"
              onClick={onArchive}
              className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-[#e34a29] px-3 text-[12px] font-medium text-white transition-colors hover:bg-[#cc3f20]"
            >
              <IconArchive className="h-5 w-5" /> Guruhni arxivlash
            </button>
          )}
          {onUnarchive && (
            <button
              type="button"
              onClick={onUnarchive}
              className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg border border-brand-600/50 bg-white px-3 text-[12px] font-medium text-brand-600 transition-colors hover:bg-brand-50"
            >
              <IconArchiveOff className="h-5 w-5" /> Arxivdan chiqarish
            </button>
          )}
          {onEdit && (
            <button
              type="button"
              onClick={onEdit}
              className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 text-[12px] font-medium text-white transition-colors hover:bg-brand-700"
            >
              <IconPencil className="h-5 w-5" /> Tahrirlash
            </button>
          )}
        </div>
      )}
    </div>
  )
}
