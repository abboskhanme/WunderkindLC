import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import type { DatedLesson, DatedRoom, DatedTeacher } from '@/api/services/schedule'
import {
  WEEKDAY_SHORT,
  dotted,
  groupRuns,
  weekRows,
  type DatedParams,
} from '@/lib/scheduleDated'
import { lessonColor, type GroupBy } from '@/lib/scheduleGrid'
import { cn } from '@/lib/utils'

const OWNER_W = 150
const DAY_W = 170

/**
 * «HAFTA» ko'rinishi: qatorlar — xonalar (QAVAT bo'yicha guruhlangan) yoki o'qituvchilar, ustunlar —
 * Dushanbadan Yakshanbagacha 7 sana, katakda dars chiplari vaqt bo'yicha («14:00 Guruh A»).
 * Ko'chirilgan dars «ko'chirilgan», o'rinbosar «o'rinbosar: …» bilan belgilanadi.
 */
export function WeekBoard({
  lessons,
  rooms,
  teachers,
  dates,
  groupBy,
  params,
  today,
  canOpenGroup,
  maxHeight,
}: {
  lessons: DatedLesson[]
  rooms: DatedRoom[]
  teachers: DatedTeacher[]
  dates: string[]
  groupBy: GroupBy
  params: Pick<DatedParams, 'room' | 'floor' | 'teacher'>
  today: string
  canOpenGroup: boolean
  maxHeight: string
}) {
  const rows = useMemo(
    () => weekRows(groupBy, rooms, teachers, lessons, dates, params),
    [groupBy, rooms, teachers, lessons, dates, params],
  )
  const runs = groupRuns(rows)

  if (rows.length === 0)
    return (
      <p className="py-10 text-center text-[13px] text-[#6B7280]">
        {groupBy === 'room' ? "Xonalar hali qo'shilmagan" : "O'qituvchilar yo'q"} — ko'rsatiladigan qator yo'q.
      </p>
    )

  return (
    <div className="overflow-auto rounded-lg border border-[#E5E7EB]" style={{ maxHeight }}>
      <table className="border-separate border-spacing-0 text-[12px]" style={{ minWidth: OWNER_W + dates.length * DAY_W }}>
        <thead className="sticky top-0 z-20 bg-white">
          <tr>
            <th
              className="sticky left-0 z-30 border-b border-r border-[#E5E7EB] bg-[#F9FAFB]"
              style={{ width: OWNER_W, minWidth: OWNER_W }}
            />
            {dates.map((d, i) => (
              <th
                key={d}
                className={cn(
                  'border-b border-r border-[#E5E7EB] px-2 py-2 text-center text-[13px] font-semibold',
                  d === today ? 'bg-[#EEF2FF] text-[#3D68FF]' : 'text-[#374151]',
                )}
                style={{ width: DAY_W, minWidth: DAY_W }}
              >
                {WEEKDAY_SHORT[i]} {dotted(d).slice(0, 5)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {runs.map((run, ri) => [
            run.group ? (
              <tr key={`g:${run.group}:${ri}`}>
                <td
                  colSpan={dates.length + 1}
                  className="border-b border-[#E5E7EB] bg-[#EEF2FF] px-3 py-1 text-[12px] font-semibold text-[#3D68FF]"
                >
                  <span className="sticky left-3">{run.group}</span>
                </td>
              </tr>
            ) : null,
            ...run.items.map((r) => (
              <tr key={r.key || '__none'}>
                <td
                  title={r.name}
                  className="sticky left-0 z-10 border-b border-r border-[#E5E7EB] bg-white px-2 py-2 align-top text-[13px] font-semibold text-[#374151]"
                  style={{ width: OWNER_W, minWidth: OWNER_W }}
                >
                  <span className="line-clamp-2">{r.name}</span>
                </td>
                {dates.map((d) => (
                  <td key={d} className="border-b border-r border-[#E5E7EB] p-1 align-top">
                    <div className="flex flex-col gap-1">
                      {r.cells[d].map((l) => (
                        <LessonChip key={`${l.groupId}:${l.date}`} lesson={l} canOpen={canOpenGroup} groupBy={groupBy} />
                      ))}
                    </div>
                  </td>
                ))}
              </tr>
            )),
          ])}
        </tbody>
      </table>
    </div>
  )
}

function LessonChip({ lesson: l, canOpen, groupBy }: { lesson: DatedLesson; canOpen: boolean; groupBy: GroupBy }) {
  const color = lessonColor(l)
  const other = groupBy === 'room' ? (l.substituteTeacherName || l.teacherName) : l.roomName
  const content = (
    <>
      <div className="truncate font-semibold text-[#111827]">
        {l.start} {l.groupName}
      </div>
      {other && <div className="truncate text-[11px] text-[#6B7280]">{other}</div>}
      {l.moved && <div className="text-[10.5px] font-semibold text-amber-700">ko'chirilgan</div>}
      {l.substituteTeacherId && (
        <div className="truncate text-[10.5px] font-semibold text-violet-700">o'rinbosar: {l.substituteTeacherName}</div>
      )}
    </>
  )
  const cls = 'block rounded-md border border-[#E5E7EB] bg-white px-1.5 py-1 leading-[15px] hover:bg-[#F9FAFB]'
  const style = { borderLeft: `3px solid ${color}` }
  return canOpen ? (
    <Link to={`/admin/classes/${l.groupId}`} className={cls} style={style} title={`${l.start}–${l.end} · ${l.groupName}`}>
      {content}
    </Link>
  ) : (
    <div className={cls} style={style} title={`${l.start}–${l.end} · ${l.groupName}`}>
      {content}
    </div>
  )
}
