import { useMemo } from 'react'
import { CalendarDays, MapPin, UserRound } from 'lucide-react'
import { getStudentPortalGroups } from '@/api/services/studentPortal'
import { DAY_NAMES, courseTitle, liveGroups, nextLesson, weekdayIndex } from '@/pages/student/format'
import { useApi, useNow } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, Skeleton, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Darslar → JADVAL (weekly timetable, starting from today).
   Only groups the student studies in now (active + trial) — frozen and finished groups never
   appear. Weekdays 0=Du … 6=Ya (server `Group.Days`). The next/ongoing lesson is highlighted.
   ============================================================ */

const loadGroups = () => getStudentPortalGroups()

export function StudentScheduleScreen() {
  const { data, error, loading, reload } = useApi(loadGroups)
  const now = useNow()
  const today = weekdayIndex(now)
  const nowHm = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`

  // Day → lessons that day (by time), starting with today.
  const byDay = useMemo(() => {
    const live = liveGroups(data ?? [])
    return [0, 1, 2, 3, 4, 5, 6]
      .map((i) => (today + i) % 7)
      .map((day, offset) => ({
        day,
        offset,
        lessons: live.filter((g) => (g.days || []).includes(day)).sort((a, b) => (a.startTime || '').localeCompare(b.startTime || '')),
      }))
      .filter((d) => d.lessons.length > 0)
  }, [data, today])
  const next = useMemo(() => (data ? nextLesson(data, now) : null), [data, now])

  return (
    <div className="screen">
      <PageHeader section="lessons" />
      <div className="scroll">
        <div className="st-page">
          {error && !data ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !data ? (
            [0, 1].map((i) => (
              <div key={i} style={{ marginBottom: 18 }}>
                <Skeleton h={16} w={120} style={{ marginBottom: 10 }} />
                <div className="card row gap12">
                  <Skeleton h={40} w={56} />
                  <div style={{ flex: 1 }}>
                    <Skeleton h={15} w="70%" />
                    <Skeleton h={12} w="50%" style={{ marginTop: 8 }} />
                  </div>
                </div>
              </div>
            ))
          ) : byDay.length === 0 ? (
            <div className="card flush">
              <EmptyState icon={CalendarDays} title="Dars jadvali yo'q" sub="Faol guruh yo'q yoki guruh vaqti hali kiritilmagan." />
            </div>
          ) : (
            byDay.map(({ day, offset, lessons }) => (
              <div key={day} style={{ marginBottom: 18 }}>
                <div className="row gap8" style={{ margin: '4px 2px 10px' }}>
                  <div style={{ fontSize: 16, fontWeight: 800 }}>{DAY_NAMES[day]}</div>
                  {offset === 0 && <StatusChip tone="info">Bugun</StatusChip>}
                  {offset === 1 && <StatusChip>Ertaga</StatusChip>}
                </div>
                <div className="col" style={{ gap: 10 }}>
                  {lessons.map((g) => {
                    const isNext = !!next && next.group.groupId === g.groupId && next.day === day && next.dayOffset === offset
                    // Today's lessons that are already over are dimmed.
                    const over = offset === 0 && !isNext && !!g.endTime && g.endTime <= nowHm
                    return (
                      <div
                        key={g.groupId}
                        className="card"
                        style={{
                          display: 'flex',
                          gap: 14,
                          padding: 14,
                          borderColor: isNext ? 'color-mix(in srgb, var(--accent) 45%, var(--border))' : undefined,
                          boxShadow: isNext ? '0 0 0 3px var(--accentSoft)' : undefined,
                          opacity: over ? 0.6 : 1,
                        }}
                      >
                        <div style={{ flex: 'none', width: 58, paddingRight: 12, borderRight: '1px solid var(--border)' }}>
                          <div className="st-num" style={{ fontSize: 17 }}>{g.startTime || '—'}</div>
                          {g.endTime && <div className="muted st-num" style={{ fontSize: 13.5, fontWeight: 600 }}>{g.endTime}</div>}
                        </div>
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div className="row gap8" style={{ alignItems: 'flex-start', justifyContent: 'space-between' }}>
                            <div className="st-wrap" style={{ fontSize: 15.5, fontWeight: 700, lineHeight: 1.3 }}>{courseTitle(g)}</div>
                            {isNext && <StatusChip tone="info">{next?.status === 'now' ? 'Hozir' : 'Keyingi'}</StatusChip>}
                            {over && <StatusChip>Tugadi</StatusChip>}
                            {!isNext && !over && g.state === 'trial' && <StatusChip tone="violet">Sinov</StatusChip>}
                          </div>
                          <div className="row" style={{ flexWrap: 'wrap', gap: '4px 14px', marginTop: 6, fontSize: 13.5, color: 'var(--muted)' }}>
                            {g.teacherName && (
                              <span className="row gap6">
                                <UserRound size={15} /> {g.teacherName}
                              </span>
                            )}
                            {g.room && (
                              <span className="row gap6">
                                <MapPin size={15} /> {g.room}
                              </span>
                            )}
                          </div>
                          {isNext && g.state === 'trial' && (
                            <div style={{ marginTop: 6 }}>
                              <StatusChip tone="violet">Sinov darsi</StatusChip>
                            </div>
                          )}
                        </div>
                      </div>
                    )
                  })}
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
