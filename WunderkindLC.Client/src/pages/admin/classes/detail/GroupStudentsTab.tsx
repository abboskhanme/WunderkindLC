import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  IconArrowBackUp,
  IconArrowsLeftRight,
  IconCalendarPause,
  IconCircleCheck,
  IconFileExport,
  IconHistory,
  IconInfoCircle,
  IconPlus,
  IconSchool,
  IconSearch,
  IconSnowflake,
  IconTrash,
} from '@tabler/icons-react'
import {
  bulkActivateMembers,
  bulkFreezeMembers,
  bulkMembershipSummary,
  type GroupRosterRow,
} from '@/api/services/classes'
import { apiErrorMessage, cn, exportToCsv, formatDate, formatMoney } from '@/lib/utils'
import type { BackState } from '@/lib/nav'
import { membershipBadge } from '@/lib/groupDisplay'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { AuditHistoryList } from '@/components/audit/AuditHistoryList'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { Switch } from '@/components/ui/list/Switch'
import { MoreMenu, type MoreMenuItem } from '@/components/ui/list/MoreMenu'
import { RowActionBar, type RowAction } from '@/components/ui/list/RowActionBar'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'

export type MemberActionKind =
  | 'activate'
  | 'freeze'
  | 'yearFreeze'
  | 'return'
  | 'remove'
  | 'transfer'
  | 'extension'

type BulkKind = 'freeze' | 'yearFreeze' | 'activate'

/**
 * Guruh sahifasi → "O'quvchilar" tabi (edutizim): asboblar ("O'quvchi qo'shish" … "Arxiv
 * o'quvchilar" kaliti, qidiruv, "⋮"), jadval va qator "⋮" — qator USTIDA suzuvchi ikonkalar
 * paneli. Amallar va ularning ruxsat darvozalari ilgarigi a'zolar ro'yxatidagi bilan AYNAN
 * bir xil: holat o'zgartirish — `classes.list:create`, «Aktiv muzlatish» — faqat superadmin
 * (`year-freeze.md` §3), tarix — `audit` ruxsati.
 *
 * Belgilab OMMAVIY aktivlashtirish/muzlatish — «Talabalarni boshqarish» oynasidagi bilan bir xil
 * endpointlar (`bulk-freeze` / `bulk-activate`) va natija matni (`bulkMembershipSummary`).
 */
export function GroupStudentsTab({
  groupId,
  groupName,
  groupFee,
  roster,
  loading,
  back,
  canManage,
  canYearFreeze,
  canSetBonus,
  canSeeAudit,
  onAdd,
  onMemberAction,
  onChanged,
  menu,
}: {
  groupId: string
  groupName: string
  groupFee: number
  roster: GroupRosterRow[]
  loading: boolean
  back: BackState
  canManage: boolean
  canYearFreeze: boolean
  canSetBonus: boolean
  canSeeAudit: boolean
  onAdd?: () => void
  onMemberAction: (kind: MemberActionKind, m: GroupRosterRow) => void
  onChanged: () => void
  menu: MoreMenuItem[]
}) {
  const navigate = useNavigate()
  const [archiveView, setArchiveView] = useState(false)
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [bulk, setBulk] = useState<BulkKind | null>(null)
  const [busy, setBusy] = useState(false)
  const [historyOf, setHistoryOf] = useState<GroupRosterRow | null>(null)

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    const digits = q.replace(/\D/g, '')
    return roster.filter((m) => {
      if (m.isActive === archiveView) return false
      if (!q) return true
      if (m.fullName.toLowerCase().includes(q)) return true
      return digits.length >= 3 && (m.phone ?? '').replace(/\D/g, '').includes(digits)
    })
  }, [roster, query, archiveView])

  const pg = usePagination(rows)

  // Tanlov ko'rinayotgan ro'yxat bilan KESISHTIRIB olinadi — ro'yxat yangilangach endi yo'q id
  // na sanoqqa, na so'rovga tushadi (a'zolar oynasidagi bilan bir xil qoida).
  const selectable = canManage && !archiveView
  const selectedIds = rows.filter((m) => selected.has(m.studentId)).map((m) => m.studentId)
  const allSelected = rows.length > 0 && selectedIds.length === rows.length
  const headerCb = useRef<HTMLInputElement>(null)
  useEffect(() => {
    if (headerCb.current) headerCb.current.indeterminate = selectedIds.length > 0 && !allSelected
  })

  const toggleAll = () =>
    setSelected(allSelected ? new Set() : new Set(rows.map((m) => m.studentId)))
  const toggleOne = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const confirmBulk = async (reasonId: string | undefined, date?: string, retentionBonus?: boolean) => {
    if (!bulk || busy) return
    const day = date ?? new Date().toISOString().slice(0, 10)
    setBusy(true)
    try {
      const freezing = bulk === 'freeze' || bulk === 'yearFreeze'
      const res = freezing
        ? await bulkFreezeMembers(groupId, selectedIds, day, reasonId, bulk === 'yearFreeze')
        : await bulkActivateMembers(groupId, selectedIds, day, canSetBonus ? retentionBonus : undefined)
      setBulk(null)
      setSelected(new Set())
      onChanged()
      alert(bulkMembershipSummary(res, freezing ? 'freeze' : 'activate'))
    } catch (err) {
      alert(apiErrorMessage(err, 'Ommaviy amal bajarilmadi'))
    } finally {
      setBusy(false)
    }
  }

  const exportCsv = () => {
    exportToCsv(
      `${groupName || 'guruh'}-oquvchilar${archiveView ? '-arxiv' : ''}.csv`,
      ['№', "O'quvchi", "Qo'shilgan sana", 'Telefon', 'Balans', 'Narxi', 'Holati', 'Oxirgi izoh'],
      rows.map((m, i) => [
        String(i + 1),
        m.fullName,
        formatDate(m.joinedAt),
        m.phone ?? '',
        String(m.balance),
        String(m.price ?? groupFee),
        membershipBadge(m.status, m.yearFreeze, m.isActive).label,
        m.lastNote ?? '',
      ]),
    )
  }

  const actionsFor = (m: GroupRosterRow): RowAction[] => {
    const manage = canManage && m.isActive
    return [
      {
        label: 'Aktivlashtirish',
        icon: IconCircleCheck,
        hidden: !manage || m.status === 'active',
        onClick: () => onMemberAction('activate', m),
      },
      {
        label: "O'quvchini guruhdan muzlatish",
        icon: IconSnowflake,
        hidden: !manage || m.status !== 'active',
        onClick: () => onMemberAction('freeze', m),
      },
      {
        label: 'Aktiv muzlatish (yangi o\'quv yili)',
        icon: IconCalendarPause,
        hidden: !manage || m.status !== 'active' || !canYearFreeze,
        onClick: () => onMemberAction('yearFreeze', m),
      },
      {
        label: "Ma'lumot",
        icon: IconInfoCircle,
        onClick: () => navigate(`/admin/students/${m.studentId}`, { state: back }),
      },
      {
        label: 'Transfer',
        icon: IconArrowsLeftRight,
        hidden: !manage,
        onClick: () => onMemberAction('transfer', m),
      },
      {
        // Uzaytirish — a'zolikni KO'CHIRMAYDI, faqat hodisani yozadi (`kpi.md` §1).
        label: 'Uzaytirish qaydi (keyingi bosqich)',
        icon: IconSchool,
        hidden: !manage,
        onClick: () => onMemberAction('extension', m),
      },
      {
        label: "O'quvchini tarixi",
        icon: IconHistory,
        hidden: !canSeeAudit,
        onClick: () => setHistoryOf(m),
      },
      {
        label: 'Sinov darsiga qaytarish',
        icon: IconArrowBackUp,
        hidden: !manage || m.status === 'trial',
        onClick: () => onMemberAction('return', m),
      },
      {
        label: 'Guruhdan chiqarish',
        icon: IconTrash,
        danger: true,
        hidden: !manage,
        onClick: () => onMemberAction('remove', m),
      },
    ]
  }

  const columns: DataColumn<GroupRosterRow>[] = [
    ...(selectable
      ? [
          {
            key: '__cb',
            width: 44,
            header: (
              <input
                ref={headerCb}
                type="checkbox"
                aria-label="Hammasini tanlash"
                checked={allSelected}
                onChange={toggleAll}
                className="h-4 w-4 cursor-pointer accent-[#3d68ff]"
              />
            ),
            render: (m: GroupRosterRow) => (
              <input
                type="checkbox"
                aria-label={`${m.fullName} — tanlash`}
                checked={selected.has(m.studentId)}
                onChange={() => toggleOne(m.studentId)}
                className="h-4 w-4 cursor-pointer accent-[#3d68ff]"
              />
            ),
          },
        ]
      : []),
    {
      key: 'name',
      header: "O'quvchini ismi",
      width: 230,
      render: (m) => (
        <div className="flex flex-col items-start gap-0.5 py-1">
          <Link
            to={`/admin/students/${m.studentId}`}
            state={back}
            className="text-black no-underline hover:text-brand-600 hover:underline"
          >
            {m.fullName}
          </Link>
          {m.courseCount >= 2 && (
            <span className="rounded-full bg-emerald-50 px-1.5 py-0.5 text-[10px] font-semibold text-emerald-700">
              {m.courseCount} ta Kurs
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'joined',
      header: "Qo'shilgan sanasi",
      render: (m) => <span className="whitespace-nowrap">{formatDate(m.joinedAt) || '—'}</span>,
    },
    { key: 'phone', header: 'Telefon raqam', render: (m) => <PhoneChip phone={m.phone} /> },
    {
      key: 'balance',
      header: 'Balans',
      render: (m) => (
        <span className={cn('whitespace-nowrap', m.balance < 0 && 'text-[#d32f2f]')}>{formatMoney(m.balance)}</span>
      ),
    },
    {
      key: 'price',
      header: 'Narxi',
      render: (m) => <span className="whitespace-nowrap">{formatMoney(m.price ?? groupFee)}</span>,
    },
    {
      key: 'note',
      header: 'Oxirgi izoh',
      render: (m) =>
        m.lastNote ? (
          <span className="block max-w-[220px] truncate" title={`${m.lastNote}${m.lastNoteAt ? ` (${formatDate(m.lastNoteAt)})` : ''}`}>
            {m.lastNote}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'status',
      header: 'Holati',
      render: (m) => {
        const b = membershipBadge(m.status, m.yearFreeze, m.isActive)
        return (
          <span className="flex flex-col items-start gap-0.5">
            <span className={cn('whitespace-nowrap rounded-full px-2 py-0.5 text-[11px] font-semibold', b.cls)}>{b.label}</span>
            {!m.isActive && m.leftAt && <span className="text-[11px] text-[#6b7280]">{formatDate(m.leftAt)}</span>}
          </span>
        )
      },
    },
    {
      key: 'actions',
      header: '',
      width: 48,
      align: 'center',
      render: (m) => <RowActionBar actions={actionsFor(m)} />,
    },
  ]

  return (
    <div>
      <div className="mb-2.5 flex flex-wrap items-center gap-2">
        {onAdd && !archiveView && (
          <Button onClick={onAdd}>
            <IconPlus className="h-5 w-5" /> O'quvchi qo'shish
          </Button>
        )}
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Switch
            label="Arxiv o'quvchilar"
            checked={archiveView}
            onChange={(v) => {
              setArchiveView(v)
              setSelected(new Set())
            }}
          />
          <div className="relative">
            <IconSearch className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Qidirish"
              className="h-[34px] w-56 rounded-lg border border-black/25 bg-white pl-8 pr-3 text-[13px] text-black outline-none placeholder:text-[#6b7280] hover:border-black/60 focus:border-brand-600 focus:ring-1 focus:ring-brand-600"
            />
          </div>
          <MoreMenu items={[...menu, { label: 'Export (CSV)', icon: IconFileExport, onClick: exportCsv }]} />
        </div>
      </div>

      {selectable && selectedIds.length > 0 && (
        <div className="mb-2 flex flex-wrap items-center gap-2 rounded-xl border border-brand-600/20 bg-brand-600/[0.06] px-3 py-2 text-[13px]">
          <b className="text-brand-700">{selectedIds.length} ta tanlandi</b>
          <Button variant="secondary" disabled={busy} onClick={() => setBulk('activate')}>
            <IconCircleCheck className="h-4 w-4" /> Aktivlashtirish
          </Button>
          <Button variant="secondary" disabled={busy} onClick={() => setBulk('freeze')}>
            <IconSnowflake className="h-4 w-4" /> Muzlatish
          </Button>
          {canYearFreeze && (
            <Button variant="secondary" disabled={busy} onClick={() => setBulk('yearFreeze')}>
              <IconCalendarPause className="h-4 w-4" /> Aktiv muzlatish
            </Button>
          )}
          <Button variant="ghost" onClick={() => setSelected(new Set())}>
            Bekor qilish
          </Button>
        </div>
      )}

      <TotalPill total={rows.length} />
      <DataTable
        rows={pg.paged}
        columns={columns}
        rowKey={(m) => m.studentId}
        loading={loading}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        rowClassName={(m) => (selected.has(m.studentId) ? '!bg-brand-600/[0.06]' : undefined)}
        footer={<TablePagination {...pg} />}
      />

      {/* Ommaviy muzlatish/aktivlashtirish — sabab va sana BIR marta tanlanadi. */}
      <ReasonPromptModal
        open={!!bulk}
        category={bulk === 'activate' ? 'activate' : 'freeze'}
        title={
          bulk === 'freeze' ? 'Ommaviy muzlatish' : bulk === 'yearFreeze' ? 'Ommaviy aktiv muzlatish' : 'Ommaviy aktivlashtirish'
        }
        message={
          bulk === 'freeze'
            ? `${selectedIds.length} ta o'quvchi — shu sanadan boshlab oylik to'lov hisoblanmaydi.`
            : bulk === 'yearFreeze'
              ? `${selectedIds.length} ta o'quvchi shu sanadan muzlatiladi va «yangi o'quv yiliga o'tish» deb belgilanadi. Hisob-kitob oddiy muzlatish bilan bir xil.`
              : `${selectedIds.length} ta o'quvchi — shu sanadan boshlab qisman oylik hisoblanadi.`
        }
        confirmLabel={bulk === 'freeze' ? 'Muzlatish' : bulk === 'yearFreeze' ? 'Aktiv muzlatish' : 'Aktivlashtirish'}
        tone={bulk === 'activate' ? 'brand' : 'sky'}
        showDate
        showRetentionBonus={bulk === 'activate' && canSetBonus}
        onConfirm={confirmBulk}
        onClose={() => setBulk(null)}
      />

      {/* O'quvchining SHU GURUHDAGI a'zolik tarixi (audit: EntityId = "{guruh}:{o'quvchi}"). */}
      <Modal
        open={!!historyOf}
        onClose={() => setHistoryOf(null)}
        size="lg"
        title={historyOf ? `${historyOf.fullName} — guruhdagi tarixi` : 'Tarix'}
      >
        {historyOf && (
          <AuditHistoryList
            filters={{ entityType: 'Membership', entityId: `${groupId}:${historyOf.studentId}` }}
            emptyLabel="Bu o'quvchi bo'yicha tarix yo'q"
          />
        )}
      </Modal>
    </div>
  )
}

