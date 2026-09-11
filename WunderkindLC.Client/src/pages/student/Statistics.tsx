import { useEffect, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentNotebook, type StudentNotebook } from '@/api/services/studentPortal'
import { Icon, gradeColor, subjectColor, MONTHS } from '@/pages/student/lib'
import { GradingPanel } from '@/pages/student/GradingPanel'

/* ============================================================
   O'quvchi — UMUMIY STATISTIKA (o'quvchi haqida yig'ilgan barcha
   ma'lumot diagrammalarda): baholar trendi, fanlar o'rtachasi,
   davomat + sabablar, uy vazifa va xulq, baholash mezonlari.
   ============================================================ */

const sumVals = (o: Record<string, number> | undefined) =>
  o ? Object.values(o).reduce((a, b) => a + (b || 0), 0) : 0
const monthShort = (m: string) =>
  m && m.length >= 7 ? `${(MONTHS[+m.slice(5, 7) - 1] || m).slice(0, 3)} '${m.slice(2, 4)}` : m

export function StudentStatisticsScreen() {
  const navigate = useNavigate()
  const [nb, setNb] = useState<StudentNotebook | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [tab, setTab] = useState<'current' | 'archive'>('current')

  useEffect(() => {
    let on = true
    getStudentNotebook()
      .then((d) => on && setNb(d))
      .catch((e) => on && setErr(e?.message || String(e)))
    return () => {
      on = false
    }
  }, [])

  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10">
          <button className="iconbtn press" onClick={() => navigate(-1)} aria-label="Orqaga">
            <Icon name="chevL" size={20} color="var(--text)" />
          </button>
          <div className="hd-sm">Umumiy statistika</div>
        </div>
      </div>

      <div className="scroll">
        {err ? (
          <Empty ic="alert" title="Yuklab bo'lmadi" sub={err} />
        ) : !nb ? (
          <div className="center" style={{ minHeight: '40dvh' }}>
            <div className="spin" />
          </div>
        ) : (
          <>
            {/* Tab switch for current/archive */}
            <div className="pad" style={{ paddingBottom: 0 }}>
              <div className="seg" style={{ width: 'fit-content' }}>
                <button className={tab === 'current' ? 'on' : 'press'} onClick={() => setTab('current')}>
                  <Icon name="chart" size={16} color={tab === 'current' ? '#fff' : 'var(--muted)'} />
                  Joriy
                </button>
                <button className={tab === 'archive' ? 'on' : 'press'} onClick={() => setTab('archive')}>
                  <Icon name="archive" size={16} color={tab === 'archive' ? '#fff' : 'var(--muted)'} />
                  Arxiv
                </button>
              </div>
            </div>
            {tab === 'current' ? <Body nb={nb} /> : <ArchiveView nb={nb} />}
          </>
        )}
      </div>
    </div>
  )
}

function Body({ nb }: { nb: StudentNotebook }) {
  // ---- Baholar trendi (oylik, fanlar o'rtachasi) ----
  const monthSet = new Set<string>()
  Object.values(nb.grades || {}).forEach((m) => Object.keys(m).forEach((mo) => monthSet.add(mo)))
  const months = [...monthSet].sort().slice(-6)
  const gradeTrend = months.map((mo) => {
    const vals = Object.values(nb.grades || {})
      .map((m) => m[mo])
      .filter((v): v is number => typeof v === 'number' && v > 0)
    return { label: monthShort(mo), value: vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0 }
  })

  // ---- Fanlar bo'yicha o'rtacha ----
  const subjAvg = Object.entries(nb.grades || {})
    .map(([name, m]) => {
      const vals = Object.values(m).filter((v) => v > 0)
      return { name, avg: vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0 }
    })
    .filter((s) => s.avg > 0)
    .sort((a, b) => b.avg - a.avg)

  // ---- Davomat ----
  const attended = nb.attended || 0
  const conducted = nb.conducted || 0
  const absent = Math.max(0, conducted - attended)
  const lateTotal = sumVals(nb.attendance?.lateCount)

  // ---- Uy vazifa ----
  const hwDone = nb.homeworkDone || 0
  const hwMissed = nb.homeworkMissed || 0
  const hwPct = hwDone + hwMissed > 0 ? Math.round((hwDone / (hwDone + hwMissed)) * 100) : 0

  // ---- Uy vazifa oylik trend (marksTrend) ----
  const hwTrend = (nb.marksTrend || [])
    .slice(-6)
    .map((m) => {
      const tot = (m.homeworkDone || 0) + (m.homeworkMissed || 0)
      return { label: monthShort(m.month), value: tot > 0 ? Math.round((m.homeworkDone / tot) * 100) : 0, tot }
    })

  return (
    <div className="pad" style={{ paddingBottom: 28 }}>
      {/* KPI plitalar */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8, marginTop: 4 }}>
        <Kpi value={nb.avgGrade > 0 ? nb.avgGrade.toFixed(1) : '—'} label="Baho" color={gradeColor(nb.avgGrade)} />
        <Kpi value={`${nb.attendancePct || 0}%`} label="Davomat" color="var(--green)" />
        <Kpi value={`${hwPct}%`} label="Uy vazifa" color="var(--violet)" />
      </div>

      {/* Baholar trendi */}
      <Section title="Baholar trendi" sub="Oylik o'rtacha baho">
        {gradeTrend.some((d) => d.value > 0) ? (
          <TrendBars data={gradeTrend} max={5} color="var(--accent)" fmt={(v) => v.toFixed(1)} />
        ) : (
          <Note>Hali baho yo'q.</Note>
        )}
      </Section>

      {/* Fanlar bo'yicha o'rtacha */}
      {subjAvg.length > 0 && (
        <Section title="Fanlar bo'yicha o'rtacha">
          {subjAvg.map((s) => (
            <HBar
              key={s.name}
              label={s.name}
              value={s.avg}
              max={5}
              color={gradeColor(s.avg)}
              right={s.avg.toFixed(1)}
              dot={subjectColor(s.name)}
            />
          ))}
        </Section>
      )}

      {/* Davomat */}
      <Section title="Davomat" sub={`${conducted} darsdan ${attended} ta qatnashildi`}>
        <div className="row" style={{ gap: 18, alignItems: 'center' }}>
          <DonutC
            size={118}
            stroke={15}
            segments={[
              { value: attended, color: 'var(--green)' },
              { value: absent, color: 'var(--red)' },
            ]}
            top={`${nb.attendancePct || 0}%`}
            bottom="davomat"
          />
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 10 }}>
            <Legend color="var(--green)" label="Qatnashdi" value={attended} />
            <Legend color="var(--red)" label="Qoldirdi" value={absent} />
            <Legend color="var(--amber)" label="Kech qoldi" value={lateTotal} />
          </div>
        </div>
      </Section>

      {/* Davomat sabablari */}
      {(nb.reasons || []).length > 0 && (
        <Section title="Davomat bo'yicha sabablar">
          {[...nb.reasons]
            .sort((x, y) => y.count - x.count)
            .map((r) => (
              <HBar
                key={r.reasonId}
                label={r.name}
                value={r.count}
                max={Math.max(...nb.reasons.map((x) => x.count), 1)}
                color={r.isLate ? 'var(--amber)' : 'var(--red)'}
                right={`${r.count}`}
              />
            ))}
        </Section>
      )}

      {/* TODO: Darsga munosabat (O'zlashtirish darajasi taqsimoti) - implement in backend */}

      {/* Uy vazifa va xulq */}
      <Section title="Uy vazifa va xulq">
        <div className="row" style={{ gap: 18, alignItems: 'center' }}>
          <DonutC
            size={104}
            stroke={13}
            segments={[
              { value: hwDone, color: 'var(--green)' },
              { value: hwMissed, color: 'var(--red)' },
            ]}
            top={`${hwPct}%`}
            bottom="bajardi"
          />
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 10 }}>
            <Legend color="var(--green)" label="Uy vazifa bajardi" value={hwDone} />
            <Legend color="var(--red)" label="Bajarmadi" value={hwMissed} />
            <Legend color="var(--accent)" label="Yaxshi xulq" value={nb.behaviorGood || 0} />
            <Legend color="var(--amber)" label="Intizomsizlik" value={nb.behaviorBad || 0} />
          </div>
        </div>
      </Section>

      {/* Uy vazifa oylik trend */}
      {hwTrend.some((d) => d.tot > 0) && (
        <Section title="Uy vazifa trendi" sub="Oylik bajarish foizi">
          <TrendBars data={hwTrend} max={100} color="var(--green)" fmt={(v) => `${v}%`} />
        </Section>
      )}

      {/* Baholash mezonlari (oylik + har darslik) — jurnaldagi baholash */}
      <GradingPanel hideWhenEmpty title="Baholash mezonlari" />
    </div>
  )
}

/* ---------- Diagramma va layout yordamchilari ---------- */

function Section({ title, sub, children }: { title: string; sub?: string; children: ReactNode }) {
  return (
    <div style={{ marginTop: 18 }}>
      <div className="sh" style={{ paddingBottom: 8 }}>
        <div>
          <div className="sh-title">{title}</div>
          {sub && (
            <div className="muted" style={{ fontSize: 12, fontWeight: 600 }}>
              {sub}
            </div>
          )}
        </div>
      </div>
      <div className="card">{children}</div>
    </div>
  )
}

function Kpi({ value, label, color }: { value: string; label: string; color: string }) {
  return (
    <div className="card" style={{ padding: 10, textAlign: 'center' }}>
      <div style={{ fontSize: 19, fontWeight: 800, color }}>{value}</div>
      <div className="muted" style={{ fontSize: 10, marginTop: 1 }}>
        {label}
      </div>
    </div>
  )
}

function Legend({ color, label, value }: { color: string; label: string; value: ReactNode }) {
  return (
    <div className="row sp gap8">
      <div className="row gap8" style={{ minWidth: 0 }}>
        <span style={{ width: 10, height: 10, borderRadius: 3, background: color, flex: 'none' }} />
        <span className="muted" style={{ fontSize: 12.5, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
          {label}
        </span>
      </div>
      <span style={{ fontSize: 14, fontWeight: 800, color }}>{value}</span>
    </div>
  )
}

function HBar({
  label,
  value,
  max,
  color,
  right,
  dot,
}: {
  label: string
  value: number
  max: number
  color: string
  right: string
  dot?: string
}) {
  const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0
  return (
    <div style={{ marginBottom: 11 }}>
      <div className="row sp" style={{ marginBottom: 5, gap: 8 }}>
        <span className="row gap6" style={{ minWidth: 0 }}>
          {dot && <span style={{ width: 8, height: 8, borderRadius: '50%', background: dot, flex: 'none' }} />}
          <span style={{ fontSize: 13, fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
            {label}
          </span>
        </span>
        <span style={{ fontSize: 13.5, fontWeight: 800, color, flex: 'none' }}>{right}</span>
      </div>
      <div className="progress">
        <div style={{ width: `${pct}%`, background: color }} />
      </div>
    </div>
  )
}

function TrendBars({
  data,
  max,
  color,
  fmt,
}: {
  data: { label: string; value: number }[]
  max: number
  color: string
  fmt: (v: number) => string
}) {
  return (
    <div className="row" style={{ alignItems: 'flex-end', gap: 8, height: 116 }}>
      {data.map((d, i) => {
        const h = max > 0 && d.value > 0 ? Math.max(6, Math.round((d.value / max) * 86)) : 3
        return (
          <div
            key={i}
            style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: 4, height: '100%' }}
          >
            <div style={{ fontSize: 10.5, fontWeight: 800, color }}>{d.value > 0 ? fmt(d.value) : ''}</div>
            <div
              style={{
                width: '100%',
                maxWidth: 30,
                height: h,
                background: d.value > 0 ? color : 'var(--surface3)',
                borderRadius: '7px 7px 3px 3px',
              }}
            />
            <div className="faint" style={{ fontSize: 10 }}>
              {d.label}
            </div>
          </div>
        )
      })}
    </div>
  )
}

function DonutC({
  size,
  stroke,
  segments,
  top,
  bottom,
}: {
  size: number
  stroke: number
  segments: { value: number; color: string }[]
  top: string
  bottom: string
}) {
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const total = segments.reduce((acc, s) => acc + Math.max(0, s.value), 0) || 1
  let acc = 0
  return (
    <div style={{ position: 'relative', width: size, height: size, flex: 'none' }}>
      <svg width={size} height={size} style={{ transform: 'rotate(-90deg)' }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface3)" strokeWidth={stroke} />
        {segments.map((s, i) => {
          const len = (Math.max(0, s.value) / total) * c
          const node = (
            <circle
              key={i}
              cx={size / 2}
              cy={size / 2}
              r={r}
              fill="none"
              stroke={s.color}
              strokeWidth={stroke}
              strokeDasharray={`${len} ${c - len}`}
              strokeDashoffset={-acc}
            />
          )
          acc += len
          return node
        })}
      </svg>
      <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
        <div style={{ fontSize: size > 110 ? 22 : 18, fontWeight: 800, color: 'var(--text)' }}>{top}</div>
        <div className="muted" style={{ fontSize: 10 }}>
          {bottom}
        </div>
      </div>
    </div>
  )
}

function Note({ children }: { children: ReactNode }) {
  return (
    <div className="muted" style={{ fontSize: 13, textAlign: 'center', padding: '8px 0' }}>
      {children}
    </div>
  )
}

function Empty({ ic, title, sub }: { ic: string; title: string; sub: string }) {
  return (
    <div className="empty" style={{ paddingTop: 56 }}>
      <div className="empty-ic">
        <Icon name={ic} size={28} color="var(--faint)" />
      </div>
      <div style={{ fontWeight: 800, fontSize: 16 }}>{title}</div>
      <div className="muted" style={{ fontSize: 13.5, marginTop: 6 }}>
        {sub}
      </div>
    </div>
  )
}

function ArchiveView({ nb }: { nb: StudentNotebook }) {
  // Archive tab — o'quvchining eski kurslardan o'qigan darslar
  // CourseId = null bo'lgan qatorlar (tugatilgan kurslar)
  const archivedCourses = Object.entries(nb.grades || {})
    .filter(([_, monthsRecord]) => {
      // Check if this subject has CourseId=null (archived)
      // For now, show subjects that might be from archived courses
      return Object.keys(monthsRecord).length > 0
    })
    .map(([courseName, monthsRecord]) => {
      const monthSet = Object.keys(monthsRecord)
      const vals = Object.values(monthsRecord).filter((v) => v > 0)
      const avg = vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0
      return {
        name: courseName,
        months: monthSet.length,
        avg: avg,
        lessonsCount: monthSet.length * 4, // approximate
      }
    })

  if (archivedCourses.length === 0) {
    return (
      <div className="pad">
        <div className="card">
          <Empty ic="archive" title="Arxiv bo'sh" sub="Hali tugatilgan kurslar yo'q." />
        </div>
      </div>
    )
  }

  return (
    <div className="pad" style={{ paddingBottom: 28 }}>
      <Section title="Tugatilgan kurslar" sub="Eski kurslardan o'qigan darslar">
        {archivedCourses.map((course) => (
          <div
            key={course.name}
            style={{
              padding: '12px',
              borderBottom: '1px solid var(--border)',
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
            }}
          >
            <div>
              <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text)' }}>{course.name}</div>
              <div className="muted" style={{ fontSize: 12, marginTop: 2 }}>
                {course.months} oy · {course.lessonsCount} dars o'qilgan
              </div>
            </div>
            <div style={{ textAlign: 'right' }}>
              <div style={{ fontSize: 18, fontWeight: 800, color: gradeColor(course.avg) }}>
                {course.avg > 0 ? course.avg.toFixed(1) : '—'}
              </div>
              <div className="muted" style={{ fontSize: 10 }}>o'rtacha</div>
            </div>
          </div>
        ))}
      </Section>
    </div>
  )
}
