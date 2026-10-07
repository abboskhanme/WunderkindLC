import { useState } from 'react'
import { Link } from 'react-router-dom'
import { AlarmClock, Clock3 } from 'lucide-react'
import type { TodayLessonMonitor } from '@/types'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { cn } from '@/lib/utils'
import { notTakenLessons, phaseLabel } from '@/lib/attendanceWatch'

/**
 * «DAVOMAT OLINMAGAN GURUHLAR» — tanlangan kunda darsi bor, lekin davomati hali olinmagan
 * guruhlar: qaysi o'qituvchi (yoki o'rinbosar) hali davomat olmagani DARHOL ko'rinsin.
 *
 * Ma'lumot «Nazorat → Davomat qilinmagan guruhlar» bilan BITTA manbadan
 * (`GET /admin/dashboard/today-lessons`) — bu yerda faqat qisqa ko'rinish; to'liq monitor
 * (baho holati, o'qituvchi bo'yicha guruhlash) o'sha sahifada, ruxsat bo'lsa havola beriladi.
 */
export function NotTakenLessonsCard({
  lessons,
  error,
  monitorHref,
}: {
  lessons: TodayLessonMonitor[]
  error: string
  /** To'liq monitor sahifasi — `classes.notAttended` ruxsati bo'lmasa `null` (havola chiqmaydi). */
  monitorHref: string | null
}) {
  const [onlyOverdue, setOnlyOverdue] = useState(true)
  const overdue = notTakenLessons(lessons, true)
  const all = notTakenLessons(lessons, false)
  const shown = onlyOverdue ? overdue : all

  return (
    <Card
      title="Davomat olinmagan guruhlar"
      sub="Darsi bor, lekin o'qituvchi hali davomat olmagan. Ko'chirilgan darslar va o'rinbosarlar hisobga olingan."
      tight
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <div className="tabs">
            <button type="button" className={cn('tab', onlyOverdue && 'active')} onClick={() => setOnlyOverdue(true)}>
              Kechikkanlar ({overdue.length})
            </button>
            <button type="button" className={cn('tab', !onlyOverdue && 'active')} onClick={() => setOnlyOverdue(false)}>
              Hammasi ({all.length})
            </button>
          </div>
          {monitorHref && (
            <Link to={monitorHref} className="text-sm font-medium text-brand-600 no-underline hover:underline">
              Darslar monitoringi →
            </Link>
          )}
        </div>
      }
    >
      {error ? (
        <p className="px-4 py-3 text-sm text-red-600">{error}</p>
      ) : shown.length === 0 ? (
        <p className="px-4 py-8 text-center text-sm text-slate-400">
          {lessons.length === 0
            ? "Bu kunda dars yo'q"
            : onlyOverdue
              ? "Kechikkan guruh yo'q — boshlangan barcha darslarda davomat olingan"
              : 'Barcha darslarda davomat olingan'}
        </p>
      ) : (
        <div className="table-wrap">
          <table className="table">
            <thead>
              <tr>
                <th>Dars vaqti</th>
                <th>Guruh</th>
                <th>O'qituvchi</th>
                <th>Holat</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((l) => (
                <tr key={l.groupId}>
                  <td className="whitespace-nowrap font-mono text-slate-600">
                    {l.startTime}
                    {l.endTime ? `–${l.endTime}` : ''}
                    {l.moved && <div className="font-sans text-xs text-slate-400">ko'chirilgan dars</div>}
                  </td>
                  <td>
                    <Link to={`/admin/classes/${l.groupId}`} className="font-medium text-slate-800 no-underline hover:underline">
                      {l.groupName}
                    </Link>
                    <div className="text-xs text-slate-400">
                      {l.courseName}
                      {l.room ? ` · ${l.room}` : ''}
                    </div>
                  </td>
                  <td>
                    <div className="text-slate-700">{l.teacherName}</div>
                    {l.substituteTeacherName && (
                      <div className="text-xs text-amber-700">O'rinbosar: {l.substituteTeacherName}</div>
                    )}
                  </td>
                  <td>
                    {l.overdue ? (
                      <Badge tone="red">
                        <AlarmClock className="h-3 w-3" /> {phaseLabel(l)}
                      </Badge>
                    ) : (
                      <Badge tone="default">
                        <Clock3 className="h-3 w-3" /> {phaseLabel(l)}
                      </Badge>
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
