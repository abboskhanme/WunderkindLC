import { useState, type ReactNode } from 'react'
import { Archive, ChartColumn } from 'lucide-react'
import { getStudentNotebook, type StudentNotebook } from '@/api/services/studentPortal'
import { gradeColor, subjectColor, MONTHS } from '@/pages/student/lib'
import { GradingPanel } from '@/pages/student/GradingPanel'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, Skeleton } from '@/pages/student/ui'

/* ============================================================
   Natijalar → STATISTIKA — everything collected about the student, as charts: grade trend,
   subject averages, attendance + reasons, homework & behaviour, grading criteria.
   ============================================================ */

const loadNotebook = () => getStudentNotebook()

const sumVals = (o: Record<string, number> | undefined) => (o ? Object.values(o).reduce((a, b) => a + (b || 0), 0) : 0)
const monthShort = (m: string) => (m && m.length >= 7 ? `${(MONTHS[+m.slice(5, 7) - 1] || m).slice(0, 3)} '${m.slice(2, 4)}` : m)

export function StudentStatisticsScreen() {
  const { data: nb, error, loading, reload } = useApi(loadNotebook)
  const [tab, setTab] = useState<'current' | 'archive'>('current')

  return (
    <div className="screen">
      <PageHeader section="results" />
      <div className="scroll">
        {error && !nb ? (
          <ErrorState message={error} onRetry={reload} />
        ) : loading && !nb ? (
          <div className="st-page">
            <div className="st-kpis">
              {[0, 1, 2].map((i) => (
                <div key={i} className="st-kpi">
                  <Skeleton h={22} w="55%" />
                  <Skeleton h={12} w="70%" style={{ marginTop: 8 }} />
                </div>
              ))}
            </div>
            <Skeleton h={150} r={18} style={{ marginTop: 22 }} />
          </div>
        ) : nb ? (
          <div className="st-page">
            <div className="st-pills" role="tablist" aria-label="Davr">
              <button role="tab" aria-selected={tab === 'current'} className={'pill press' + (tab === 'current' ? ' on' : '')} onClick={() => setTab('current')}>
                <ChartColumn size={16} />
                Joriy
              </button>
              <button role="tab" aria-selected={tab === 'archive'} className={'pill press' + (tab === 'archive' ? ' on' : '')} onClick={() => setTab('archive')}>
                <Archive size={16} />
                Arxiv
              </button>
            </div>
            {tab === 'current' ? <Body nb={nb} /> : <ArchiveView nb={nb} />}
          </div>
        ) : null}
      </div>
    </div>
  )
}

function Body({ nb }: { nb: StudentNotebook }) {
  // ---- Grade trend (monthly, average over subjects) ----
  const monthSet = new Set<string>()
  Object.values(nb.grades || {}).forEach((m) => Object.keys(m).forEach((mo) => monthSet.add(mo)))
  const months = [...monthSet].sort().slice(-6)
  const gradeTrend = months.map((mo) => {
    const vals = Object.values(nb.grades || {})
      .map((m) => m[mo])
      .filter((v): v is number => typeof v === 'number' && v > 0)
    return { label: monthShort(mo), value: vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0 }
  })

  // ---- Average per subject ----
  const subjAvg = Object.entries(nb.grades || {})
    .map(([name, m]) => {
      const vals = Object.values(m).filter((v) => v > 0)
      return { name, avg: vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0 }
    })
    .filter((s) => s.avg > 0)
    .sort((a, b) => b.avg - a.avg)

  // ---- Attendance ----
  const attended = nb.attended || 0
  const conducted = nb.conducted || 0
  const absent = Math.max(0, conducted - attended)
  const lateTotal = sumVals(nb.attendance?.lateCount)

  // ---- Homework & behaviour ----
  const hwDone = nb.homeworkDone || 0
  const hwMissed = nb.homeworkMissed || 0
  const hwTotal = hwDone + hwMissed
  const hwPct = hwTotal > 0 ? Math.round((hwDone / hwTotal) * 100) : 0
  const hasMarks = hwTotal + (nb.behaviorGood || 0) + (nb.behaviorBad || 0) > 0

  const hwTrend = (nb.marksTrend || []).slice(-6).map((m) => {
    const tot = (m.homeworkDone || 0) + (m.homeworkMissed || 0)
    return { label: monthShort(m.month), value: tot > 0 ? Math.round((m.homeworkDone / tot) * 100) : 0, tot }
  })

  return (
    <>
      <div className="st-kpis" style={{ marginTop: 14 }}>
        <Kpi value={nb.avgGrade > 0 ? nb.avgGrade.toFixed(1) : '—'} label="O'rtacha baho" color={nb.avgGrade > 0 ? gradeColor(nb.avgGrade) : undefined} />
        <Kpi value={conducted > 0 ? `${nb.attendancePct || 0}%` : '—'} label="Davomat" color={conducted > 0 ? 'var(--green)' : undefined} />
        <Kpi value={hwTotal > 0 ? `${hwPct}%` : '—'} label="Uy vazifa" color={hwTotal > 0 ? 'var(--violet)' : undefined} />
      </div>

      <Section title="Baholar trendi" sub="Oylik o'rtacha baho">
        {gradeTrend.some((d) => d.value > 0) ? (
          <TrendBars data={gradeTrend} max={5} color="var(--accent)" fmt={(v) => v.toFixed(1)} />
        ) : (
          <Note>Hali baho qo'yilmagan.</Note>
        )}
      </Section>

      {subjAvg.length > 0 && (
        <Section title="Fanlar bo'yicha o'rtacha">
          {subjAvg.map((s) => (
            <HBar key={s.name} label={s.name} value={s.avg} max={5} color={gradeColor(s.avg)} right={s.avg.toFixed(1)} dot={subjectColor(s.name)} />
          ))}
        </Section>
      )}

      <Section title="Davomat" sub={conducted > 0 ? `${conducted} ta darsdan ${attended} tasida qatnashdi` : undefined}>
        {conducted > 0 ? (
          <div className="row" style={{ gap: 18 }}>
            <Donut
              size={112}
              stroke={14}
              segments={[
                { value: attended, color: 'var(--greenBar)' },
                { value: absent, color: 'var(--red)' },
              ]}
              top={`${nb.attendancePct || 0}%`}
              bottom="davomat"
            />
            <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
              <Legend color="var(--greenBar)" label="Qatnashdi" value={attended} />
              <Legend color="var(--red)" label="Qoldirdi" value={absent} />
              <Legend color="var(--amberBar)" label="Kech qoldi" value={lateTotal} />
            </div>
          </div>
        ) : (
          <Note>Hali dars o'tilmagan.</Note>
        )}
      </Section>

      {(nb.reasons || []).length > 0 && (
        <Section title="Qoldirish sabablari">
          {[...nb.reasons]
            .sort((x, y) => y.count - x.count)
            .map((r) => (
              <HBar
                key={r.reasonId}
                label={r.name}
                value={r.count}
                max={Math.max(...nb.reasons.map((x) => x.count), 1)}
                color={r.isLate ? 'var(--amberBar)' : 'var(--red)'}
                right={`${r.count}`}
              />
            ))}
        </Section>
      )}

      <Section title="Uy vazifa va xulq">
        {hasMarks ? (
          <div className="row" style={{ gap: 18 }}>
            <Donut
              size={104}
              stroke={13}
              segments={[
                { value: hwDone, color: 'var(--greenBar)' },
                { value: hwMissed, color: 'var(--red)' },
              ]}
              top={`${hwPct}%`}
              bottom="bajardi"
            />
            <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
              <Legend color="var(--greenBar)" label="Uy vazifa bajardi" value={hwDone} />
              <Legend color="var(--red)" label="Bajarmadi" value={hwMissed} />
              <Legend color="var(--accent)" label="Yaxshi xulq" value={nb.behaviorGood || 0} />
              <Legend color="var(--amberBar)" label="Intizomsizlik" value={nb.behaviorBad || 0} />
            </div>
          </div>
        ) : (
          <Note>Hali belgilanmagan.</Note>
        )}
      </Section>

      {hwTrend.some((d) => d.tot > 0) && (
        <Section title="Uy vazifa trendi" sub="Oylik bajarish foizi">
          <TrendBars data={hwTrend} max={100} color="var(--greenBar)" fmt={(v) => `${v}%`} />
        </Section>
      )}

      {/* Grading criteria (monthly + per lesson) — from the journal */}
      <GradingPanel hideWhenEmpty title="Baholash mezonlari" />
    </>
  )
}

/* ---------- chart & layout helpers ---------- */

function Section({ title, sub, children }: { title: string; sub?: string; children: ReactNode }) {
  return (
    <>
      <div className="st-sh">
        <div>
          <div className="st-sh-title">{title}</div>
          {sub && <div className="st-sh-sub">{sub}</div>}
        </div>
      </div>
      <div className="card">{children}</div>
    </>
  )
}

function Kpi({ value, label, color }: { value: string; label: string; color?: string }) {
  return (
    <div className="st-kpi" style={{ textAlign: 'center' }}>
      <div className="st-kpi-v" style={{ color: color ?? 'var(--faint)' }}>{value}</div>
      <div className="st-kpi-l">{label}</div>
    </div>
  )
}

function Legend({ color, label, value }: { color: string; label: string; value: ReactNode }) {
  return (
    <div className="row sp gap8">
      <div className="row gap8" style={{ minWidth: 0 }}>
        <span style={{ width: 10, height: 10, borderRadius: 3, background: color, flex: 'none' }} />
        <span className="muted" style={{ fontSize: 13.5 }}>{label}</span>
      </div>
      <span className="st-num" style={{ fontSize: 15 }}>{value}</span>
    </div>
  )
}

function HBar({ label, value, max, color, right, dot }: { label: string; value: number; max: number; color: string; right: string; dot?: string }) {
  const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0
  return (
    <div style={{ marginBottom: 12 }}>
      <div className="row sp" style={{ marginBottom: 6, gap: 8, alignItems: 'flex-start' }}>
        <span className="row gap6" style={{ minWidth: 0 }}>
          {dot && <span style={{ width: 8, height: 8, borderRadius: '50%', background: dot, flex: 'none' }} />}
          <span className="st-wrap" style={{ fontSize: 14, fontWeight: 600 }}>{label}</span>
        </span>
        <span className="st-num" style={{ fontSize: 14, color, flex: 'none' }}>{right}</span>
      </div>
      <div className="progress thin">
        <div style={{ width: `${pct}%`, background: color }} />
      </div>
    </div>
  )
}

function TrendBars({ data, max, color, fmt }: { data: { label: string; value: number }[]; max: number; color: string; fmt: (v: number) => string }) {
  return (
    <div className="row" style={{ alignItems: 'flex-end', gap: 8, height: 120 }}>
      {data.map((d, i) => {
        const h = max > 0 && d.value > 0 ? Math.max(6, Math.round((d.value / max) * 86)) : 3
        return (
          <div key={i} style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'flex-end', gap: 4, height: '100%' }}>
            <div className="st-num" style={{ fontSize: 11.5, color }}>{d.value > 0 ? fmt(d.value) : ''}</div>
            <div style={{ width: '100%', maxWidth: 30, height: h, background: d.value > 0 ? color : 'var(--surface3)', borderRadius: '7px 7px 3px 3px' }} />
            <div className="muted" style={{ fontSize: 11 }}>{d.label}</div>
          </div>
        )
      })}
    </div>
  )
}

function Donut({ size, stroke, segments, top, bottom }: { size: number; stroke: number; segments: { value: number; color: string }[]; top: string; bottom: string }) {
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const total = segments.reduce((acc, s) => acc + Math.max(0, s.value), 0) || 1
  // Arc lengths and their start offsets, computed without mutating during render.
  const arcs = segments.reduce<{ len: number; off: number; color: string }[]>((list, s) => {
    const prev = list[list.length - 1]
    return [...list, { len: (Math.max(0, s.value) / total) * c, off: prev ? prev.off + prev.len : 0, color: s.color }]
  }, [])
  return (
    <div style={{ position: 'relative', width: size, height: size, flex: 'none' }}>
      <svg width={size} height={size} style={{ transform: 'rotate(-90deg)' }}>
        <circle cx={size / 2} cy={size / 2} r={r} fill="none" stroke="var(--surface3)" strokeWidth={stroke} />
        {arcs.map((a, i) => (
          <circle key={i} cx={size / 2} cy={size / 2} r={r} fill="none" stroke={a.color} strokeWidth={stroke} strokeDasharray={`${a.len} ${c - a.len}`} strokeDashoffset={-a.off} />
        ))}
      </svg>
      <div style={{ position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center' }}>
        <div className="st-num" style={{ fontSize: size > 110 ? 22 : 19 }}>{top}</div>
        <div className="muted" style={{ fontSize: 11.5 }}>{bottom}</div>
      </div>
    </div>
  )
}

function Note({ children }: { children: ReactNode }) {
  return (
    <div className="muted" style={{ fontSize: 14, textAlign: 'center', padding: '6px 0' }}>
      {children}
    </div>
  )
}

/** Courses with grades (per-course average and how many months were graded). */
function ArchiveView({ nb }: { nb: StudentNotebook }) {
  const courses = Object.entries(nb.grades || {})
    .filter(([, monthsRecord]) => Object.keys(monthsRecord).length > 0)
    .map(([courseName, monthsRecord]) => {
      const vals = Object.values(monthsRecord).filter((v) => v > 0)
      return { name: courseName, months: Object.keys(monthsRecord).length, avg: vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0 }
    })

  if (courses.length === 0)
    return (
      <div className="card flush" style={{ marginTop: 14 }}>
        <EmptyState icon={Archive} title="Arxiv bo'sh" sub="Hali tugatilgan kurslar yo'q." />
      </div>
    )

  return (
    <>
      <div className="st-sh">
        <div>
          <div className="st-sh-title">Tugatilgan kurslar</div>
          <div className="st-sh-sub">Eski kurslardagi baholar</div>
        </div>
      </div>
      <div className="card flush">
        {courses.map((course) => (
          <div key={course.name} className="st-li">
            <div className="st-li-main">
              <div className="st-li-title">{course.name}</div>
              <div className="st-li-sub">{course.months} oy baholangan</div>
            </div>
            <div className="st-li-end">
              <div className="st-num" style={{ fontSize: 18, color: course.avg > 0 ? gradeColor(course.avg) : 'var(--faint)' }}>{course.avg > 0 ? course.avg.toFixed(1) : '—'}</div>
              <div className="muted" style={{ fontSize: 12 }}>o'rtacha</div>
            </div>
          </div>
        ))}
      </div>
    </>
  )
}
