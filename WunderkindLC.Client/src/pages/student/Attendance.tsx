import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentAttendance, type StudentAttendanceFull } from '@/api/services/studentPortal'
import { Icon, subjectColor, subjInitial } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — Davomat ekrani (chorak yo'q).
   ============================================================ */

const MONTHS_SHORT = ['Yan', 'Fev', 'Mar', 'Apr', 'May', 'Iyn', 'Iyl', 'Avg', 'Sen', 'Okt', 'Noy', 'Dek']
function dateBlock(iso?: string) {
  if (!iso) return { d: '', m: '' }
  const dt = new Date(iso.length <= 10 ? iso + 'T00:00:00' : iso)
  if (isNaN(dt.getTime())) return { d: '', m: '' }
  return { d: String(dt.getDate()), m: MONTHS_SHORT[dt.getMonth()] }
}

export function StudentAttendanceScreen() {
  const navigate = useNavigate()
  const [data, setData] = useState<StudentAttendanceFull | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let on = true
    getStudentAttendance(1)
      .then((d) => on && setData(d))
      .catch((e) => on && setErr(e?.message || String(e)))
    return () => {
      on = false
    }
  }, [])

  const head = (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div className="hd-sm" style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          Davomat
        </div>
      </div>
    </div>
  )

  if (err) {
    return (
      <div className="screen">
        {head}
        <div className="center">
          <Empty title="Yuklab bo'lmadi" sub={err} ic="alert" />
        </div>
      </div>
    )
  }
  if (!data) {
    return (
      <div className="screen">
        {head}
        <div className="center">
          <div className="spin" />
        </div>
      </div>
    )
  }

  const a = data.summary || ({} as StudentAttendanceFull['summary'])
  const stats = [
    { label: 'Dars qoldirildi', val: a.missedLessons?.[1] || 0, ic: 'alert', color: 'var(--red)' },
    { label: 'Kasallik', val: a.illnessDays?.[1] || 0, ic: 'info', color: 'var(--amber)' },
    { label: 'Kech qoldi', val: a.lateCount?.[1] || 0, ic: 'clock', color: 'var(--accent)' },
  ]
  const rows = data.rows || []

  return (
    <div className="screen">
      {head}
      <div className="scroll pad" style={{ paddingBottom: 24 }}>
        <div className="row gap10">
          {stats.map((s) => (
            <div key={s.label} className="card" style={{ flex: 1, padding: 13, borderRadius: 18 }}>
              <div
                style={{
                  width: 32,
                  height: 32,
                  borderRadius: 10,
                  background: `color-mix(in srgb,${s.color} 13%,transparent)`,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <Icon name={s.ic} size={17} color={s.color} />
              </div>
              <div style={{ fontSize: 22, fontWeight: 800, lineHeight: 1, marginTop: 6, color: s.color }}>
                {s.val}
              </div>
              <div style={{ fontSize: 11, fontWeight: 700, lineHeight: 1.2, marginTop: 4 }}>{s.label}</div>
            </div>
          ))}
        </div>

        <div style={{ height: 14 }} />
        <div className="sh">
          <div className="sh-title">Davomat tarixi</div>
        </div>

        {rows.length ? (
          <div className="card" style={{ padding: 4 }}>
            {rows.map((r, i) => {
              const col = subjectColor(r.subjectId)
              const rc = r.isLate ? 'var(--accent)' : r.isIll ? 'var(--amber)' : 'var(--red)'
              const db = dateBlock(r.date)
              return (
                <div
                  key={r.date + '_' + r.period + '_' + i}
                  className="row gap12"
                  style={{ padding: 11, borderBottom: i < rows.length - 1 ? '1px solid var(--border)' : undefined }}
                >
                  <div style={{ width: 46, textAlign: 'center' }}>
                    <div style={{ fontSize: 16, fontWeight: 800 }}>{db.d}</div>
                    <div className="faint" style={{ fontSize: 10.5 }}>
                      {db.m}
                    </div>
                  </div>
                  <div
                    className="subj"
                    style={{
                      width: 36,
                      height: 36,
                      borderRadius: 13,
                      flex: 'none',
                      background: col + '22',
                      color: col,
                      fontSize: 15,
                    }}
                  >
                    {subjInitial(r.subjectName)}
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontSize: 14.5, fontWeight: 700 }}>{r.subjectName}</div>
                    <div className="muted" style={{ fontSize: 12 }}>
                      {r.period}-dars
                    </div>
                  </div>
                  <span
                    className="chip"
                    style={{ color: rc, background: `color-mix(in srgb,${rc} 12%,transparent)`, fontSize: 11 }}
                  >
                    {r.reasonName || ''}
                  </span>
                </div>
              )
            })}
          </div>
        ) : (
          <div className="card">
            <Empty title="Ajoyib davomat!" sub="Qoldirilgan dars yo'q." ic="checkCircle" />
          </div>
        )}
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
