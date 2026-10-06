import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentTestResults, type StudentTestResult } from '@/api/services/studentPortal'
import { Icon, fmtDate } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — OYLIK TEST NATIJALARI (Google Sheets o'rniga, 2026-10-07 uchrashuvi).
   O'qituvchi o'z guruhida ball kiritadi (Mini App → Testlar) — natija shu yerda o'quvchiga ham,
   ota-onaga ham darhol ko'rinadi. Manba: GET /student/test-results (o'quvchi — o'ziniki,
   ota-ona — farzandiniki). Dizayn: Contracts/Certificates bilan bir xil (.student-app).
   ============================================================ */

const pct = (r: StudentTestResult) =>
  r.score === null || r.maxScore <= 0 ? null : Math.round((r.score / r.maxScore) * 100)

/** Foiz rangi: 85+ yashil, 60+ ko'k, 40+ sariq, qolgani qizil. */
function pctColor(p: number | null): string {
  if (p === null) return 'var(--faint)'
  if (p >= 85) return '#16A34A'
  if (p >= 60) return '#2563EB'
  if (p >= 40) return '#D97706'
  return '#DC2626'
}

function ResultCard({ r }: { r: StudentTestResult }) {
  const p = pct(r)
  const color = pctColor(p)
  return (
    <div className="card" style={{ borderRadius: 18, padding: '14px 16px', marginBottom: 12, borderLeft: `4px solid ${color}` }}>
      <div className="row gap12" style={{ alignItems: 'flex-start' }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 800, letterSpacing: '-.2px', lineHeight: 1.25 }}>{r.name}</div>
          <div className="muted" style={{ fontSize: 12.5, marginTop: 2 }}>
            {r.groupName} · {fmtDate(r.date)}
          </div>
        </div>
        <div style={{ textAlign: 'right', flex: 'none' }}>
          <div style={{ fontSize: 20, fontWeight: 800, color }}>{p === null ? '—' : `${p}%`}</div>
          <div className="faint" style={{ fontSize: 11.5, fontWeight: 700 }}>
            {r.score === null ? 'baho qo\'yilmagan' : `${r.score} / ${r.maxScore}`}
          </div>
        </div>
      </div>
      {p !== null && (
        <div style={{ marginTop: 10, height: 6, borderRadius: 6, background: 'var(--line, #E5E7EB)', overflow: 'hidden' }}>
          <div style={{ width: `${Math.min(100, p)}%`, height: '100%', background: color, borderRadius: 6 }} />
        </div>
      )}
      {r.score !== null && r.total > 0 && (
        <div className="muted" style={{ fontSize: 12, marginTop: 8 }}>
          Guruhda <b>{r.rank}</b>-o'rin ({r.total} ta o'quvchidan)
        </div>
      )}
    </div>
  )
}

export function StudentTestResultsScreen() {
  const navigate = useNavigate()
  const [rows, setRows] = useState<StudentTestResult[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getStudentTestResults()
      .then((d) => alive && setRows(d))
      .catch((e) => alive && setError(e?.response?.data?.message || e?.message || String(e)))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [])

  // O'rtacha — faqat baho qo'yilgan testlar bo'yicha.
  const avg = useMemo(() => {
    const ps = rows.map(pct).filter((p): p is number => p !== null)
    return ps.length ? Math.round(ps.reduce((a, b) => a + b, 0) / ps.length) : null
  }, [rows])

  const head = (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div className="hd-sm" style={{ flex: 1 }}>Test natijalari</div>
      </div>
    </div>
  )

  return (
    <div className="screen">
      {head}
      <div className="scroll" style={{ paddingBottom: 24 }}>
        <div className="pad">
          {loading ? (
            <div className="center" style={{ minHeight: '60dvh' }}>
              <div className="spin" />
            </div>
          ) : error ? (
            <div className="center" style={{ minHeight: '60dvh', flexDirection: 'column', gap: 10 }}>
              <Icon name="alert" size={36} color="var(--red)" />
              <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--red)' }}>Yuklab bo'lmadi</div>
              <div className="muted" style={{ fontSize: 13, textAlign: 'center' }}>{error}</div>
            </div>
          ) : rows.length === 0 ? (
            <div className="center" style={{ minHeight: '55dvh', flexDirection: 'column', gap: 10, textAlign: 'center' }}>
              <Icon name="clipboard" size={40} color="var(--faint)" />
              <div style={{ fontSize: 17, fontWeight: 800 }}>Hali test natijasi yo'q</div>
              <div className="muted" style={{ fontSize: 13 }}>O'qituvchi oylik test ballarini kiritgach shu yerda ko'rinadi.</div>
            </div>
          ) : (
            <>
              {avg !== null && (
                <div className="card" style={{ borderRadius: 18, padding: '14px 16px', marginBottom: 14 }}>
                  <div className="faint" style={{ fontSize: 11, fontWeight: 700 }}>O'RTACHA NATIJA</div>
                  <div style={{ fontSize: 26, fontWeight: 800, color: pctColor(avg) }}>{avg}%</div>
                  <div className="muted" style={{ fontSize: 12 }}>{rows.length} ta test bo'yicha</div>
                </div>
              )}
              {rows.map((r) => (
                <ResultCard key={r.testId} r={r} />
              ))}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
