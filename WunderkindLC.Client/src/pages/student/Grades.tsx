import { ChartColumn } from 'lucide-react'
import { getStudentGrades } from '@/api/services/studentPortal'
import { Ring, gradeColor, subjectColor, subjInitial } from '@/pages/student/lib'
import { useApi } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, SkeletonList } from '@/pages/student/ui'

/* ============================================================
   Natijalar → BAHOLAR — current journal grade per subject (no quarters) and the average.
   ============================================================ */

const loadGrades = () => getStudentGrades()

const gradeLabel = (g: number) => (g === Math.round(g) ? String(Math.round(g)) : g.toFixed(1))

export function StudentGradesScreen() {
  const { data: report, error, loading, reload } = useApi(loadGrades)

  const subjects = report?.subjects || []
  const rows = subjects.map((s) => ({ s, g: report?.grades?.[s.id]?.[1] as number | undefined })).filter((r) => r.g != null)
  const vals = rows.map((r) => r.g as number)
  const avg = vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : 0
  const fives = vals.filter((v) => v >= 4.5).length

  return (
    <div className="screen">
      <PageHeader section="results" />
      <div className="scroll">
        <div className="st-page">
          {error && !report ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !report ? (
            <SkeletonList rows={3} />
          ) : !rows.length ? (
            <div className="card flush">
              <EmptyState icon={ChartColumn} title="Hali baho qo'yilmagan" sub="O'qituvchi jurnalga baho qo'ygach, fanlar bo'yicha baholar shu yerda ko'rinadi." />
            </div>
          ) : (
            <>
              <div className="card row" style={{ gap: 16 }}>
                <Ring value={(avg / 5) * 100} max={100} size={88} stroke={10} color={gradeColor(avg)}>
                  <div className="st-num" style={{ fontSize: 24, lineHeight: 1, color: gradeColor(avg) }}>{avg.toFixed(1)}</div>
                </Ring>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 16, fontWeight: 800 }}>O'rtacha baho</div>
                  <div className="muted" style={{ fontSize: 14, marginTop: 4, lineHeight: 1.45 }}>
                    {rows.length} ta fan bo'yicha
                    {fives > 0 ? ` · ${fives} ta fanda «5»` : ''}
                  </div>
                </div>
              </div>

              <div className="st-sh">
                <div className="st-sh-title">Fanlar bo'yicha</div>
              </div>
              <div className="card flush">
                {subjects.map((s) => {
                  const col = subjectColor(s.id)
                  const cur = report?.grades?.[s.id]?.[1]
                  return (
                    <div key={s.id} className="st-li">
                      <div className="subj st-ic" style={{ background: `color-mix(in srgb, ${col} 14%, transparent)`, color: col, fontSize: 17 }}>
                        {subjInitial(s.name)}
                      </div>
                      <div className="st-li-main">
                        <div className="st-li-title">{s.name}</div>
                      </div>
                      {cur != null ? (
                        <div
                          className="st-num"
                          style={{
                            minWidth: 40,
                            height: 40,
                            padding: '0 8px',
                            borderRadius: 12,
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            fontSize: 18,
                            color: gradeColor(cur),
                            background: `color-mix(in srgb, ${gradeColor(cur)} 14%, transparent)`,
                          }}
                        >
                          {gradeLabel(cur)}
                        </div>
                      ) : (
                        <span className="faint" style={{ fontSize: 15, fontWeight: 700 }}>—</span>
                      )}
                    </div>
                  )
                })}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
