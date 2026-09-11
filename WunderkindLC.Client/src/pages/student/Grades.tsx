import { useEffect, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentGrades, type StudentGradesReport } from '@/api/services/studentPortal'
import { Icon, Ring, gradeColor, subjectColor, subjInitial } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — Baholar ekrani (chorak yo'q, joriy baho).
   ============================================================ */

function BackHeader({ title, onBack }: { title: string; onBack: () => void }) {
  return (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={onBack}>
          <Icon name="chevL" size={22} />
        </button>
        <div className="hd-sm" style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {title}
        </div>
      </div>
    </div>
  )
}

function gradeChip(grade: number, size = 36): ReactNode {
  const col = gradeColor(grade)
  const label = grade === Math.round(grade) ? String(Math.round(grade)) : grade.toFixed(1)
  return (
    <div
      className="badge"
      style={{
        width: size,
        height: size,
        borderRadius: size * 0.32,
        background: `color-mix(in srgb,${col} 16%,var(--surface))`,
        color: col,
        fontSize: size * 0.5,
      }}
    >
      {label}
    </div>
  )
}

export function StudentGradesScreen() {
  const navigate = useNavigate()
  const [report, setReport] = useState<StudentGradesReport | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let on = true
    getStudentGrades()
      .then((d) => on && setReport(d))
      .catch((e) => on && setErr(e?.message || String(e)))
    return () => {
      on = false
    }
  }, [])

  if (err) {
    return (
      <div className="screen">
        <BackHeader title="Baholar" onBack={() => navigate(-1)} />
        <div className="center">
          <Empty title="Yuklab bo'lmadi" sub={err} ic="alert" />
        </div>
      </div>
    )
  }
  if (!report) {
    return (
      <div className="screen">
        <BackHeader title="Baholar" onBack={() => navigate(-1)} />
        <div className="center">
          <div className="spin" />
        </div>
      </div>
    )
  }

  const subjects = report.subjects || []
  const rows = subjects
    .map((s) => ({ s, g: report.grades?.[s.id]?.[1] as number | undefined }))
    .filter((r) => r.g != null)
  const vals = rows.map((r) => r.g as number)
  const avg = vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0
  const fives = vals.filter((v) => v >= 4.5).length

  const sumStat = (ic: string, color: string, value: string, label: string, bg: string) => (
    <div className="row gap8">
      <div
        style={{
          width: 34,
          height: 34,
          borderRadius: 10,
          background: bg,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Icon name={ic} size={18} color={color} />
      </div>
      <div>
        <div style={{ fontSize: 17, fontWeight: 800 }}>{value}</div>
        <div className="muted" style={{ fontSize: 11.5 }}>
          {label}
        </div>
      </div>
    </div>
  )

  return (
    <div className="screen">
      <BackHeader title="Baholar" onBack={() => navigate(-1)} />
      <div className="scroll pad" style={{ paddingBottom: 24 }}>
        <div className="card row" style={{ gap: 18 }}>
          <Ring value={avg / 5 * 100} max={100} size={92} stroke={11} color={gradeColor(avg)}>
            <div style={{ fontSize: 27, fontWeight: 800, lineHeight: 1, color: gradeColor(avg) }}>
              {avg.toFixed(2)}
            </div>
            <div className="muted" style={{ fontSize: 10.5, fontWeight: 700 }}>
              o'rtacha
            </div>
          </Ring>
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 10 }}>
            {sumStat('award', 'var(--green)', `${fives} ta`, '"5" baho', 'var(--greenSoft)')}
            {sumStat('book', 'var(--accent)', `${rows.length} ta`, 'fan', 'var(--accentSoft)')}
          </div>
        </div>

        <div style={{ height: 14 }} />
        <div className="sh">
          <div className="sh-title">Fanlar bo'yicha</div>
        </div>

        {rows.length ? (
          <div className="card" style={{ padding: 4 }}>
            {subjects.map((s, i) => {
              const col = subjectColor(s.id)
              const cur = report.grades?.[s.id]?.[1]
              return (
                <div
                  key={s.id}
                  className="row gap12"
                  style={{ padding: 11, borderBottom: i < subjects.length - 1 ? '1px solid var(--border)' : undefined }}
                >
                  <div
                    className="subj"
                    style={{
                      width: 40,
                      height: 40,
                      borderRadius: 13,
                      flex: 'none',
                      background: col + '22',
                      color: col,
                      fontSize: 17,
                    }}
                  >
                    {subjInitial(s.name)}
                  </div>
                  <div style={{ flex: 1 }}>
                    <div style={{ fontSize: 15, fontWeight: 700 }}>{s.name}</div>
                  </div>
                  {cur != null ? (
                    gradeChip(cur, 36)
                  ) : (
                    <span className="faint" style={{ fontSize: 14, fontWeight: 700 }}>
                      –
                    </span>
                  )}
                </div>
              )
            })}
          </div>
        ) : (
          <div className="card">
            <Empty title="Baholar yo'q" sub="Hozircha baho qo'yilmagan." ic="chart" />
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
