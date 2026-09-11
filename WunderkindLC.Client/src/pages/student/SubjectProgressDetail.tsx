import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  getStudentSubjectProgressDetail,
  type SubjectProgressDetail,
} from '@/api/services/studentPortal'
import { Icon, Ring, subjectColor } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — fan progresi (darslar) detali.
   ============================================================ */

const MONTHS_SHORT = ['Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn', 'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek']
function dateBlock(iso?: string) {
  if (!iso) return { d: '', m: '' }
  const dt = new Date(iso.length <= 10 ? iso + 'T00:00:00' : iso)
  if (isNaN(dt.getTime())) return { d: '', m: '' }
  return { d: String(dt.getDate()), m: MONTHS_SHORT[dt.getMonth()] }
}

export function SubjectProgressDetailScreen() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [data, setData] = useState<SubjectProgressDetail | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let on = true
    if (!id) return
    getStudentSubjectProgressDetail(id, 1)
      .then((d) => on && setData(d))
      .catch((e) => on && setErr(e?.message || String(e)))
    return () => {
      on = false
    }
  }, [id])

  const col = subjectColor(id || '')

  const head = (title: string) => (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div
          className="hd-sm"
          style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
        >
          {title}
        </div>
      </div>
    </div>
  )

  if (err) {
    return (
      <div className="screen">
        {head('Fan')}
        <div className="center">
          <Empty title="Yuklab bo'lmadi" sub={err} ic="alert" />
        </div>
      </div>
    )
  }
  if (!data) {
    return (
      <div className="screen">
        {head('Fan')}
        <div className="center">
          <div className="spin" />
        </div>
      </div>
    )
  }

  return (
    <div className="screen">
      {head(data.subjectName || 'Fan')}
      <div className="scroll" style={{ paddingBottom: 24 }}>
        <div className="pad">
          <div className="card row" style={{ gap: 18 }}>
            <Ring value={data.percent} max={100} size={92} stroke={11} color={col}>
              <div style={{ fontSize: 24, fontWeight: 800, lineHeight: 1, color: col }}>{data.percent}%</div>
              <div className="muted" style={{ fontSize: 10.5, fontWeight: 700 }}>
                o'zlashtirildi
              </div>
            </Ring>
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 24, fontWeight: 800, lineHeight: 1 }}>
                {data.conducted} / {data.planned}
              </div>
              <div className="muted" style={{ fontSize: 12.5, marginTop: 4 }}>
                {data.remaining} qoldi
              </div>
            </div>
          </div>

          <div style={{ height: 16 }} />
          <div className="sh">
            <div className="sh-title">Darslar</div>
          </div>

          {(data.lessons || []).length ? (
            <div className="card" style={{ padding: 4 }}>
              {data.lessons.map((l, i) => {
                const state = l.conducted ? 'done' : l.isPast ? 'missed' : 'future'
                const dot =
                  state === 'done' ? 'var(--green)' : state === 'missed' ? 'var(--red)' : 'var(--borderStrong)'
                const db = dateBlock(l.date)
                return (
                  <div
                    key={l.date + '_' + l.period + '_' + i}
                    className="row gap12"
                    style={{ padding: 11, borderBottom: i < data.lessons.length - 1 ? '1px solid var(--border)' : undefined }}
                  >
                    <div style={{ width: 10, height: 10, borderRadius: '50%', background: dot, flex: 'none' }} />
                    <div style={{ width: 46, textAlign: 'center' }}>
                      <div style={{ fontSize: 15, fontWeight: 800 }}>{db.d}</div>
                      <div className="faint" style={{ fontSize: 10.5 }}>
                        {db.m}
                      </div>
                    </div>
                    <div style={{ flex: 1, opacity: state === 'future' ? 0.6 : 1 }}>
                      <div style={{ fontSize: 14, fontWeight: 700 }}>{l.topic || `${l.period}-dars`}</div>
                      {l.homework && (
                        <div className="muted" style={{ fontSize: 12 }}>
                          Uy vazifa: {l.homework}
                        </div>
                      )}
                    </div>
                    {state === 'done' ? (
                      <Icon name="checkCircle" size={20} color="var(--green)" />
                    ) : state === 'missed' ? (
                      <Icon name="x" size={20} color="var(--red)" />
                    ) : (
                      <Icon name="clock" size={20} color="var(--faint)" />
                    )}
                  </div>
                )
              })}
            </div>
          ) : (
            <div className="card">
              <Empty title="Dars yo'q" ic="book" />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function Empty({ title, sub, ic = 'sparkle' }: { title: string; sub?: string; ic?: string }) {
  return (
    <div className="empty">
      <div className="empty-ic">
        <Icon name={ic} size={30} />
      </div>
      <div style={{ fontSize: 16, fontWeight: 800 }}>{title}</div>
      {sub && (
        <div className="muted" style={{ fontSize: 13.5, marginTop: 6, lineHeight: 1.5 }}>
          {sub}
        </div>
      )}
    </div>
  )
}
