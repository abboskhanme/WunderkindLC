import { useMemo, useState } from 'react'
import { getTransactions } from '@/api/services/finance'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { useAsync } from '@/hooks/useAsync'
import { cn, formatMoney } from '@/lib/utils'
import { tabFromUrl } from '@/lib/tabParam'
import { AttendanceFinesTab } from './AttendanceFinesTab'
import { TeacherFinesTab } from './TeacherFinesTab'

const PENALTY_TABS = ['list', 'davomat'] as const
type PenaltyTab = (typeof PENALTY_TABS)[number]

/**
 * Moliya → «Jarima»: ikki tab — «Jarimalar» (xodimdan ushlangan summalar, edutizim `/finance/penalty`) va
 * «Davomat jarimalari» (CRM Phase 3, U3c — davomat o'z vaqtida olinmagan darslar). Boshlang'ich tab
 * `?tab=davomat` bilan ham ochiladi (`lib/tabParam.ts`).
 */
export function PenaltyPage() {
  const [tab, setTab] = useState<PenaltyTab>(() => tabFromUrl(PENALTY_TABS, 'list'))
  return (
    <div>
      <div className="mb-3 flex w-fit rounded-lg border border-[#dbe0e6] bg-white p-1">
        {PENALTY_TABS.map((k) => (
          <button
            key={k}
            type="button"
            onClick={() => setTab(k)}
            className={cn(
              'h-8 rounded-md px-3 text-[13px] font-medium transition-colors',
              tab === k ? 'bg-brand-600 text-white' : 'text-[#333] hover:bg-[#f0f2f2]',
            )}
          >
            {k === 'list' ? 'Jarimalar' : 'Davomat jarimalari'}
          </button>
        ))}
      </div>
      {tab === 'list' ? (
        <>
          <TeacherFinesTab />
          <LegacyPenaltyList />
        </>
      ) : (
        <AttendanceFinesTab />
      )}
    </div>
  )
}

/**
 * ESKI jarimalar — 2026-10-13 gacha CHIQIM sifatida kiritilgan (`category = "penalty"`): ular kassadan pul chiqqandek
 * ko'rinadi va maoshdan USHLANMAGAN. Yangi jarima endi `TeacherFinesTab` da (maoshdan ushlanadi); bu ro'yxat faqat
 * tarix uchun, faqat qatori bo'lsa ko'rinadi. Yangi `penalty` chiqimini server rad etadi.
 */
function LegacyPenaltyList() {
  const [q, setQ] = useState('')
  const [filtersOpen, setFiltersOpen] = useState(false)
  const { data, loading, error } = useAsync(() => getTransactions({ direction: 'expense' }), [])

  const rows = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return (data ?? [])
      .filter((t) => t.category === 'penalty')
      .filter((t) =>
        !needle ||
        (t.teacherName ?? '').toLowerCase().includes(needle) ||
        (t.note ?? '').toLowerCase().includes(needle),
      )
  }, [data, q])

  const pg = usePagination(rows)

  if (loading && !data) return null
  if (error) return <p className="mt-6 text-[13px] text-red-600">{error}</p>
  if ((data ?? []).every((t) => t.category !== 'penalty')) return null

  return (
    <div className="mt-8">
      <p className="mb-1 text-[14px] font-semibold text-black">Eski yozuvlar — chiqim sifatida kiritilgan</p>
      <p className="mb-2.5 text-[12px] text-[#6b7280]">
        2026-10-13 gacha jarima kassadan CHIQIM bo'lib yozilardi va maoshdan ushlanmasdi. Bu yozuvlar tarix uchun
        ko'rsatiladi; yangi jarima yuqorida qo'yiladi.
      </p>
      <ListToolbar filtersOpen={filtersOpen} onToggleFilters={() => setFiltersOpen((v) => !v)} />

      {filtersOpen && (
        <div className="mb-2.5">
          <input
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Qidiruv"
            className="h-[34px] w-full max-w-[280px] rounded-lg border border-black/25 bg-white px-3 text-[13px] outline-none focus:border-brand-600"
          />
        </div>
      )}

      <div className="mb-2 flex justify-end">
        <TotalPill total={rows.length} />
      </div>

      <DataTable
        rows={pg.paged}
        rowKey={(t) => t.id}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        columns={[
          { key: 'name', header: "To'liq ismi", render: (t) => t.teacherName || t.studentName || <span className="text-[#9ca3af]">—</span> },
          { key: 'amount', header: 'Miqdori', align: 'right', render: (t) => formatMoney(t.amount) },
          { key: 'note', header: 'Izoh', render: (t) => t.note || <span className="text-[#9ca3af]">—</span> },
          { key: 'month', header: 'Oy', render: (t) => t.month || <span className="text-[#9ca3af]">—</span> },
          {
            key: 'date',
            header: 'Sana',
            render: (t) => {
              const v = (t.date ?? '').slice(0, 10)
              if (!v) return <span className="text-[#9ca3af]">—</span>
              const [y, m, d] = v.split('-')
              return <span className="whitespace-nowrap">{`${d}.${m}.${y}`}</span>
            },
          },
          { key: 'by', header: 'Kiritgan', render: (t) => t.createdBy || <span className="text-[#9ca3af]">—</span> },
        ]}
        footer={<TablePagination {...pg} />}
      />
    </div>
  )
}
