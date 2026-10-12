import { useEffect, useState } from 'react'
import { Trophy } from 'lucide-react'
import { getStudentRatingSummary, type RatingTopRow, type StudentRatingSummary } from '@/api/services/rating'
import { StudentRatingBlock } from '@/components/rating/StudentRatingBlock'
import { defaultRatingPeriod, fmtScore, type RatingPeriodValue } from '@/lib/ratingPeriods'
import { apiErrorMessage } from '@/lib/utils'
import { Avatar, EmptyState, PageHeader, SectionTitle, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Natijalar → REYTING (testlar + ball, CRM 4-bosqich; `.claude/rules/rating.md` §7).
   The block (same component as the admin profile, `cabinet` variant) + group and centre TOP lists
   with the student's own row marked. Source: GET /api/student/rating/summary.
   Debt never hides the rating (owner's decision).
   ============================================================ */

/** Period key — which selection a response belongs to. */
const periodKey = (p: RatingPeriodValue) => `${p.kind}:${p.key}`

export function StudentRatingScreen() {
  return (
    <div className="screen">
      <PageHeader section="results" />
      <div className="scroll">
        <RatingCabinetView />
      </div>
    </div>
  )
}

function RatingCabinetView() {
  const [period, setPeriod] = useState<RatingPeriodValue>(() => defaultRatingPeriod(new Date()))
  const [res, setRes] = useState<{ key: string; data: StudentRatingSummary | null; error: string | null } | null>(null)
  const key = periodKey(period)

  useEffect(() => {
    let on = true
    const k = `${period.kind}:${period.key}`
    getStudentRatingSummary({ period: period.kind, key: period.key })
      .then((data) => on && setRes({ key: k, data, error: null }))
      .catch((e) => on && setRes((prev) => ({ key: k, data: prev?.data ?? null, error: apiErrorMessage(e, "Reytingni yuklab bo'lmadi") })))
    return () => {
      on = false
    }
  }, [period.kind, period.key])

  const data = res?.data ?? null
  return (
    <div className="st-page" style={{ paddingTop: 8 }}>
      <StudentRatingBlock
        variant="cabinet"
        data={data}
        loading={res?.key !== key}
        error={res?.key === key ? res.error : null}
        period={period}
        onPeriodChange={setPeriod}
      >
        {data && (
          <>
            {data.groupTop.map((g) => (
              <TopList key={g.groupId} title={g.groupName} sub="Guruh reytingi" rows={g.rows} />
            ))}
            <TopList title="Markaz reytingi" sub="TOP 10" rows={data.centreTop} />
          </>
        )}
      </StudentRatingBlock>
    </div>
  )
}

const MEDAL: Record<number, string> = { 1: '#d4a106', 2: '#8a94a6', 3: '#b8712f' }

/** TOP list: rank (medal colours for 1–3), initials, "Siz" mark on the student's own row, score. */
function TopList({ title, sub, rows }: { title: string; sub: string; rows: RatingTopRow[] }) {
  return (
    <div>
      <SectionTitle title={title} sub={sub} style={{ marginTop: 6 }} />
      {rows.length ? (
        <div className="card flush">
          {rows.map((e, i) => {
            const m = MEDAL[e.rank]
            const prevRank = i > 0 ? rows[i - 1].rank : 0
            // Own row appended after the top 10: a thin gap shows the jump (e.g. 10 → 23).
            const gap = i > 0 && e.rank - prevRank > 1 && e.isMe
            return (
              <div key={`${e.rank}_${i}`}>
                {gap && (
                  <div className="muted" style={{ textAlign: 'center', fontSize: 13, padding: '2px 0', borderTop: '1px solid var(--border)' }}>
                    ⋯
                  </div>
                )}
                <div
                  className="st-li"
                  style={{
                    minHeight: 56,
                    background: e.isMe ? 'var(--accentSoft)' : undefined,
                    borderTop: i > 0 ? '1px solid var(--border)' : undefined,
                  }}
                >
                  <div
                    className="st-num"
                    style={{
                      width: 34,
                      height: 34,
                      borderRadius: 10,
                      flex: 'none',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontSize: 15,
                      color: m || 'var(--muted)',
                      background: m ? `color-mix(in srgb, ${m} 16%, transparent)` : 'var(--surface3)',
                    }}
                  >
                    {e.rank}
                  </div>
                  <Avatar name={e.fullName} size={34} gradient="linear-gradient(135deg,#6366f1,#4f46e5)" />
                  <div className="st-li-main">
                    <div className="st-li-title" style={{ fontSize: 14.5, fontWeight: e.isMe ? 800 : 600 }}>
                      {e.fullName} {e.isMe && <StatusChip tone="info">Siz</StatusChip>}
                    </div>
                  </div>
                  <div className="st-li-end">
                    <div className="st-num" style={{ fontSize: 16, color: m || '#6366f1' }}>{fmtScore(e.score)}</div>
                    <div className="faint" style={{ fontSize: 11.5, fontWeight: 600 }}>reyting</div>
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      ) : (
        <div className="card flush">
          <EmptyState compact icon={Trophy} title="Reyting yo'q" sub="Bu davrda test ham, ball ham yo'q." />
        </div>
      )}
    </div>
  )
}
