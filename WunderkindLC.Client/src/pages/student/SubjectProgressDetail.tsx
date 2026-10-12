import { useCallback } from 'react'
import { useParams } from 'react-router-dom'
import { BookOpen, CircleCheck, Clock, X } from 'lucide-react'
import { getStudentSubjectProgressDetail } from '@/api/services/studentPortal'
import { Ring, subjectColor } from '@/pages/student/lib'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, SkeletonList } from '@/pages/student/ui'

/* ============================================================
   Subject progress detail — lessons of one subject (conducted / missed / upcoming).
   ============================================================ */

const MONTHS_SHORT = ['Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn', 'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek']
function dateBlock(iso?: string) {
  if (!iso) return { d: '', m: '' }
  const dt = new Date(iso.length <= 10 ? iso + 'T00:00:00' : iso)
  if (isNaN(dt.getTime())) return { d: '', m: '' }
  return { d: String(dt.getDate()), m: MONTHS_SHORT[dt.getMonth()] }
}

export function SubjectProgressDetailScreen() {
  const { id = '' } = useParams<{ id: string }>()
  const load = useCallback(() => getStudentSubjectProgressDetail(id, 1), [id])
  const { data, error, loading, reload } = useApi(load)
  const col = subjectColor(id)

  return (
    <div className="screen">
      <PageHeader title={data?.subjectName || 'Fan'} back="/student/progress" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {error && !data ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !data ? (
            <SkeletonList rows={4} />
          ) : data ? (
            <>
              <div className="card row" style={{ gap: 16 }}>
                <Ring value={data.percent} max={100} size={88} stroke={10} color={col}>
                  <div className="st-num" style={{ fontSize: 22, lineHeight: 1, color: col }}>{data.percent}%</div>
                </Ring>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="st-num" style={{ fontSize: 24, lineHeight: 1.1 }}>
                    {data.conducted} / {data.planned}
                  </div>
                  <div className="muted" style={{ fontSize: 14, marginTop: 4 }}>dars o'tildi · {data.remaining} ta qoldi</div>
                </div>
              </div>

              <div className="st-sh">
                <div className="st-sh-title">Darslar</div>
              </div>
              {(data.lessons || []).length ? (
                <div className="card flush">
                  {data.lessons.map((l, i) => {
                    const state = l.conducted ? 'done' : l.isPast ? 'missed' : 'future'
                    const db = dateBlock(l.date)
                    return (
                      <div key={l.date + '_' + l.period + '_' + i} className="st-li" style={{ opacity: state === 'future' ? 0.7 : 1 }}>
                        <div style={{ width: 44, textAlign: 'center', flex: 'none' }}>
                          <div className="st-num" style={{ fontSize: 17 }}>{db.d}</div>
                          <div className="muted" style={{ fontSize: 12, fontWeight: 600 }}>{db.m}</div>
                        </div>
                        <div className="st-li-main">
                          <div className="st-li-title">{l.topic || `${l.period}-dars`}</div>
                          {l.homework && <div className="st-li-sub">Uy vazifa: {l.homework}</div>}
                        </div>
                        {state === 'done' ? (
                          <CircleCheck size={20} color="var(--green)" />
                        ) : state === 'missed' ? (
                          <X size={20} color="var(--red)" />
                        ) : (
                          <Clock size={20} color="var(--faint)" />
                        )}
                      </div>
                    )
                  })}
                </div>
              ) : (
                <div className="card flush">
                  <EmptyState icon={BookOpen} title="Dars yo'q" />
                </div>
              )}
            </>
          ) : null}
        </div>
      </div>
    </div>
  )
}
