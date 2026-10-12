import { CalendarCheck } from 'lucide-react'
import { getStudentAttendance, getStudentNotebook, type AbsenceRow } from '@/api/services/studentPortal'
import { Ring, fmtDate } from '@/pages/student/lib'
import { DAY_NAMES, toneColors, type Tone } from '@/pages/student/format'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, Skeleton, SkeletonList, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Darslar → DAVOMAT: overall attendance (notebook — the same number as Statistika) +
   missed / ill / late counters and the list of missed lessons with the reason.
   ============================================================ */

const loadAttendance = () => getStudentAttendance(1)
const loadNotebook = () => getStudentNotebook()

function reasonTone(r: AbsenceRow): Tone {
  if (r.isLate) return 'info'
  if (r.isIll) return 'warn'
  if (/^sababli/i.test((r.reasonName || '').trim())) return 'neutral'
  return 'bad'
}

function weekdayOf(iso: string): string {
  const d = new Date(iso.length <= 10 ? iso + 'T00:00:00' : iso)
  return isNaN(d.getTime()) ? '' : DAY_NAMES[(d.getDay() + 6) % 7]
}

export function StudentAttendanceScreen() {
  const att = useApi(loadAttendance)
  const nb = useApi(loadNotebook)

  const a = att.data?.summary
  const rows = [...(att.data?.rows ?? [])].sort((x, y) => (y.date || '').localeCompare(x.date || ''))
  const counters: { label: string; value: number; tone: Tone }[] = [
    { label: 'Qoldirilgan', value: a?.missedLessons?.[1] || 0, tone: 'bad' },
    { label: 'Kasallik', value: a?.illnessDays?.[1] || 0, tone: 'warn' },
    { label: 'Kech qolgan', value: a?.lateCount?.[1] || 0, tone: 'info' },
  ]
  const conducted = nb.data?.conducted ?? 0
  const attended = nb.data?.attended ?? 0
  const pct = nb.data?.attendancePct ?? 0

  return (
    <div className="screen">
      <PageHeader section="lessons" />
      <div className="scroll">
        <div className="st-page">
          {att.error && !att.data ? (
            <ErrorState message={att.error} onRetry={att.reload} />
          ) : att.loading && !att.data ? (
            <>
              <div className="card row gap12">
                <Skeleton h={84} w={84} r={42} />
                <div style={{ flex: 1 }}>
                  <Skeleton h={16} w="70%" />
                  <Skeleton h={12} w="45%" style={{ marginTop: 10 }} />
                </div>
              </div>
              <div style={{ height: 22 }} />
              <SkeletonList rows={2} />
            </>
          ) : (
            <>
              <div className="card">
                {nb.data && conducted > 0 && (
                  <div className="row gap12" style={{ marginBottom: 14 }}>
                    <Ring value={pct} max={100} size={84} stroke={9} color={pct >= 75 ? 'var(--greenBar)' : pct >= 50 ? 'var(--amberBar)' : 'var(--red)'}>
                      <div className="st-num" style={{ fontSize: 20 }}>{pct}%</div>
                    </Ring>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontSize: 16, fontWeight: 800, lineHeight: 1.3 }}>Davomat</div>
                      <div className="muted" style={{ fontSize: 14, marginTop: 2, lineHeight: 1.4 }}>
                        {conducted} ta darsdan {attended} tasida qatnashdi
                      </div>
                    </div>
                  </div>
                )}
                <div className="st-kpis">
                  {counters.map((c) => {
                    const t = toneColors(c.value > 0 ? c.tone : 'neutral')
                    return (
                      <div key={c.label} style={{ padding: '10px 8px', borderRadius: 14, background: t.bg, textAlign: 'center', minWidth: 0 }}>
                        <div className="st-num" style={{ fontSize: 22, color: c.value > 0 ? t.fg : 'var(--text)' }}>{c.value}</div>
                        <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--muted)', marginTop: 2 }}>{c.label}</div>
                      </div>
                    )
                  })}
                </div>
              </div>

              <div className="st-sh">
                <div className="st-sh-title">Qoldirilgan darslar</div>
              </div>
              {rows.length ? (
                <div className="card flush">
                  {rows.map((r, i) => (
                    <div key={r.date + '_' + r.period + '_' + i} className="st-li">
                      <div style={{ width: 44, flex: 'none', textAlign: 'center' }}>
                        <div className="st-num" style={{ fontSize: 18, lineHeight: 1.1 }}>{Number((r.date || '').slice(8, 10)) || ''}</div>
                        <div className="muted" style={{ fontSize: 12, fontWeight: 600 }}>{fmtDate(r.date).split(' ')[1]?.slice(0, 3)}</div>
                      </div>
                      <div className="st-li-main">
                        <div className="st-li-title">{r.subjectName}</div>
                        <div className="st-li-sub">{weekdayOf(r.date)}</div>
                      </div>
                      {r.reasonName && <StatusChip tone={reasonTone(r)}>{r.reasonName}</StatusChip>}
                    </div>
                  ))}
                </div>
              ) : (
                <div className="card flush">
                  <EmptyState icon={CalendarCheck} tone="ok" title="Ajoyib davomat!" sub="Qoldirilgan dars yo'q." />
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
