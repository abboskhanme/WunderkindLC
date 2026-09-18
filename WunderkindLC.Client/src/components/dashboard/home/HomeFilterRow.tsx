import { useMemo } from 'react'
import type { ScheduleGrid } from '@/api/services/schedule'
import { STATUS_OPTIONS, naturalCompare, type HomeParams } from '@/lib/scheduleGrid'
import { FilterSelect } from './HomeControls'

type FilterKey = 'teacher' | 'group' | 'room' | 'course' | 'status'

/**
 * "Filtr" tugmasi ochadigan qator (edutizim §3): o'ngga tekislangan beshta ixcham select —
 * "O'qituvchi" · "Guruh" · "Xona" · "Kurs" · "Holati". Variantlar jadval ma'lumotining O'ZIDAN
 * olinadi (alohida so'rov yo'q). Qiymat manzilga yoziladi (sahifa boshqaradi).
 */
export function HomeFilterRow({
  grid,
  params,
  onChange,
}: {
  grid: ScheduleGrid | null
  params: HomeParams
  onChange: (key: FilterKey, value: string) => void
}) {
  const options = useMemo(() => {
    const groups = grid?.groups ?? []
    const courses = new Map<string, string>()
    for (const g of groups) if (g.courseId) courses.set(g.courseId, g.courseName || g.courseId)
    return {
      teacher: (grid?.teachers ?? []).map((t) => ({ value: t.id, label: t.name })),
      group: [...groups]
        .sort((a, b) => naturalCompare(a.groupName, b.groupName))
        .map((g) => ({ value: g.groupId, label: g.groupName })),
      room: (grid?.rooms ?? []).map((r) => ({ value: r.id, label: r.name })),
      course: [...courses.entries()]
        .sort((a, b) => naturalCompare(a[1], b[1]))
        .map(([value, label]) => ({ value, label })),
      status: STATUS_OPTIONS.map((o) => ({ value: o.value, label: o.label })),
    }
  }, [grid])

  return (
    <div className="flex flex-wrap justify-end gap-2.5">
      <FilterSelect placeholder="O'qituvchi" value={params.teacher} options={options.teacher} onChange={(v) => onChange('teacher', v)} />
      <FilterSelect placeholder="Guruh" value={params.group} options={options.group} onChange={(v) => onChange('group', v)} />
      <FilterSelect placeholder="Xona" value={params.room} options={options.room} onChange={(v) => onChange('room', v)} />
      <FilterSelect placeholder="Kurs" value={params.course} options={options.course} onChange={(v) => onChange('course', v)} />
      <FilterSelect placeholder="Holati" value={params.status} options={options.status} onChange={(v) => onChange('status', v)} />
    </div>
  )
}
