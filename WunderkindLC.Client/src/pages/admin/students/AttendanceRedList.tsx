import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { getRedList, type RedListRow } from '@/api/services/absences'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { useAsync } from '@/hooks/useAsync'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'

const CONTACT_LABELS: Record<string, string> = {
  new: "Bog'lanish kerak",
  callback: "Qayta qo'ng'iroq",
  done: "Hal bo'ldi",
  failed: "Bog'lanib bo'lmadi",
}

const dm = (d: string) => (d.length >= 10 ? `${d.slice(8, 10)}.${d.slice(5, 7)}` : d)

/**
 * «O'quvchilar davomati» → «Qizil ro'yxat» (CRM Phase 3, U3d — SPEC P3.5): bitta guruhda ketma-ket SABABSIZ dars
 * qoldirganlar. Sababli yo'qlik, «kech» yoki «keldi» seriyani to'xtatadi; belgilanmagan dars o'tkazib yuboriladi.
 * 3+ — sariq, 5+ — qizil. 3 ga yetganda tizim o'zi «Bog'lanish kerak» talabini ochadi (oxirgi yo'qlik ≤ 14 kun).
 */
export function AttendanceRedList() {
  const { can } = usePerm()
  const [min, setMin] = useState<3 | 5>(3)
  const [groupId, setGroupId] = useState('')
  const [teacher, setTeacher] = useState('')
  const { data, loading, error } = useAsync(() => getRedList({ min }), [min])

  const groups = useMemo(() => {
    const m = new Map<string, string>()
    for (const r of data ?? []) m.set(r.groupId, r.groupName)
    return [...m.entries()].sort((a, b) => a[1].localeCompare(b[1], 'uz'))
  }, [data])
  const teachers = useMemo(
    () => [...new Set((data ?? []).map((r) => r.teacherName).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'uz')),
    [data],
  )
  const rows = (data ?? []).filter(
    (r: RedListRow) => (!groupId || r.groupId === groupId) && (!teacher || r.teacherName === teacher),
  )

  const selectCls =
    'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-700 outline-none focus:border-brand-400'

  return (
    <Card>
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <div className="flex rounded-lg border border-slate-200 bg-white p-1">
          {([3, 5] as const).map((k) => (
            <button
              key={k}
              type="button"
              onClick={() => setMin(k)}
              className={cn(
                'h-8 rounded-md px-3 text-[13px] font-semibold transition-colors',
                min === k ? (k === 5 ? 'bg-red-600 text-white' : 'bg-amber-500 text-white') : 'text-slate-600 hover:bg-slate-50',
              )}
            >
              {k}+ dars
            </button>
          ))}
        </div>
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} className={selectCls} aria-label="Guruh">
          <option value="">Barcha guruhlar</option>
          {groups.map(([id, name]) => (
            <option key={id} value={id}>
              {name}
            </option>
          ))}
        </select>
        <select value={teacher} onChange={(e) => setTeacher(e.target.value)} className={selectCls} aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {teachers.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
        <span className="ml-auto text-sm font-semibold text-slate-600">{rows.length} ta</span>
      </div>

      {error && <p className="mb-2 text-sm text-red-600">{error}</p>}
      {loading && !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : rows.length === 0 ? (
        <p className="py-10 text-center text-sm text-slate-500">
          Ketma-ket {min} va undan ko'p dars sababsiz qoldirgan o'quvchi yo'q.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="min-w-full text-sm">
            <thead>
              <tr className="border-b border-slate-200 text-left text-xs font-semibold uppercase text-slate-500">
                <th className="px-2 py-2">Daraja</th>
                <th className="px-2 py-2">O'quvchi</th>
                <th className="px-2 py-2">Guruh</th>
                <th className="px-2 py-2">O'qituvchi</th>
                <th className="px-2 py-2">Qoldirgan darslari</th>
                <th className="px-2 py-2">Bog'lanish</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={`${r.studentId}|${r.groupId}`} className="border-b border-slate-100 align-top">
                  <td className="px-2 py-2">
                    <span
                      className={cn(
                        'inline-flex rounded-full px-2 py-0.5 text-xs font-bold',
                        r.streak >= 5 ? 'bg-red-100 text-red-700' : 'bg-amber-100 text-amber-800',
                      )}
                    >
                      {r.streak >= 5 ? '5+' : '3+'}
                    </span>
                  </td>
                  <td className="px-2 py-2">
                    <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-800 hover:text-brand-600 hover:underline">
                      {r.studentName}
                    </Link>
                    <div className="text-xs text-slate-500">{r.streak} ta dars ketma-ket</div>
                  </td>
                  <td className="px-2 py-2">{r.groupName}</td>
                  <td className="px-2 py-2">{r.teacherName || '—'}</td>
                  <td className="px-2 py-2">
                    <div className="flex flex-wrap gap-1">
                      {r.dates.map((d) => (
                        <span key={d} className="rounded-md bg-red-50 px-1.5 py-0.5 font-mono text-[11px] text-red-700">
                          {dm(d)}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-2 py-2">
                    {r.contact ? (
                      can('contacts', 'view') ? (
                        <Link to="/admin/students/boglanish" className="text-xs font-semibold text-brand-600 hover:underline">
                          {CONTACT_LABELS[r.contact.status] ?? r.contact.status}
                        </Link>
                      ) : (
                        <span className="text-xs text-slate-600">{CONTACT_LABELS[r.contact.status] ?? r.contact.status}</span>
                      )
                    ) : (
                      <span className="text-xs text-slate-400">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}
