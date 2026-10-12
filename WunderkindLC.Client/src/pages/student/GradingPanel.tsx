import { useEffect, useState } from 'react'
import { Check, ChevronLeft, ChevronRight, ListChecks, X } from 'lucide-react'
import { getStudentGrading, type StudentGradingGroup } from '@/api/services/studentPortal'
import { apiErrorMessage } from '@/lib/utils'
import { EmptyState, ErrorState, Skeleton } from '@/pages/student/ui'

/* ============================================================
   GRADING panel (shared) — monthly criteria summary + per-lesson marks.
   Used by the «Baholash mezonlari» screen AND inside Statistika.
   ============================================================ */

const MONTHS = ['Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun', 'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr']
const WD = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh']
function monthLabel(m: string) {
  const [y, mm] = m.split('-')
  return `${MONTHS[Number(mm) - 1] ?? mm} ${y}`
}
function weekday(d: string) {
  const [y, m, day] = d.split('-').map(Number)
  return WD[new Date(y, m - 1, day).getDay()]
}

/**
 * @param hideWhenEmpty Inside Statistika — render nothing when there is no grading (and on errors:
 *                      it is a secondary block there).
 * @param title Heading shown above the panel when there is data.
 */
export function GradingPanel({ hideWhenEmpty = false, title }: { hideWhenEmpty?: boolean; title?: string }) {
  const [groups, setGroups] = useState<StudentGradingGroup[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [sel, setSel] = useState(0)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let on = true
    getStudentGrading()
      .then((g) => {
        if (!on) return
        setGroups(g)
        setError(null)
      })
      .catch((e) => on && setError(apiErrorMessage(e, "Baholashni yuklab bo'lmadi")))
    return () => {
      on = false
    }
  }, [tick])

  // Month switch (event handler — state changes here are fine).
  const pickMonth = (month: string) => {
    setBusy(true)
    getStudentGrading(month)
      .then((g) => {
        setGroups(g)
        setError(null)
      })
      .catch((e) => setError(apiErrorMessage(e, "Baholashni yuklab bo'lmadi")))
      .finally(() => setBusy(false))
  }

  const g = groups && groups.length > 0 ? groups[Math.min(sel, groups.length - 1)] : null

  if (error && !groups) return hideWhenEmpty ? null : <ErrorState message={error} onRetry={() => setTick((t) => t + 1)} />
  if (!groups)
    return hideWhenEmpty ? null : (
      <div className="card">
        <Skeleton h={16} w="45%" />
        <Skeleton h={8} w="100%" style={{ marginTop: 14 }} />
        <Skeleton h={8} w="80%" style={{ marginTop: 14 }} />
      </div>
    )

  if (groups.length === 0 || !g) {
    if (hideWhenEmpty) return null
    return (
      <div className="card flush">
        <EmptyState icon={ListChecks} title="Baholash mezoni yo'q" sub="Guruhga baholash mezonlari biriktirilgach, har dars va oy bo'yicha ball shu yerda ko'rinadi." />
      </div>
    )
  }

  const idx = g.months.indexOf(g.month)
  return (
    <div style={{ opacity: busy ? 0.6 : 1, transition: 'opacity .15s' }}>
      {title && (
        <div className="st-sh">
          <div className="st-sh-title">{title}</div>
        </div>
      )}

      {groups.length > 1 && (
        <div className="st-pills" style={{ marginBottom: 12 }}>
          {groups.map((x, i) => (
            <button key={x.groupId} onClick={() => setSel(i)} className={'pill press' + (i === sel ? ' on' : '')}>
              {x.groupName}
            </button>
          ))}
        </div>
      )}

      {error && (
        <div role="alert" className="st-banner" style={{ marginBottom: 12 }}>
          {error}
        </div>
      )}

      {/* Month navigation */}
      <div className="card row sp" style={{ padding: 8 }}>
        <button className="iconbtn press" disabled={idx <= 0 || busy} style={{ opacity: idx <= 0 ? 0.35 : 1 }} onClick={() => idx > 0 && pickMonth(g.months[idx - 1])} aria-label="Oldingi oy">
          <ChevronLeft size={20} />
        </button>
        <span style={{ fontSize: 16, fontWeight: 800 }}>{monthLabel(g.month)}</span>
        <button
          className="iconbtn press"
          disabled={idx >= g.months.length - 1 || busy}
          style={{ opacity: idx >= g.months.length - 1 ? 0.35 : 1 }}
          onClick={() => idx < g.months.length - 1 && pickMonth(g.months[idx + 1])}
          aria-label="Keyingi oy"
        >
          <ChevronRight size={20} />
        </button>
      </div>

      {/* Collected points: this month + all time (sums, not averages) */}
      <div className="st-kpis two" style={{ marginTop: 10 }}>
        <div className="st-kpi" style={{ background: 'var(--accentSoft)', borderColor: 'transparent', boxShadow: 'none' }}>
          <div className="st-kpi-l" style={{ marginTop: 0, color: 'var(--accent)' }}>Bu oyda</div>
          <div className="st-kpi-v" style={{ color: 'var(--accent)' }}>{g.monthBall ?? 0}</div>
          <div className="st-kpi-s">ball</div>
        </div>
        <div className="st-kpi">
          <div className="st-kpi-l" style={{ marginTop: 0 }}>Jami yig'ilgan</div>
          <div className="st-kpi-v">{g.totalBall ?? 0}</div>
          <div className="st-kpi-s">ball</div>
        </div>
      </div>

      {/* Monthly summary */}
      <div className="st-sh">
        <div className="st-sh-title">Oylik xulosa</div>
      </div>
      <div className="card col" style={{ gap: 14 }}>
        {g.criteria.length === 0 ? (
          <p className="muted" style={{ fontSize: 14, textAlign: 'center', margin: 0 }}>Mezon biriktirilmagan.</p>
        ) : (
          g.criteria.map((c) => {
            const pct = c.total > 0 ? Math.round((c.done / c.total) * 100) : 0
            return (
              <div key={c.id}>
                <div className="row sp gap8" style={{ marginBottom: 6, alignItems: 'flex-start' }}>
                  <span className="st-wrap" style={{ fontSize: 14.5, fontWeight: 700 }}>{c.name}</span>
                  <span className="st-num" style={{ fontSize: 14, color: 'var(--accent)', flex: 'none' }}>
                    {c.done}/{c.total} · {pct}%
                  </span>
                </div>
                <div className="progress thin">
                  <div style={{ width: `${pct}%`, background: 'var(--accent)' }} />
                </div>
              </div>
            )
          })
        )}
      </div>

      {/* Per lesson */}
      <div className="st-sh">
        <div className="st-sh-title">Har dars</div>
      </div>
      {g.dates.length === 0 ? (
        <div className="card">
          <p className="muted" style={{ fontSize: 14, textAlign: 'center', margin: 0 }}>Bu oyda dars yo'q.</p>
        </div>
      ) : (
        <div className="card flush">
          {g.lessons.map((les) => {
            const doneSet = new Set(les.doneCriterionIds)
            return (
              <div key={les.date} className="st-li" style={{ alignItems: 'flex-start' }}>
                <div style={{ width: 40, textAlign: 'center', flex: 'none' }}>
                  <div className="muted" style={{ fontSize: 12, fontWeight: 600 }}>{weekday(les.date)}</div>
                  <div className="st-num" style={{ fontSize: 17 }}>{Number(les.date.slice(8, 10))}</div>
                </div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, flex: 1, minWidth: 0 }}>
                  {g.criteria.map((c) => {
                    const ok = doneSet.has(c.id)
                    return (
                      <span key={c.id} className={'st-chip' + (ok ? ' ok' : '')} style={{ whiteSpace: 'normal' }}>
                        {ok ? <Check size={13} strokeWidth={2.8} /> : <X size={13} strokeWidth={2.6} />}
                        {c.name}
                      </span>
                    )
                  })}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
