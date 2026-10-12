import { useMemo } from 'react'
import type { DatedSchedule } from '@/api/services/schedule'
import { FLOOR_NONE, floorLabel, type DatedParams } from '@/lib/scheduleDated'
import { STATUS_OPTIONS, naturalCompare } from '@/lib/scheduleGrid'
import { FilterSelect } from './HomeControls'

export type DatedFilterKey = 'teacher' | 'group' | 'room' | 'course' | 'floor' | 'status'

/**
 * Sanali jadval filtrlari: «O'qituvchi» · «Guruh» · «Kurs» · «Xona» · «Qavat» · «Holat». Variantlar
 * jadval ma'lumotining O'ZIDAN (alohida so'rov yo'q); qiymat manzilga yoziladi (sahifa boshqaradi).
 */
export function DatedFilterRow({
  data,
  params,
  onChange,
}: {
  data: DatedSchedule | null
  params: DatedParams
  onChange: (key: DatedFilterKey, value: string) => void
}) {
  const options = useMemo(() => {
    const lessons = data?.lessons ?? []
    const groups = new Map<string, string>()
    const courses = new Map<string, string>()
    for (const l of lessons) {
      groups.set(l.groupId, l.groupName)
      if (l.courseId) courses.set(l.courseId, l.courseName || l.courseId)
    }
    const floors = [...new Set((data?.rooms ?? []).map((r) => r.floor))]
      .sort((a, b) => (a === null ? 1 : 0) - (b === null ? 1 : 0) || (a ?? 0) - (b ?? 0))
    const byName = (a: [string, string], b: [string, string]) => naturalCompare(a[1], b[1])
    return {
      teacher: (data?.teachers ?? []).map((t) => ({ value: t.id, label: t.name })),
      group: [...groups.entries()].sort(byName).map(([value, label]) => ({ value, label })),
      course: [...courses.entries()].sort(byName).map(([value, label]) => ({ value, label })),
      room: (data?.rooms ?? []).map((r) => ({ value: r.id, label: r.name })),
      floor: floors.map((f) => ({ value: f === null ? FLOOR_NONE : String(f), label: floorLabel(f) })),
      status: STATUS_OPTIONS.map((o) => ({ value: o.value, label: o.label })),
    }
  }, [data])

  return (
    <div className="flex flex-wrap justify-end gap-2.5">
      <FilterSelect placeholder="O'qituvchi" value={params.teacher} options={options.teacher} onChange={(v) => onChange('teacher', v)} />
      <FilterSelect placeholder="Guruh" value={params.group} options={options.group} onChange={(v) => onChange('group', v)} />
      <FilterSelect placeholder="Kurs" value={params.course} options={options.course} onChange={(v) => onChange('course', v)} />
      <FilterSelect placeholder="Xona" value={params.room} options={options.room} onChange={(v) => onChange('room', v)} />
      <FilterSelect placeholder="Qavat" value={params.floor} options={options.floor} onChange={(v) => onChange('floor', v)} />
      <FilterSelect placeholder="Holat" value={params.status} options={options.status} onChange={(v) => onChange('status', v)} />
    </div>
  )
}
