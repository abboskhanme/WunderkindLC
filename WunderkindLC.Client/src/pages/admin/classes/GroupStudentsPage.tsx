import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getGroupStudents, type GroupStudentsPage as Page } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { DataTable } from '@/components/ui/list/DataTable'
import { FilterInput, FilterSelect } from '@/components/ui/list/FilterGrid'
import { TotalPill } from '@/components/ui/list/TotalPill'
import { Loader } from '@/components/ui/Loader'
import { studentStateBadge } from '@/config/constants'
import { apiErrorMessage, cn } from '@/lib/utils'
import type { Teacher } from '@/types'

const PAGE_SIZE = 50

/**
 * Guruh → «Guruh o'quvchilari» (edutizim `/group/group-students`): BARCHA guruhlardagi JORIY
 * a'zoliklar. Bitta o'quvchi ikki guruhda bo'lsa IKKI qator — savol "kim qaysi guruhda".
 *
 * ⚠️ Filtr va sahifalash SERVERDA (`GroupStudentsList`, 2–3 ming qator bo'lishi mumkin), shuning
 * uchun bu yerda `usePagination` (klient sahifalash) ISHLATILMAYDI.
 */
export function GroupStudentsPage() {
  const [frozen, setFrozen] = useState(false)
  const [teacherId, setTeacherId] = useState('')
  const [groupState, setGroupState] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)

  const [data, setData] = useState<Page | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [teachers, setTeachers] = useState<Teacher[]>([])

  useEffect(() => {
    getTeachers()
      .then(setTeachers)
      .catch(() => setTeachers([]))
  }, [])

  // Filtr o'zgarsa birinchi sahifaga qaytamiz (bo'sh sahifada "hech narsa yo'q" ko'rinib qolmasin).
  useEffect(() => {
    setPage(1)
  }, [frozen, teacherId, groupState, from, to, q])

  useEffect(() => {
    let alive = true
    setLoading(true)
    getGroupStudents({ frozen, teacherId, groupState, from, to, q, page, pageSize: PAGE_SIZE })
      .then((r) => {
        if (!alive) return
        setData(r)
        setError(null)
      })
      .catch((e) => alive && setError(apiErrorMessage(e, "Ro'yxatni yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [frozen, teacherId, groupState, from, to, q, page])

  const total = data?.total ?? 0
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  if (loading && !data) return <Loader className="min-h-[240px]" />

  return (
    <div>
      {/* Filtrlar — edutizimdagidek bitta qator, o'ngga tekislangan */}
      <div className="mb-2.5 flex flex-wrap items-center justify-end gap-2">
        <label className="flex h-[34px] items-center gap-2 rounded-lg border border-black/25 bg-white px-3 text-[13px] text-[#333]">
          <input
            type="checkbox"
            checked={frozen}
            onChange={(e) => setFrozen(e.target.checked)}
            className="h-4 w-4 accent-[#3d68ff]"
          />
          Muzlatilgan
        </label>
        <div className="w-[190px]">
          <FilterSelect placeholder="O'qituvchi" value={teacherId} onChange={(e) => setTeacherId(e.target.value)}>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </FilterSelect>
        </div>
        <div className="w-[170px]">
          <FilterSelect placeholder="Guruh holati" value={groupState} onChange={(e) => setGroupState(e.target.value)}>
            <option value="active">Arxivlanmagan</option>
            <option value="archived">Arxivdagi</option>
          </FilterSelect>
        </div>
        <FilterInput type="date" title="Qo'shilgan sana (dan)" value={from} onChange={(e) => setFrom(e.target.value)} className="w-[150px]" />
        <FilterInput type="date" title="Qo'shilgan sana (gacha)" value={to} onChange={(e) => setTo(e.target.value)} className="w-[150px]" />
        <div className="w-[220px]">
          <FilterInput search placeholder="Qidirish" value={q} onChange={(e) => setQ(e.target.value)} />
        </div>
      </div>

      {error && <p className="mb-2 text-[13px] text-red-600">{error}</p>}

      <div className="mb-2 flex justify-end">
        <TotalPill total={total} />
      </div>

      <DataTable
        rows={data?.items ?? []}
        loading={loading}
        rowKey={(r) => r.membershipId}
        numbered
        offset={(page - 1) * PAGE_SIZE}
        columns={[
          {
            key: 'name',
            header: 'Ism',
            render: (r) => (
              <Link
                to={`/admin/students/${r.studentId}`}
                className="font-medium text-[#333] hover:text-brand-600 hover:underline"
              >
                {r.fullName}
              </Link>
            ),
          },
          {
            key: 'group',
            header: 'Guruhlar',
            render: (r) => (
              <Link to={`/admin/classes/${r.groupId}`} className="text-[#333] hover:text-brand-600 hover:underline">
                {r.groupName}
                {r.groupArchived && <span className="ml-1 text-[11px] text-[#9ca3af]">(arxiv)</span>}
              </Link>
            ),
          },
          { key: 'teacher', header: "O'qituvchi", render: (r) => r.teacherName || <span className="text-[#9ca3af]">—</span> },
          {
            key: 'joined',
            header: "Qo'shilgan sana",
            render: (r) => {
              const v = (r.joinedAt ?? '').slice(0, 10)
              if (!v) return <span className="text-[#9ca3af]">—</span>
              const [y, m, d] = v.split('-')
              return <span className="whitespace-nowrap">{`${d}.${m}.${y}`}</span>
            },
          },
          {
            key: 'status',
            header: 'Holati',
            render: (r) => {
              // «Aktiv muzlatish» ham muzlatilgan (year-freeze.md §1), faqat belgisi boshqacha.
              const b = studentStateBadge(r.yearFreeze ? 'yearFrozen' : r.status)
              return b ? (
                <span className={`rounded-full px-2 py-0.5 text-[11px] font-semibold ${b.className}`}>{b.label}</span>
              ) : (
                <span className="text-[#333]">aktiv</span>
              )
            },
          },
        ]}
        footer={
          <div className="pagination flex-wrap gap-3">
            <span className="mr-auto text-[12px] text-[#6b7280]">
              {total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1}–{Math.min(total, page * PAGE_SIZE)} / {total}
            </span>
            <div className="pages items-center">
              <button type="button" className="pg-btn" disabled={page <= 1} onClick={() => setPage(1)}>
                ««
              </button>
              <button type="button" className="pg-btn" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                ‹
              </button>
              <span className="px-2 text-[13px] text-[#333]">
                {page} / {pages}
              </span>
              <button type="button" className={cn('pg-btn')} disabled={page >= pages} onClick={() => setPage(page + 1)}>
                ›
              </button>
              <button type="button" className="pg-btn" disabled={page >= pages} onClick={() => setPage(pages)}>
                »»
              </button>
            </div>
          </div>
        }
      />
    </div>
  )
}
