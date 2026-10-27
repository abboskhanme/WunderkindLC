import { useState, type ReactNode } from 'react'
import { Link, Navigate, useParams, useSearchParams } from 'react-router-dom'
import { IconChevronRight, IconExternalLink, IconLock } from '@tabler/icons-react'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import {
  visibleSettingsGroups,
  type SettingsItem,
  type SettingsModalKey,
  type SettingsPanelKey,
  type SettingsRow,
} from '@/config/settingsGroups'
import { JournalPolicyModal, type JournalPolicySection } from '@/pages/admin/classes/JournalPolicyModal'
import { RetentionSettingsModal } from '@/pages/admin/students/RetentionSettingsModal'
import { RatingSettingsModal } from '@/components/rating/RatingSettingsModal'
import { AttendanceDisciplineModal } from './AttendanceDisciplineModal'
import { AbsentNoticeSettingsModal } from './AbsentNoticeSettingsModal'
import { ReasonsPage } from '@/pages/admin/reasons/ReasonsPage'
import { AccountSettings } from '@/pages/admin/account/AccountSettings'
import { SchoolSettings } from './SchoolSettings'
import { CheckSettings } from './CheckSettings'
import { ChannelsSettings } from './ChannelsSettings'
import { AzureSpeechSettings } from './AzureSpeechSettings'
import { GeminiSettings } from './GeminiSettings'
import { McpSettings } from './McpSettings'
import { ContactServiceSettings } from './ContactServiceSettings'
import { MyMcpConnections } from '@/components/settings/MyMcpConnections'
import { useAuth } from '@/context/auth-context'

/**
 * SOZLAMALAR — edutizim ko'rinishida: chapda oq kartada bo'limlar ro'yxati (ikonka + nom,
 * tanlangani ko'k), o'ngda kontent. "Funksionallik" uslubidagi bo'limlar kulrang qatorlar
 * (rangli ikonka + sarlavha + izoh + strelka) bo'lib chiqadi.
 *
 * Tuzilma `config/settingsGroups.ts` da (menyu bilan YAGONA manba). Tanlangan bo'lim —
 * `?bolim=`.
 *
 * Marshrutda `RequirePerm` YO'Q — sahifa har bandni o'zi ruxsat bo'yicha filtrlaydi
 * (hisobotlar hub'idagi naqsh): guruhda ko'rinadigan band qolmasa buni ochiq aytadi.
 */
export function SettingsGroupPage() {
  const { group = 'umumiy' } = useParams()
  const [params, setParams] = useSearchParams()
  const { can } = usePerm()
  const { user } = useAuth()
  const [modal, setModal] = useState<SettingsModalKey | null>(null)

  const groups = visibleSettingsGroups((p, a) => can(p, a), user?.role === 'superadmin')
  const current = groups.find((g) => g.key === group)

  if (!current) {
    // Guruh yo'q yoki ruxsat yo'q: ko'rinadigan birinchi guruhga (bo'lmasa — xabar).
    if (groups.length > 0 && !['umumiy', 'moliya', 'oquv', 'sotuv', 'ilova'].includes(group))
      return <Navigate to={groups[0].to} replace />
    return (
      <div className="flex min-h-[300px] flex-col items-center justify-center rounded-xl bg-white p-8 text-center">
        <IconLock className="mb-2 h-8 w-8 text-slate-300" />
        <p className="text-[15px] font-semibold text-[#333]">Bu bo'limga ruxsatingiz yo'q</p>
        <p className="mt-1 text-sm text-slate-500">Kerak bo'lsa administratorga murojaat qiling.</p>
      </div>
    )
  }

  const wanted = params.get('bolim')
  const item = current.items.find((i) => i.key === wanted && i.kind !== 'link')
    ?? current.items.find((i) => i.kind !== 'link')
    ?? current.items[0]

  const select = (key: string) => {
    const p = new URLSearchParams()
    p.set('bolim', key)
    setParams(p, { replace: true })
  }

  return (
    <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:gap-4">
      {/* ── Chap: bo'limlar kartasi ── */}
      {/* edutizimdagidek ikkala karta kompyuterda ekran pastigacha cho'ziladi. */}
      <nav className="shrink-0 overflow-hidden rounded-xl bg-white lg:min-h-[calc(100dvh-96px)] lg:w-[386px]" aria-label={current.label}>
        {current.items.map((i, idx) => {
          const Icon = i.icon
          const active = i.key === item.key
          const cls = cn(
            'flex h-[49px] w-full items-center gap-3.5 px-5 text-left text-[16px] font-semibold transition-colors',
            idx > 0 && 'border-t border-[#eceff2]',
            active ? 'text-brand-600' : 'text-[#333] hover:bg-[#f6f7f9] hover:text-brand-600',
          )
          const body = (
            <>
              <Icon className={cn('h-5 w-5 shrink-0', active ? 'text-brand-600' : 'text-[#5b6b8c]')} stroke={1.8} />
              <span className="min-w-0 flex-1 truncate">{i.label}</span>
              {i.kind === 'link' && <IconExternalLink className="h-4 w-4 shrink-0 text-slate-400" />}
            </>
          )
          return i.kind === 'link' ? (
            <Link key={i.key} to={i.to!} className={cls} title="Alohida sahifada ochiladi">
              {body}
            </Link>
          ) : (
            <button key={i.key} type="button" onClick={() => select(i.key)} className={cls} aria-current={active ? 'page' : undefined}>
              {body}
            </button>
          )
        })}
      </nav>

      {/* ── O'ng: kontent ── */}
      <div className="min-w-0 flex-1">
        <ItemContent item={item} onOpenModal={setModal} />
      </div>

      <SettingsModals modal={modal} onClose={() => setModal(null)} />
    </div>
  )
}

function ItemContent({
  item,
  onOpenModal,
}: {
  item: SettingsItem
  onOpenModal: (m: SettingsModalKey) => void
}) {
  if (item.kind === 'rows')
    return (
      <div className="space-y-1.5 rounded-xl bg-white p-2.5 lg:min-h-[calc(100dvh-96px)]">
        {(item.rows ?? []).map((r) => (
          <SettingsRowButton key={r.key} row={r} onOpenModal={onOpenModal} />
        ))}
      </div>
    )
  if (item.kind === 'panel' && item.panel) return <Panel panel={item.panel} />
  return null
}

/** edutizim "Funksionallik" qatori: kulrang #f0f2f2, 44px rangli ikonka, sarlavha + izoh, strelka. */
function SettingsRowButton({ row, onOpenModal }: { row: SettingsRow; onOpenModal: (m: SettingsModalKey) => void }) {
  const Icon = row.icon
  const body = (
    <>
      <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-[10px] text-white" style={{ background: row.color }}>
        <Icon className="h-6 w-6" stroke={1.8} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="block truncate text-[15px] font-semibold text-black">{row.title}</span>
        <span className="line-clamp-2 block text-[13px] text-[#8a8f98] sm:truncate">{row.sub}</span>
      </span>
      <IconChevronRight className="h-5 w-5 shrink-0 text-[#333]" />
    </>
  )
  const cls =
    'flex w-full items-center gap-3.5 rounded-lg bg-[#f0f2f2] px-2 py-2 text-left transition-colors hover:bg-[#e6eaea]'
  return 'to' in row.open ? (
    <Link to={row.open.to} className={cls}>
      {body}
    </Link>
  ) : (
    <button type="button" className={cls} onClick={() => onOpenModal((row.open as { modal: SettingsModalKey }).modal)}>
      {body}
    </button>
  )
}

/** Tayyor sozlama komponentlari (o'z kartasi bilan chiziladi). */
function Panel({ panel }: { panel: SettingsPanelKey }): ReactNode {
  switch (panel) {
    case 'check':
      return <CheckSettings />
    case 'school':
      return <SchoolSettings />
    case 'account':
      return (
        <>
          <div className="rounded-xl bg-white p-4">
            <AccountSettings />
          </div>
          {/* O'z AI ulanishlari (read-only MCP) — faqat ulana oladigan yoki ulanishi borga ko'rinadi. */}
          <MyMcpConnections />
        </>
      )
    case 'reasons':
      return <ReasonsPage embedded />
    case 'channels':
      return <ChannelsSettings />
    case 'azure':
      return <AzureSpeechSettings />
    case 'gemini':
      return <GeminiSettings />
    case 'mcp':
      return <McpSettings />
    case 'servis':
      return <ContactServiceSettings />
  }
}

const JOURNAL_MODALS: Partial<Record<SettingsModalKey, { sections: JournalPolicySection[]; title: string }>> = {
  'journal-edit': { sections: ['edit'], title: 'Davomat sozlamalari' },
  'journal-payment': { sections: ['payment'], title: 'Moliya sozlamalari' },
  'journal-salary': { sections: ['salary'], title: "O'qituvchi holatlari" },
}

function SettingsModals({ modal, onClose }: { modal: SettingsModalKey | null; onClose: () => void }) {
  if (modal === 'retention') return <RetentionSettingsModal onClose={onClose} onSaved={onClose} />
  if (modal === 'rating') return <RatingSettingsModal onClose={onClose} />
  if (modal === 'attendance-discipline') return <AttendanceDisciplineModal open onClose={onClose} />
  if (modal === 'absent-notice') return <AbsentNoticeSettingsModal open onClose={onClose} />
  const journal = modal ? JOURNAL_MODALS[modal] : undefined
  return (
    <JournalPolicyModal
      open={!!journal}
      onClose={onClose}
      sections={journal?.sections}
      title={journal?.title}
    />
  )
}
