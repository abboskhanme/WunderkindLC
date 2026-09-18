import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAsync } from '@/hooks/useAsync'
import type { Student } from '@/types'
import { DataTable, type DataColumn } from '@/components/ui/list/DataTable'
import { FilterGrid, FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { ListToolbar } from '@/components/ui/list/ListToolbar'
import { PhoneChip } from '@/components/ui/list/PhoneChip'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { Loader } from '@/components/ui/Loader'
import { balanceTextCls, formatMoney } from '@/lib/utils'
import { groupsText } from '@/lib/studentGroups'

/** Jadval ustuni — `DataColumn` ning o'quvchi qatoriga moslangan ko'rinishi. */
export type StudentColumn = DataColumn<Student>

interface StudentListBoardProps {
  /** Ro'yxatni yuklovchi (sahifa o'z filtri bilan chaqiradi). */
  load: () => Promise<Student[]>
  /** Ustunlar — har sahifada edutizimdagi tartibda. */
  columns: StudentColumn[]
  /** Bo'sh holat matni o'rniga hech narsa kerak emas — `DataTable` o'zi yozadi. */
  emptyHint?: string
}

/**
 * O'QUVCHILAR RO'YXATI (edutizim ko'rinishi, `docs/edutizim/INVENTORY.md` §6):
 * sahifa SARLAVHASIZ, tepada asboblar qatori (voronka), filtr to'ri, "Qarzdor | Haqdor" jamlamasi,
 * "Umumiy soni" va DataGrid ko'rinishidagi jadval + "50 qator" sahifalash.
 *
 * ⚠️ Filtr va qidiruv KLIENTDA: ro'yxat bir so'rovda keladi (`GET /admin/students`), ya'ni har
 * bosishda server so'rovi ketmaydi. Katta markazda ro'yxat sahifalanadi (`usePagination`).
 */
export function StudentListBoard({ load, columns }: StudentListBoardProps) {
  const { data, loading, error } = useAsync(load, [])
  const students = useMemo(() => data ?? [], [data])

  const [filtersOpen, setFiltersOpen] = useState(false)
  const [q, setQ] = useState('')
  const [group, setGroup] = useState('')
  const [balance, setBalance] = useState('')
  const [moderator, setModerator] = useState('')

  const groupOptions = useMemo(
    () => [...new Set(students.flatMap((s) => s.groups ?? []).filter(Boolean))].sort(),
    [students],
  )
  const moderatorOptions = useMemo(
    () => [...new Set(students.map((s) => s.moderator ?? '').filter(Boolean))].sort(),
    [students],
  )

  const shown = useMemo(() => {
    const needle = q.trim().toLowerCase()
    const digits = needle.replace(/\D/g, '')
    return students.filter((s) => {
      if (group && !(s.groups ?? []).includes(group)) return false
      if (balance === 'debt' && s.balance >= 0) return false
      if (balance === 'credit' && s.balance <= 0) return false
      if (moderator && (s.moderator ?? '') !== moderator) return false
      if (!needle) return true
      const phones = [s.phone, s.parentPhone, s.fatherPhone, s.motherPhone]
        .filter(Boolean)
        .join(' ')
        .replace(/\D/g, '')
      return (
        s.fullName.toLowerCase().includes(needle) ||
        (s.parentFullName ?? '').toLowerCase().includes(needle) ||
        (digits.length >= 3 && phones.includes(digits))
      )
    })
  }, [students, q, group, balance, moderator])

  // "Qarzdor" / "Haqdor" — edutizimdagidek jadval TEPASIDA, ko'rinayotgan ro'yxat bo'yicha.
  const debt = shown.reduce((sum, s) => sum + (s.balance < 0 ? -s.balance : 0), 0)
  const credit = shown.reduce((sum, s) => sum + (s.balance > 0 ? s.balance : 0), 0)

  const pg = usePagination(shown)

  if (loading && students.length === 0) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  return (
    <div>
      <ListToolbar filtersOpen={filtersOpen} onToggleFilters={() => setFiltersOpen((v) => !v)} />

      {filtersOpen && (
        <FilterGrid>
          <FilterInput
            search
            placeholder="Qidiruv"
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
          <FilterSelect placeholder="Guruh" value={group} onChange={(e) => setGroup(e.target.value)}>
            {groupOptions.map((g) => (
              <option key={g} value={g}>
                {g}
              </option>
            ))}
          </FilterSelect>
          <FilterSelect placeholder="Balans" value={balance} onChange={(e) => setBalance(e.target.value)}>
            <option value="debt">Qarzdor</option>
            <option value="credit">Haqdor</option>
          </FilterSelect>
          {moderatorOptions.length > 0 && (
            <FilterSelect placeholder="Moderator" value={moderator} onChange={(e) => setModerator(e.target.value)}>
              {moderatorOptions.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </FilterSelect>
          )}
        </FilterGrid>
      )}

      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <p className="text-[13px] text-[#333]">
          <span className="font-semibold text-[#e53e3e]">Qarzdor</span>{' '}
          <span className="text-[#6b7280]">{formatMoney(debt)}</span>
          <span className="mx-2 text-[#dbe0e6]">|</span>
          <span className="font-semibold text-[#2e7d32]">Haqdor</span>{' '}
          <span className="text-[#6b7280]">{formatMoney(credit)}</span>
        </p>
        <TotalPill total={shown.length} />
      </div>

      <DataTable
        rows={pg.paged}
        columns={columns}
        rowKey={(s) => s.id}
        numbered
        offset={(pg.page - 1) * pg.pageSize}
        footer={<TablePagination {...pg} />}
      />
    </div>
  )
}

/* ─────────────── qayta ishlatiladigan ustunlar (edutizim nomlari bilan) ─────────────── */

/** ISM — o'quvchi profiliga havola (edutizimda ham faqat ism bosiladi). */
export const nameColumn: StudentColumn = {
  key: 'name',
  header: "O'quvchini ismi",
  render: (s) => (
    <Link to={`/admin/students/${s.id}`} className="font-medium text-[#333] hover:text-brand-600 hover:underline">
      {s.fullName}
    </Link>
  ),
}

export const phoneColumn: StudentColumn = {
  key: 'phone',
  header: 'Telefon raqam',
  render: (s) => <PhoneChip phone={s.phone || s.parentPhone} />,
}

export const balanceColumn: StudentColumn = {
  key: 'balance',
  header: 'Balans',
  align: 'right',
  render: (s) => <span className={balanceTextCls(s.balance)}>{formatMoney(s.balance)}</span>,
}

export const groupColumn: StudentColumn = {
  key: 'group',
  header: 'Guruh',
  render: (s) => <span className="text-[#333]">{groupsText(s.groupStates, s.className, '—')}</span>,
}

export const moderatorColumn: StudentColumn = {
  key: 'moderator',
  header: 'Moderator',
  render: (s) => s.moderator || <span className="text-[#9ca3af]">—</span>,
}

/** Sana ustuni — "dd.mm.yyyy" (edutizim ro'yxatlaridagi ko'rinish). */
export function dateColumn(key: string, header: string, pick: (s: Student) => string | undefined): StudentColumn {
  return {
    key,
    header,
    render: (s) => {
      const v = (pick(s) ?? '').slice(0, 10)
      if (!v) return <span className="text-[#9ca3af]">—</span>
      const [y, m, d] = v.split('-')
      return <span className="whitespace-nowrap">{d && m && y ? `${d}.${m}.${y}` : v}</span>
    },
  }
}
