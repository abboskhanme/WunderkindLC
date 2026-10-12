import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { BookOpen } from 'lucide-react'
import {
  getStudentCurriculum,
  type StudentCurriculum,
  type CurriculumItem,
} from '@/api/services/studentPortal'
import { Icon, Ring, subjectColor, fmtDate } from '@/pages/student/lib'
import { useApi, useToast } from '@/pages/student/hooks'
import { EmptyState, ErrorState, PageHeader, Skeleton, Toast } from '@/pages/student/ui'

/* ============================================================
   Darslar → DASTUR — Duolingo-style curriculum roadmap (covered / remaining topics).
   The rating that used to share this screen («Reyting» tab) moved to Natijalar → Reyting
   (/student/rating); the old deep link `?tab=reyting` redirects there.
   ============================================================ */


/** Birinchi o'tilmagan band (kurs tartibida) — "hozir o'rganiladigan". */
function findNext(cur: StudentCurriculum): string | null {
  for (const md of cur.modules)
    for (const tp of md.topics) for (const it of tp.items) if (!it.covered) return it.id
  return null
}

const loadCurriculum = () => getStudentCurriculum()

function DasturView() {
  const { data: courses, error, loading, reload } = useApi(loadCurriculum)
  const [sel, setSel] = useState(0)

  if (error && !courses) return <ErrorState message={error} onRetry={reload} />
  if (loading && !courses)
    return (
      <div className="st-page">
        <div className="card row gap12">
          <Skeleton h={80} w={80} r={40} />
          <div style={{ flex: 1 }}>
            <Skeleton h={16} w="70%" />
            <Skeleton h={12} w="50%" style={{ marginTop: 10 }} />
            <Skeleton h={8} w="100%" style={{ marginTop: 12 }} />
          </div>
        </div>
      </div>
    )
  if (!courses || !courses.length)
    return (
      <div className="st-page">
        <div className="card flush">
          <EmptyState
            icon={BookOpen}
            title="O'quv dasturi hali yo'q"
            sub="Kursga o'quv dasturi biriktirilgach, o'tilgan va qolgan mavzular shu yerda ko'rinadi."
          />
        </div>
      </div>
    )

  const cur = courses[Math.min(sel, courses.length - 1)]
  return (
    <div className="st-page">
      {courses.length > 1 && (
        <div className="st-pills" style={{ marginBottom: 12 }}>
          {courses.map((c, i) => (
            <button key={c.groupId} onClick={() => setSel(i)} className={'pill press' + (i === sel ? ' on' : '')}>
              {c.courseName}
            </button>
          ))}
        </div>
      )}
      <ForecastCard cur={cur} />
      <Roadmap cur={cur} />
    </div>
  )
}


function ForecastCard({ cur }: { cur: StudentCurriculum }) {
  const pct = cur.totalItems > 0 ? Math.round((cur.coveredCount / cur.totalItems) * 100) : 0
  const done = cur.remainingItems <= 0
  const col = subjectColor(cur.courseId)
  return (
    <div
      className="card"
      style={{
        marginTop: 4,
        display: 'flex',
        gap: 14,
        alignItems: 'center',
        position: 'relative',
        overflow: 'hidden',
        background: `linear-gradient(135deg, ${col}16, var(--surface) 58%)`,
        borderColor: `${col}2e`,
      }}
    >
      {/* Ta'limiy dekor — fonda so'lg'in bitiruv shapkasi */}
      <div style={{ position: 'absolute', right: -14, bottom: -16, opacity: 0.08, pointerEvents: 'none' }}>
        <Icon name="school" size={104} color={col} fill />
      </div>
      <Ring value={pct} max={100} size={80} stroke={9} color={col}>
        <div style={{ fontSize: 20, fontWeight: 800, color: 'var(--text)' }}>{pct}%</div>
        <div style={{ fontSize: 10, color: 'var(--muted)' }}>o'tildi</div>
      </Ring>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ fontSize: 16, fontWeight: 800, color: 'var(--text)' }}>{cur.courseName}</div>
        <div style={{ fontSize: 13, color: 'var(--muted)', marginTop: 2 }}>
          O'tildi {cur.coveredCount}/{cur.totalItems} mavzu
        </div>
        <div className="progress" style={{ marginTop: 8 }}>
          <div style={{ width: `${pct}%`, background: col }} />
        </div>
        <div style={{ fontSize: 12.5, marginTop: 8, display: 'flex', alignItems: 'center', gap: 6 }}>
          <Icon name={done ? 'award' : 'clock'} size={15} color={done ? 'var(--green)' : col} fill={done} />
          {done ? (
            <span style={{ color: 'var(--green)', fontWeight: 700 }}>Kurs tugatildi! {'\u{1F389}'}</span>
          ) : (
            <span style={{ color: 'var(--muted)' }}>
              ~{cur.estLessonsLeft} dars qoldi
              {cur.estFinishDate ? ` · ≈ ${fmtDate(cur.estFinishDate)}` : ''}
            </span>
          )}
        </div>
      </div>
    </div>
  )
}

/** Roadmap panel fonidagi so'lg'in ta'limiy ikonkalar (kurs rangida). */
function Deco({ col }: { col: string }) {
  const items: Array<{ name: string; size: number; rot: number; pos: React.CSSProperties }> = [
    { name: 'book', size: 44, rot: -12, pos: { top: 10, left: 12 } },
    { name: 'edit', size: 36, rot: 14, pos: { top: 78, right: 16 } },
    { name: 'sparkle', size: 30, rot: 0, pos: { bottom: 60, left: 20 } },
    { name: 'flame', size: 32, rot: -8, pos: { bottom: 14, right: 18 } },
    { name: 'award', size: 30, rot: 10, pos: { top: 150, left: 18 } },
  ]
  return (
    <div style={{ position: 'absolute', inset: 0, borderRadius: 20, overflow: 'hidden', pointerEvents: 'none' }}>
      {items.map((d, i) => (
        <div key={i} style={{ position: 'absolute', opacity: 0.07, transform: `rotate(${d.rot}deg)`, ...d.pos }}>
          <Icon name={d.name} size={d.size} color={col} fill />
        </div>
      ))}
    </div>
  )
}

/** Kurs yakuni — sovrin belgisi (tugatilganda oltin). */
function FinishNode({ done, color }: { done: boolean; color: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 6, marginTop: 2 }}>
      <div
        style={{
          width: 66,
          height: 66,
          borderRadius: '50%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: done ? 'linear-gradient(135deg,#F5B301,#E8920A)' : 'var(--surface3)',
          color: done ? '#fff' : 'var(--faint)',
          boxShadow: done ? '0 8px 22px rgba(245,179,1,.42)' : 'none',
          border: done ? 'none' : `2px dashed ${color}55`,
        }}
      >
        <Icon name="award" size={32} color={done ? '#fff' : 'var(--faint)'} fill={done} />
      </div>
      <div style={{ fontSize: 12, fontWeight: 800, color: done ? '#E8920A' : 'var(--faint)' }}>
        {done ? "Kurs yakunlandi! \u{1F3C6}" : 'Kurs yakuni'}
      </div>
    </div>
  )
}

function Roadmap({ cur }: { cur: StudentCurriculum }) {
  const nav = useNavigate()
  const [toast, setToast] = useToast(2400)
  const nextId = findNext(cur)
  const col = subjectColor(cur.courseId)
  const allDone = cur.remainingItems <= 0

  // Dars FAQAT o'qituvchi "o'tildi" qilgach (covered) ochiladi; aks holda yopiq.
  const openLesson = (it: CurriculumItem) => {
    if (it.covered) nav(`/student/lesson/${it.id}`)
    else setToast("Bu dars hali yopiq — o'qituvchi o'tgach ochiladi")
  }

  return (
    <div style={{ marginTop: 18 }}>
      {cur.modules.map((md) => {
        const mItems = md.topics.flatMap((tp) => tp.items)
        if (!mItems.length) return null
        const mCov = mItems.filter((i) => i.covered).length
        return (
          <div key={md.id} style={{ marginBottom: 8 }}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '4px 2px 8px',
              }}
            >
              <div style={{ fontSize: 12, fontWeight: 800, letterSpacing: 0.4, textTransform: 'uppercase', color: 'var(--muted)' }}>
                {md.name}
              </div>
              <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--muted)' }}>
                {mCov}/{mItems.length}
              </div>
            </div>
            {md.topics.map((tp) => {
        const items = tp.items
        if (!items.length) return null
        const cov = items.filter((i) => i.covered).length
        const tpDone = cov === items.length
        const coveredFrac = items.length > 0 ? cov / items.length : 0
        return (
          <div key={tp.id} style={{ marginBottom: 14 }}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 10,
                padding: '13px 14px',
                borderRadius: 16,
                background: `linear-gradient(135deg, ${col}, ${col}cc)`,
                color: '#fff',
                marginBottom: 6,
                boxShadow: `0 8px 20px ${col}40`,
              }}
            >
              <div
                style={{
                  width: 34,
                  height: 34,
                  borderRadius: 11,
                  background: 'rgba(255,255,255,.22)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  flex: 'none',
                }}
              >
                <Icon name={tpDone ? 'award' : 'book'} size={19} color="#fff" fill />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 10.5, fontWeight: 700, opacity: 0.85, textTransform: 'uppercase', letterSpacing: 0.4 }}>
                  Mavzu
                </div>
                <div style={{ fontSize: 16, fontWeight: 800 }}>{tp.title}</div>
              </div>
              <div style={{ fontSize: 13, fontWeight: 800 }}>
                {cov}/{items.length}
              </div>
            </div>

            <div
              style={{
                position: 'relative',
                borderRadius: 20,
                padding: '16px 0 12px',
                background: `linear-gradient(180deg, ${col}12, ${col}04)`,
                border: `1px solid ${col}1f`,
              }}
            >
              <Deco col={col} />
              {/* Yo'l chizig'i — o'tilgan qismi kurs rangida, qolgani so'lg'in */}
              <div
                style={{
                  position: 'absolute',
                  left: '50%',
                  top: 12,
                  bottom: 12,
                  width: 4,
                  transform: 'translateX(-2px)',
                  borderRadius: 4,
                  background: `linear-gradient(${col} ${Math.round(coveredFrac * 100)}%, ${col}26 ${Math.round(coveredFrac * 100)}%)`,
                  opacity: 0.55,
                }}
              />
              <div style={{ position: 'relative', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 18 }}>
                {items.map((it, idx) => (
                  <Node
                    key={it.id}
                    item={it}
                    state={it.covered ? 'done' : it.id === nextId ? 'now' : 'lock'}
                    offset={Math.round(Math.sin(idx * 0.85) * 58)}
                    color={col}
                    onOpen={() => openLesson(it)}
                  />
                ))}
              </div>
            </div>
          </div>
        )
            })}
          </div>
        )
      })}
      <FinishNode done={allDone} color={col} />
      <Toast text={toast} />
    </div>
  )
}

function Node({
  item,
  state,
  offset,
  color,
  onOpen,
}: {
  item: CurriculumItem
  state: 'done' | 'now' | 'lock'
  offset: number
  color: string
  onOpen: () => void
}) {
  const bg = state === 'done' ? 'var(--green)' : state === 'now' ? color : 'var(--surface3)'
  const fg = state === 'lock' ? 'var(--faint)' : '#fff'
  const icon = state === 'done' ? 'check' : state === 'now' ? 'book' : 'lock'
  return (
    <div style={{ transform: `translateX(${offset}px)`, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 5, maxWidth: 150 }}>
      <button
        type="button"
        onClick={onOpen}
        className="press"
        style={{
          width: 58,
          height: 58,
          borderRadius: '50%',
          background: bg,
          color: fg,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          flex: 'none',
          border: state === 'lock' ? '1px solid var(--border)' : 'none',
          boxShadow:
            state === 'now'
              ? `0 0 0 4px var(--surface), 0 0 0 7px ${color}`
              : state === 'done'
                ? '0 4px 10px rgba(22,163,74,.30)'
                : 'none',
        }}
      >
        <Icon name={icon} size={26} color={fg} fill={state !== 'lock'} />
      </button>
      <div
        style={{
          fontSize: 11,
          fontWeight: 700,
          color: state === 'lock' ? 'var(--faint)' : 'var(--text)',
          textAlign: 'center',
          lineHeight: 1.2,
          maxWidth: 132,
          overflow: 'hidden',
          display: '-webkit-box',
          WebkitLineClamp: 2,
          WebkitBoxOrient: 'vertical',
        }}
      >
        {item.text}
      </div>
      {state === 'now' && <div style={{ fontSize: 10, fontWeight: 800, color }}>HOZIR</div>}
      {state === 'done' && item.coveredDate && (
        <div style={{ fontSize: 10, color: 'var(--faint)' }}>{fmtDate(item.coveredDate)}</div>
      )}
    </div>
  )
}

export function StudentProgressScreen() {
  const { search } = useLocation()
  // Old deep link (the rating used to be a tab here).
  if (new URLSearchParams(search).get('tab') === 'reyting') return <Navigate to="/student/rating" replace />
  return (
    <div className="screen">
      <PageHeader section="lessons" />
      <div className="scroll">
        <DasturView />
      </div>
    </div>
  )
}
