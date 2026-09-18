import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { getSubscriptionRisk, type SubscriptionRiskRow } from '@/api/services/students'
import { DataTable } from '@/components/ui/list/DataTable'
import { FilterSelect } from '@/components/ui/list/FilterGrid'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { studentStateBadge } from '@/config/constants'
import { balanceTextCls, formatMoney } from '@/lib/utils'

/**
 * O'quvchilar → «Joriy oyda obunasi tugaydiganlar» (edutizim `/students/debt-risk`):
 * puli JORIY OY darslarini qoplamaydigan o'quvchilar.
 *
 * Hisob SERVERDA (`SubscriptionRisk`): mavjud oylik hisob qatorlari + hali yozilmagan qism.
 * ⚠️ Bu yerda qayta hisob-kitob QILINMAYDI — chegirma, qisman oylik va a'zolik davrlari qoidasi
 * bitta joyda qoladi (`.claude/rules/billing.md`, `membership-periods.md`).
 */
export function SubscriptionRiskPage() {
  const { data, loading, error } = useAsync(getSubscriptionRisk, [])
  const [state, setState] = useState('')

  const rows = useMemo(
    () => (data?.items ?? []).filter((r) => !state || r.memberState === state),
    [data, state],
  )
  const pg = usePagination(rows)

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  const money = (n: number) => `${formatMoney(n)}`

  return (
    <div>
      <div className="mb-2.5 flex flex-wrap items-center justify-between gap-2">
        <p className="text-[13px] text-[#6b7280]">
          <b className="text-[#333]">Jami darslar narxi</b> {money(data?.totalLessons ?? 0)}
          <span className="mx-2 text-[#dbe0e6]">|</span>
          <b className="text-[#333]">Joriy balans</b> {money(data?.totalBalance ?? 0)}
          <span className="mx-2 text-[#dbe0e6]">|</span>
          <b className="text-[#333]">Kutilayotgan balans</b> {money(data?.totalExpected ?? 0)}
        </p>
        <div className="w-[180px]">
          <FilterSelect placeholder="Statusi" value={state} onChange={(e) => setState(e.target.value)}>
            <option value="active">Aktiv</option>
            <option value="trial">Sinovda</option>
            <option value="frozen">Muzlatilgan</option>
            <option value="yearFrozen">Aktiv muzlatilgan</option>
          </FilterSelect>
        </div>
      </div>

      <DataTable<SubscriptionRiskRow>
        rows={pg.paged}
        columns={[
          {
            key: 'name',
            header: "O'quvchini ismi",
            render: (r) => (
              <Link
                to={`/admin/students/${r.studentId}`}
                className="font-medium text-[#333] hover:text-brand-600 hover:underline"
              >
                {r.fullName}
              </Link>
            ),
          },
          { key: 'phone', header: 'Telefon raqam', render: (r) => <PhoneChip phone={r.phone || r.parentPhone} /> },
          {
            key: 'state',
            header: 'Holati',
            render: (r) => {
              const b = studentStateBadge(r.memberState)
              return b ? (
                <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${b.className}`}>{b.label}</span>
              ) : (
                <span className="text-[#333]">aktiv</span>
              )
            },
          },
          {
            key: 'lessons',
            header: 'Jami darslar narxi',
            align: 'right',
            render: (r) => money(r.lessonsTotal),
          },
          {
            key: 'balance',
            header: 'Joriy balans',
            align: 'right',
            render: (r) => <span className={balanceTextCls(r.balance)}>{money(r.balance)}</span>,
          },
          {
            key: 'expected',
            header: 'Kutilayotgan balans',
            align: 'right',
            render: (r) => <span className={balanceTextCls(r.expected)}>{money(r.expected)}</span>,
          },
          {
            key: 'more',
            header: '',
            render: (r) => (
              <Link
                to={`/admin/students/${r.studentId}`}
                className="text-[12px] font-medium text-brand-600 underline underline-offset-2"
              >
                Batafsil
              </Link>
            ),
          },
        ]}
        rowKey={(r) => r.studentId}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        footer={<TablePagination {...pg} />}
      />
    </div>
  )
}
