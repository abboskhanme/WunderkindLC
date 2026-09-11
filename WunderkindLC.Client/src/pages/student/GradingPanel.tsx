import { useEffect, useState } from 'react'
import { getStudentGrading, type StudentGradingGroup } from '@/api/services/studentPortal'
import { Icon } from '@/pages/student/lib'

/* ============================================================
   BAHOLASH paneli (umumiy) — oylik mezon xulosasi + har darslik
   belgilar. Grading ekrani VA Statistika bo'limi ishlatadi.
   ============================================================ */

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
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
 * @param hideWhenEmpty Statistika ichida — baholash bo'lmasa umuman ko'rsatmaslik (null).
 * @param title Ma'lumot bo'lsa tepada ko'rsatiladigan sarlavha (bo'sh bo'lsa sarlavha chiqmaydi).
 */
export function GradingPanel({
  hideWhenEmpty = false,
  title,
}: {
  hideWhenEmpty?: boolean
  title?: string
}) {
  const [groups, setGroups] = useState<StudentGradingGroup[] | null>(null)
  const [sel, setSel] = useState(0)
  const [loading, setLoading] = useState(true)

  const load = (month?: string) => {
    setLoading(true)
    getStudentGrading(month)
      .then((g) => setGroups(g))
      .catch(() => setGroups([]))
      .finally(() => setLoading(false))
  }
  useEffect(() => load(undefined), [])

  const g = groups && groups.length > 0 ? groups[Math.min(sel, groups.length - 1)] : null

  if (loading)
    return (
      <div className="center" style={{ minHeight: 80 }}>
        <div className="spin" />
      </div>
    )

  if (!groups || groups.length === 0) {
    if (hideWhenEmpty) return null
    return (
      <div className="card" style={{ marginTop: 12 }}>
        <div className="empty">
          <div className="empty-ic"><Icon name="checkCircle" size={28} /></div>
          <div style={{ fontSize: 15, fontWeight: 800 }}>Baholash mavjud emas</div>
          <div className="muted" style={{ fontSize: 13, marginTop: 4 }}>
            Guruhingizga baholash mezoni biriktirilmagan.
          </div>
        </div>
      </div>
    )
  }
  if (!g) return null

  return (
    <>
      {title && (
        <div className="sh" style={{ marginTop: 22, paddingBottom: 4 }}>
          <div className="sh-title">{title}</div>
        </div>
      )}

      {/* Guruh tanlash (bir nechta bo'lsa) */}
      {groups.length > 1 && (
        <div style={{ display: 'flex', gap: 8, overflowX: 'auto', paddingBottom: 8 }}>
          {groups.map((x, i) => (
            <button
              key={x.groupId}
              onClick={() => setSel(i)}
              className="press"
              style={{
                flex: 'none', padding: '8px 14px', borderRadius: 12, fontSize: 13, fontWeight: 700,
                background: i === sel ? 'var(--accent)' : 'var(--surface)',
                color: i === sel ? '#fff' : 'var(--muted)',
                border: `1px solid ${i === sel ? 'var(--accent)' : 'var(--border)'}`,
              }}
            >
              {x.groupName}
            </button>
          ))}
        </div>
      )}

      {/* Oy navigatsiyasi */}
      <MonthNav group={g} onPick={(m) => load(m)} />

      {/* Yig'ilgan ball: shu oy + guruh bo'yicha jami (o'rtacha emas) */}
      <div className="sh" style={{ marginTop: 6 }}>
        <div className="sh-title">Yig'ilgan ball</div>
      </div>
      <div className="card" style={{ display: 'flex', gap: 10 }}>
        <BallTile label="Bu oyda" value={g.monthBall ?? 0} accent />
        <BallTile label="Jami yig'ilgan" value={g.totalBall ?? 0} />
      </div>

      {/* OYLIK xulosa */}
      <div className="sh" style={{ marginTop: 16 }}>
        <div className="sh-title">Oylik xulosa</div>
      </div>
      <div className="card" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
        {g.criteria.length === 0 ? (
          <p className="muted" style={{ fontSize: 13, textAlign: 'center' }}>Mezon biriktirilmagan.</p>
        ) : (
          g.criteria.map((c) => {
            const pct = c.total > 0 ? Math.round((c.done / c.total) * 100) : 0
            return (
              <div key={c.id}>
                <div className="row" style={{ justifyContent: 'space-between', marginBottom: 6 }}>
                  <span style={{ fontSize: 14.5, fontWeight: 700 }}>{c.name}</span>
                  <span className="font-mono" style={{ fontSize: 13.5, fontWeight: 800, color: 'var(--accent)' }}>
                    {c.done}/{c.total} · {pct}%
                  </span>
                </div>
                <div className="progress">
                  <div style={{ width: `${pct}%`, background: 'var(--accent)' }} />
                </div>
              </div>
            )
          })
        )}
      </div>

      {/* HAR DARSLIK */}
      <div className="sh" style={{ marginTop: 16 }}>
        <div className="sh-title">Har darslik</div>
      </div>
      {g.dates.length === 0 ? (
        <div className="card"><p className="muted" style={{ fontSize: 13, textAlign: 'center' }}>Bu oyda dars yo'q.</p></div>
      ) : (
        <div className="card" style={{ padding: 4 }}>
          {g.lessons.map((les, i) => {
            const doneSet = new Set(les.doneCriterionIds)
            return (
              <div
                key={les.date}
                style={{
                  display: 'flex', alignItems: 'center', gap: 12, padding: '10px 12px',
                  borderBottom: i < g.lessons.length - 1 ? '1px solid var(--border)' : undefined,
                }}
              >
                {/* Sana */}
                <div style={{ width: 42, textAlign: 'center', flex: 'none' }}>
                  <div className="muted" style={{ fontSize: 10.5, fontWeight: 600 }}>{weekday(les.date)}</div>
                  <div style={{ fontSize: 16, fontWeight: 800 }}>{Number(les.date.slice(8, 10))}</div>
                </div>
                {/* Mezonlar */}
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, flex: 1 }}>
                  {g.criteria.map((c) => {
                    const ok = doneSet.has(c.id)
                    return (
                      <span
                        key={c.id}
                        style={{
                          display: 'inline-flex', alignItems: 'center', gap: 4,
                          padding: '3px 8px', borderRadius: 9, fontSize: 11.5, fontWeight: 700,
                          background: ok ? 'var(--greenSoft, #dcfce7)' : 'var(--surface3)',
                          color: ok ? 'var(--green, #16a34a)' : 'var(--faint)',
                        }}
                      >
                        <Icon name={ok ? 'check' : 'x'} size={12} color={ok ? 'var(--green, #16a34a)' : 'var(--faint)'} />
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
    </>
  )
}

/** Ball kartachasi — "Bu oyda" / "Jami yig'ilgan". */
function BallTile({ label, value, accent = false }: { label: string; value: number; accent?: boolean }) {
  return (
    <div
      style={{
        flex: 1, textAlign: 'center', padding: '10px 8px', borderRadius: 14,
        background: accent ? 'var(--accentSoft, #ccfbf1)' : 'var(--surface3)',
        border: `1px solid ${accent ? 'var(--accent)' : 'var(--border)'}`,
      }}
    >
      <div className="muted" style={{ fontSize: 11.5, fontWeight: 700 }}>{label}</div>
      <div
        className="font-mono"
        style={{ fontSize: 22, fontWeight: 800, marginTop: 2, color: accent ? 'var(--accent)' : undefined }}
      >
        {value}
      </div>
      <div className="muted" style={{ fontSize: 10.5 }}>ball</div>
    </div>
  )
}

function MonthNav({ group, onPick }: { group: StudentGradingGroup; onPick: (m: string) => void }) {
  const idx = group.months.indexOf(group.month)
  return (
    <div className="row" style={{ justifyContent: 'center', gap: 10, marginTop: 4 }}>
      <button
        className="iconbtn press"
        disabled={idx <= 0}
        style={{ opacity: idx <= 0 ? 0.4 : 1 }}
        onClick={() => idx > 0 && onPick(group.months[idx - 1])}
      >
        <Icon name="chevL" size={18} />
      </button>
      <span style={{ minWidth: 120, textAlign: 'center', fontSize: 14.5, fontWeight: 800 }}>
        {monthLabel(group.month)}
      </span>
      <button
        className="iconbtn press"
        disabled={idx >= group.months.length - 1}
        style={{ opacity: idx >= group.months.length - 1 ? 0.4 : 1 }}
        onClick={() => idx < group.months.length - 1 && onPick(group.months[idx + 1])}
      >
        <Icon name="chevR" size={18} />
      </button>
    </div>
  )
}
