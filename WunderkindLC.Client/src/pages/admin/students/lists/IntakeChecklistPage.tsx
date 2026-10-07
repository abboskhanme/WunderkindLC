import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { CheckCircle2, Circle, UserCheck } from 'lucide-react'
import {
  getStudentIntakeList,
  type IntakeItem,
  type IntakeItemKey,
  type StudentIntake,
} from '@/api/services/studentIntake'
import { DataTable } from '@/components/ui/list/DataTable'
import { FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { PageHeader } from '@/components/ui/PageHeader'
import { Loader } from '@/components/ui/Loader'
import { IntakeMarkModal } from '@/components/students/IntakeMarkModal'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import {
  INTAKE_COLUMNS,
  INTAKE_DAY_OPTIONS,
  INTAKE_DEFAULT_DAYS,
  itemState,
  matchesIntake,
  pendingLabels,
  progressTone,
  type IntakeStatusFilter,
} from '@/lib/intake'
import { cn, formatDate } from '@/lib/utils'

const chipCls = (active: boolean) =>
  cn(
    'rounded-full border px-3 py-1 text-[12px] font-semibold transition-colors',
    active ? 'border-brand-600 bg-brand-600 text-white' : 'border-[#dbe0e6] bg-white text-[#333] hover:border-brand-300',
  )

const barCls = { complete: 'bg-emerald-500', partial: 'bg-amber-400', low: 'bg-rose-400' } as const

/** Jadval katagi: band holati belgisi; ruxsat bo'lsa (va avtomatik bo'lmasa) bosiladi → qo'lda belgi. */
function ItemCell({ item, onMark }: { item: IntakeItem | undefined; onMark?: () => void }) {
  if (!item) return <span className="text-[#9ca3af]">—</span>
  const s = itemState(item)
  const icon =
    s === 'auto' ? (
      <CheckCircle2 className="h-[18px] w-[18px] text-emerald-600" />
    ) : s === 'manual' ? (
      <UserCheck className="h-[18px] w-[18px] text-indigo-600" />
    ) : (
      <Circle className="h-[18px] w-[18px] text-slate-300" />
    )
  const title =
    `${item.label}: ${item.detail}` +
    (item.mark ? ` — qo'lda: ${item.mark.markedBy}${item.mark.note ? ` («${item.mark.note}»)` : ''}` : '')
  if (onMark && !item.auto)
    return (
      <button type="button" onClick={onMark} title={title} className="inline-flex rounded-md p-1 hover:bg-slate-100">
        {icon}
      </button>
    )
  return (
    <span title={title} className="inline-flex p-1">
      {icon}
    </span>
  )
}

/**
 * O'quvchilar → «Qabul cheklisti» (Future): yangi o'quvchilarni qabul qilish nazorati —
 * kimda ma'lumot, daraja testi, shartnoma, Telegram yoki to'lov hali yo'q.
 *
 * <p>Qamrov SERVERDA (`StudentIntake.ListAsync`): oxirgi N kunda qabul qilinganlar + barcha
 * sinovdagilar. Filtr va qidiruv KLIENTDA (`lib/intake.ts`) — ro'yxat bir so'rovda keladi.
 * Qo'lda belgidan keyin qator serverdan kelgan YANGI holat bilan almashtiriladi (optimistik emas).</p>
 */
export function IntakeChecklistPage() {
  const { can } = usePerm()
  const canMark = can('students.list', 'edit')

  const [days, setDays] = useState<number>(INTAKE_DEFAULT_DAYS)
  const { data, loading, error } = useAsync(() => getStudentIntakeList(days), [days])
  /** Qo'lda belgidan keyin serverdan qaytgan qatorlar (to'liq ro'yxatni qayta so'ramaslik uchun). */
  const [patched, setPatched] = useState<Record<string, StudentIntake>>({})
  const [status, setStatus] = useState<IntakeStatusFilter>('incomplete')
  const [item, setItem] = useState<IntakeItemKey | ''>('')
  const [q, setQ] = useState('')
  const [mark, setMark] = useState<{ row: StudentIntake; item: IntakeItem } | null>(null)

  const rows = useMemo(() => (data?.items ?? []).map((r) => patched[r.studentId] ?? r), [data, patched])
  const shown = useMemo(() => rows.filter((r) => matchesIntake(r, { status, item, q })), [rows, status, item, q])
  const pg = usePagination(shown)

  const incomplete = rows.filter((r) => !r.complete).length
  // Sanoqlar KO'RINAYOTGAN holatdan (qo'lda belgi qo'yilgach darhol yangilansin).
  const pendingOf = (key: IntakeItemKey) =>
    rows.filter((r) => r.items.some((i) => i.key === key && !i.done)).length

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div>
      <PageHeader
        title="Qabul cheklisti"
        sub={`Yangi o'quvchini qabul qilish: ma'lumotlar, daraja testi, shartnoma, Telegram va to'lov. Oxirgi ${data?.days ?? days} kunda qabul qilinganlar va barcha sinovdagilar.`}
        actions={
          <div className="w-[170px]">
            <FilterSelect
              placeholder="Davr"
              value={String(days)}
              onChange={(e) => {
                setDays(Number(e.target.value) || INTAKE_DEFAULT_DAYS)
                setPatched({})
              }}
            >
              {INTAKE_DAY_OPTIONS.map((d) => (
                <option key={d} value={d}>
                  Oxirgi {d} kun
                </option>
              ))}
            </FilterSelect>
          </div>
        }
      />

      <div className="mb-2.5 flex flex-wrap items-center gap-2">
        <button type="button" className={chipCls(status === 'incomplete')} onClick={() => setStatus('incomplete')}>
          To'liq emas · {incomplete}
        </button>
        <button type="button" className={chipCls(status === 'complete')} onClick={() => setStatus('complete')}>
          To'liq · {rows.length - incomplete}
        </button>
        <button type="button" className={chipCls(status === 'all')} onClick={() => setStatus('all')}>
          Hammasi · {rows.length}
        </button>
        <span className="mx-1 h-5 w-px bg-[#dbe0e6]" />
        {INTAKE_COLUMNS.map((c) => (
          <button
            key={c.key}
            type="button"
            className={chipCls(item === c.key)}
            title="Shu band bajarilmaganlar"
            onClick={() => setItem(item === c.key ? '' : c.key)}
          >
            {c.short} yo'q · {pendingOf(c.key)}
          </button>
        ))}
      </div>

      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <div className="w-full sm:w-[320px]">
          <FilterInput search placeholder="Ism, guruh yoki telefon" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
        <p className="flex flex-wrap items-center gap-3 text-[12px] text-[#6b7280]">
          <span className="inline-flex items-center gap-1">
            <CheckCircle2 className="h-4 w-4 text-emerald-600" /> avtomatik
          </span>
          <span className="inline-flex items-center gap-1">
            <UserCheck className="h-4 w-4 text-indigo-600" /> qo'lda belgilangan
          </span>
          <span className="inline-flex items-center gap-1">
            <Circle className="h-4 w-4 text-slate-300" /> bajarilmagan
          </span>
        </p>
      </div>

      {data?.truncated && (
        <p className="mb-2 rounded-lg bg-amber-50 px-3 py-2 text-[12px] text-amber-800">
          Ro'yxat qirqildi: jami {data.total} ta o'quvchidan eng yangi {data.items.length} tasi ko'rsatilmoqda —
          davrni qisqartiring.
        </p>
      )}

      <DataTable<StudentIntake>
        rows={pg.paged}
        rowKey={(r) => r.studentId}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        emptyHint={
          status === 'incomplete' && !item && !q
            ? "Barcha yangi o'quvchilarning qabul cheklisti to'liq."
            : undefined
        }
        footer={<TablePagination {...pg} />}
        columns={[
          {
            key: 'name',
            header: "O'quvchi",
            render: (r) => (
              <Link
                to={`/admin/students/${r.studentId}`}
                className="font-medium text-[#333] hover:text-brand-600 hover:underline"
              >
                {r.fullName}
              </Link>
            ),
          },
          { key: 'phone', header: 'Telefon', render: (r) => <PhoneChip phone={r.phone} /> },
          {
            key: 'group',
            header: 'Guruh',
            render: (r) => (r.groups.length ? r.groups.join(', ') : <span className="text-[#9ca3af]">—</span>),
          },
          {
            key: 'since',
            header: 'Qabul sanasi',
            render: (r) =>
              r.since ? <span className="whitespace-nowrap">{formatDate(r.since)}</span> : <span className="text-[#9ca3af]">—</span>,
          },
          ...INTAKE_COLUMNS.map((c) => ({
            key: c.key,
            header: c.short,
            align: 'center' as const,
            render: (r: StudentIntake) => {
              const it = r.items.find((i) => i.key === c.key)
              return (
                <ItemCell
                  item={it}
                  onMark={canMark && it ? () => setMark({ row: r, item: it }) : undefined}
                />
              )
            },
          })),
          {
            key: 'progress',
            header: 'Holat',
            render: (r) => (
              <div className="min-w-[90px]" title={r.complete ? "To'liq" : `Qolgan: ${pendingLabels(r).join(', ')}`}>
                <span className="text-[12px] font-semibold">
                  {r.done}/{r.total}
                </span>
                <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
                  <div
                    className={cn('h-full rounded-full', barCls[progressTone(r.done, r.total)])}
                    style={{ width: `${(r.done / Math.max(1, r.total)) * 100}%` }}
                  />
                </div>
              </div>
            ),
          },
        ]}
      />

      {mark && (
        <IntakeMarkModal
          key={`${mark.row.studentId}:${mark.item.key}`}
          studentId={mark.row.studentId}
          studentName={mark.row.fullName}
          item={mark.item}
          onClose={() => setMark(null)}
          onSaved={(d) => setPatched((p) => ({ ...p, [d.checklist.studentId]: d.checklist }))}
        />
      )}
    </div>
  )
}
