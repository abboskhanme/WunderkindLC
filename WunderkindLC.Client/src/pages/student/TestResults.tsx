import { useMemo } from 'react'
import { ClipboardList } from 'lucide-react'
import { getStudentTestResults, type StudentTestResult } from '@/api/services/studentPortal'
import { fmtScore } from '@/lib/ratingPeriods'
import { fmtDate } from '@/pages/student/lib'
import { percentTone, toneColors } from '@/pages/student/format'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, SkeletonList, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Natijalar → TESTLAR — monthly test results (Google Sheets replacement, 2026-10-07).
   The teacher enters scores in their Mini App; the student and the parents see them here at once.
   Source: GET /student/test-results (student — own, parent — their child's).
   ============================================================ */

const loadTests = () => getStudentTestResults()

const pct = (r: StudentTestResult) => (r.score === null || r.maxScore <= 0 ? null : Math.round((r.score / r.maxScore) * 100))

const barColor = (p: number | null) => {
  const t = percentTone(p)
  return t === 'ok' ? 'var(--greenBar)' : t === 'warn' ? 'var(--amberBar)' : toneColors(t).fg
}

function ResultCard({ r }: { r: StudentTestResult }) {
  const p = pct(r)
  const c = toneColors(percentTone(p))
  return (
    <div className="card" style={{ padding: 14 }}>
      <div className="row gap12" style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="st-wrap" style={{ fontSize: 15.5, fontWeight: 700, lineHeight: 1.3 }}>{r.name}</div>
          <div className="muted st-wrap" style={{ fontSize: 13, marginTop: 2 }}>
            {fmtDate(r.date)}
            {r.groupName ? ` · ${r.groupName}` : ''}
          </div>
        </div>
        <div style={{ textAlign: 'right', flex: 'none' }}>
          <div className="st-num" style={{ fontSize: 22, lineHeight: 1.1, color: p === null ? 'var(--faint)' : c.fg }}>{p === null ? '—' : `${p}%`}</div>
          <div className="muted" style={{ fontSize: 12.5, fontWeight: 600 }}>
            {r.score === null ? 'ball kiritilmagan' : `${fmtScore(r.score)} / ${r.maxScore}`}
          </div>
        </div>
      </div>
      {p !== null && (
        <div className="progress thin" style={{ marginTop: 12 }}>
          <div style={{ width: `${Math.min(100, p)}%`, background: barColor(p) }} />
        </div>
      )}
      {r.score !== null && r.total > 0 && (
        <div style={{ marginTop: 10 }}>
          <StatusChip tone="violet">
            Guruhda {r.rank}-o'rin · {r.total} ta o'quvchidan
          </StatusChip>
        </div>
      )}
    </div>
  )
}

export function StudentTestResultsScreen() {
  const { data, error, loading, reload } = useApi(loadTests)
  const rows = useMemo(() => [...(data ?? [])].sort((a, b) => (b.date || '').localeCompare(a.date || '')), [data])

  // Average — only over tests that have a score.
  const avg = useMemo(() => {
    const ps = rows.map(pct).filter((p): p is number => p !== null)
    return ps.length ? Math.round(ps.reduce((a, b) => a + b, 0) / ps.length) : null
  }, [rows])
  const scored = rows.filter((r) => pct(r) !== null).length

  return (
    <div className="screen">
      <PageHeader section="results" />
      <div className="scroll">
        <div className="st-page">
          {error && !data ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !data ? (
            <SkeletonList rows={3} />
          ) : rows.length === 0 ? (
            <div className="card flush">
              <EmptyState icon={ClipboardList} title="Hali test natijasi yo'q" sub="O'qituvchi oylik test ballarini kiritgach shu yerda ko'rinadi." />
            </div>
          ) : (
            <>
              {avg !== null && (
                <div className="card row gap12" style={{ marginBottom: 12 }}>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div className="muted" style={{ fontSize: 13.5, fontWeight: 600 }}>O'rtacha natija</div>
                    <div className="muted" style={{ fontSize: 13 }}>{scored} ta test bo'yicha</div>
                  </div>
                  <div className="st-num" style={{ fontSize: 30, color: toneColors(percentTone(avg)).fg }}>{avg}%</div>
                </div>
              )}
              <div className="col" style={{ gap: 10 }}>
                {rows.map((r) => (
                  <ResultCard key={r.testId} r={r} />
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
