import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getTransactions } from '@/api/services/finance'
import { DataTable } from '@/components/ui/list/DataTable'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { formatMoney } from '@/lib/utils'

/**
 * Moliya → «Jarima» (edutizim `/finance/penalty`): xodimdan ushlangan summalar ro'yxati.
 *
 * ⚠️ Yangi jadval YO'Q — jarima oddiy CHIQIM tranzaksiyasi (`category = "penalty"`), ya'ni u
 * kassa, kirim-chiqim va P&L hisobotlariga o'z-o'zidan kiradi. "Jarima qo'shish" Moliya
 * bo'limidagi "Yangi amal" formasini shu toifa bilan ochadi.
 */
export function PenaltyPage() {
  const { can } = usePerm()
  const navigate = useNavigate()
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

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div>
      <ListToolbar
        filtersOpen={filtersOpen}
        onToggleFilters={() => setFiltersOpen((v) => !v)}
        addLabel={can('finance.main', 'create') ? "Jarima qo'shish" : undefined}
        // "Yangi amal" formasi Moliya bo'limida — jarima ham oddiy chiqim sifatida kiritiladi.
        onAdd={can('finance.main', 'create') ? () => navigate('/admin/finance?tab=overview') : undefined}
      />

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
