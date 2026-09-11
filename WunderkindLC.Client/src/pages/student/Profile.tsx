import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import {
  getStudentDashboard,
  getStudentGrades,
  getStudentSchool,
  getStudentCertificates,
  type StudentDashboard,
  type StudentGradesReport,
  type StudentCertificateDto,
} from '@/api/services/studentPortal'
import { Icon, gradeColor, initials, fmtDate } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — PROFIL.
   Dizayn: student.html PROFILE. .student-app shell.
   ============================================================ */

interface MenuEntry {
  icon: string
  label: string
  color: string
  to: string
}

const MENU: MenuEntry[] = [
  { icon: 'grid', label: 'Umumiy statistika', color: '#1B7A66', to: '/student/statistics' },
  { icon: 'chart', label: 'Baholar', color: '#2563EB', to: '/student/grades' },
  { icon: 'checkCircle', label: 'Davomat', color: '#16A34A', to: '/student/attendance' },
  { icon: 'check', label: 'Baholash', color: '#0D9488', to: '/student/grading' },
  { icon: 'sparkle', label: 'AI tekshiruv', color: '#7C3AED', to: '/student/ai-check' },
  { icon: 'wallet', label: "To'lovlar", color: '#7C3AED', to: '/student/finance' },
  { icon: 'award', label: 'Sertifikatlar', color: '#D97706', to: '/student/certificates' },
  { icon: 'file', label: 'Shartnoma', color: '#2563EB', to: '/student/contracts' },
  { icon: 'feedback', label: 'Taklif va shikoyat', color: '#0D9488', to: '/student/feedback' },
  { icon: 'clock', label: 'Support', color: '#0EA5E9', to: '/student/support' },
  { icon: 'pin', label: 'Uy joylashuvi', color: '#DC2626', to: '/student/location' },
  { icon: 'settings', label: 'Sozlamalar', color: '#64748B', to: '/student/settings' },
]

export function StudentProfileScreen() {
  const nav = useNavigate()
  const { user, logout } = useAuth()
  const [dash, setDash] = useState<StudentDashboard | null>(null)
  const [report, setReport] = useState<StudentGradesReport | null>(null)
  const [schoolName, setSchoolName] = useState<string>('')
  const [certCount, setCertCount] = useState<number>(0)
  const [loading, setLoading] = useState(true)
  const [confirmOpen, setConfirmOpen] = useState(false)

  useEffect(() => {
    let alive = true
    setLoading(true)
    getStudentDashboard()
      .then(async (d) => {
        if (!alive) return
        setDash(d)
        try {
          const r = await getStudentGrades()
          if (alive) setReport(r)
        } catch {
          /* ignore */
        }
        try {
          const s = await getStudentSchool()
          if (alive && s?.name) setSchoolName(s.name)
        } catch {
          /* ignore */
        }
        try {
          const certs: StudentCertificateDto[] = await getStudentCertificates()
          if (alive) setCertCount(certs.length)
        } catch {
          /* ignore */
        }
      })
      .catch(() => {})
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [])

  if (loading)
    return (
      <div className="center" style={{ minHeight: '60dvh' }}>
        <div className="spin" />
      </div>
    )

  const p = dash?.profile
  const fullName = p?.fullName || user?.fullName || ''
  const className = p?.className || ''
  const birth = p?.birthDate ? fmtDate(p.birthDate) : ''
  const enroll = p?.enrollmentDate ? fmtDate(p.enrollmentDate) : ''

  const gpaVals = report
    ? Object.values(report.grades || {})
        .map((m) => (m as Record<number, number>)[1])
        .filter((v): v is number => typeof v === 'number')
    : []
  const gpa = gpaVals.length ? gpaVals.reduce((a, b) => a + b, 0) / gpaVals.length : 0

  const MiniStat = ({ value, label, color }: { value: string; label: string; color: string }) => (
    <div style={{ textAlign: 'center' }}>
      <div style={{ fontSize: 19, fontWeight: 800, color }}>{value}</div>
      <div className="muted" style={{ fontSize: 11.5 }}>
        {label}
      </div>
    </div>
  )

  const InfoRow = ({
    icon,
    label,
    value,
    last,
  }: {
    icon: string
    label: string
    value: string
    last?: boolean
  }) => (
    <div
      className="row gap12"
      style={{ padding: '12px 0', borderBottom: last ? undefined : '1px solid var(--border)' }}
    >
      <Icon name={icon} size={19} color="var(--faint)" />
      <span className="muted" style={{ flex: 1, fontSize: 14, fontWeight: 600 }}>
        {label}
      </span>
      <span style={{ fontSize: 14, fontWeight: 700 }}>{value}</span>
    </div>
  )

  const gender = p?.gender ? (p.gender === 'male' ? "O'g'il bola" : 'Qiz bola') : ''
  const parentPhoneTel = (p?.parentPhone || '').replace(/\s/g, '')

  return (
    <div className="screen">
      <div className="scroll" style={{ paddingBottom: 28 }}>
        <div className="hd lg">
          <div className="hd-big">Profil</div>
        </div>

        <div className="pad">
          {/* Profile card */}
          <div className="card" style={{ borderRadius: 22, padding: '22px 18px', textAlign: 'center' }}>
            <div style={{ display: 'inline-block' }}>
              <div
                className="subj"
                style={{
                  width: 78,
                  height: 78,
                  borderRadius: '50%',
                  background: 'linear-gradient(135deg,var(--accent),#5340c4)',
                  color: '#fff',
                  fontSize: 30,
                  boxShadow: '0 0 0 3px var(--surface),0 0 0 5px var(--accent)',
                }}
              >
                {initials(fullName)}
              </div>
            </div>
            <div style={{ fontSize: 20, fontWeight: 800, letterSpacing: '-.3px', marginTop: 14 }}>
              {fullName}
            </div>
            <div className="muted" style={{ fontSize: 13.5 }}>
              {className} guruhi
            </div>
            {schoolName && (
              <div
                className="faint row gap6"
                style={{ fontSize: 12.5, justifyContent: 'center', marginTop: 5 }}
              >
                <Icon name="school" size={14} />
                {schoolName}
              </div>
            )}
            <div className="row" style={{ justifyContent: 'center', gap: 32, marginTop: 18 }}>
              <MiniStat value={gpa > 0 ? gpa.toFixed(2) : '—'} label="O'rtacha" color={gradeColor(gpa)} />
              <MiniStat value={className || '—'} label="Guruh" color="var(--text)" />
            </div>
          </div>

          {/* Shaxsiy ma'lumotlar */}
          <div style={{ height: 16 }} />
          <div className="sh">
            <div className="sh-title">Shaxsiy ma'lumotlar</div>
          </div>
          <div className="card">
            {birth && <InfoRow icon="user" label="Tug'ilgan sana" value={birth} />}
            {enroll && <InfoRow icon="calendar" label="O'qishga qabul" value={enroll} />}
            {gender && <InfoRow icon="user" label="Jinsi" value={gender} last />}
            {!birth && !enroll && !gender && (
              <div className="muted" style={{ padding: 8, fontSize: 13 }}>
                Ma'lumot yo'q
              </div>
            )}
          </div>

          {/* Ota-ona */}
          {p?.parentFullName && (
            <>
              <div style={{ height: 16 }} />
              <div className="sh">
                <div className="sh-title">Ota-ona</div>
              </div>
              <div className="card row gap12" style={{ padding: 14 }}>
                <div
                  className="subj"
                  style={{
                    width: 46,
                    height: 46,
                    borderRadius: '50%',
                    flex: 'none',
                    background: 'linear-gradient(135deg,var(--accent),#5340c4)',
                    color: '#fff',
                    fontSize: 17,
                  }}
                >
                  {initials(p.parentFullName)}
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 15, fontWeight: 700 }}>{p.parentFullName}</div>
                  {p.parentPhone && (
                    <div className="muted" style={{ fontSize: 13 }}>
                      {p.parentPhone}
                    </div>
                  )}
                </div>
                {p.parentPhone && (
                  <a
                    className="press"
                    href={`tel:${parentPhoneTel}`}
                    style={{
                      width: 40,
                      height: 40,
                      borderRadius: 12,
                      background: 'var(--greenSoft)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    <Icon name="phone" size={19} color="var(--green)" />
                  </a>
                )}
              </div>
            </>
          )}

          {/* Menu */}
          <div style={{ height: 16 }} />
          <div className="card" style={{ padding: 4 }}>
            {MENU.map((m, i) => {
              const isCerts = m.to === '/student/certificates'
              return (
                <button
                  key={m.to}
                  className="press row gap12"
                  onClick={() => nav(m.to)}
                  style={{
                    width: '100%',
                    textAlign: 'left',
                    padding: '12px 11px',
                    borderBottom: i === MENU.length - 1 ? undefined : '1px solid var(--border)',
                  }}
                >
                  <div
                    style={{
                      width: 34,
                      height: 34,
                      borderRadius: 10,
                      background: m.color + '22',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      flex: 'none',
                    }}
                  >
                    <Icon name={m.icon} size={18} color={m.color} />
                  </div>
                  <span style={{ flex: 1, fontSize: 15, fontWeight: 700 }}>{m.label}</span>
                  {isCerts && certCount > 0 && (
                    <span
                      style={{
                        fontSize: 11.5,
                        fontWeight: 800,
                        color: '#fff',
                        background: '#D97706',
                        borderRadius: 8,
                        padding: '2px 7px',
                        marginRight: 4,
                      }}
                    >
                      {certCount}
                    </span>
                  )}
                  <Icon name="chevR" size={18} color="var(--faint)" />
                </button>
              )
            })}
          </div>

          {/* Chiqish */}
          <div style={{ height: 16 }} />
          <button
            className="btn btn-danger btn-lg press"
            onClick={() => setConfirmOpen(true)}
          >
            <Icon name="logout" size={18} />
            <span>Chiqish</span>
          </button>
          <div className="faint" style={{ textAlign: 'center', fontSize: 12, marginTop: 12 }}>
            Wunderkind · v1.0.0
          </div>
        </div>
      </div> {/* end .scroll */}

      {/* Logout confirm sheet */}
      {confirmOpen && (
        <div className="scrim" onClick={(e) => e.target === e.currentTarget && setConfirmOpen(false)}>
          <div className="sheet">
            <div className="grab" />
            <div
              style={{ fontSize: 19, fontWeight: 800, letterSpacing: '-.3px', marginBottom: 14 }}
            >
              Chiqishni tasdiqlang
            </div>
            <div className="muted" style={{ fontSize: 14, marginBottom: 16 }}>
              Akkauntdan chiqmoqchimisiz?
            </div>
            <button
              className="btn btn-danger btn-lg press"
              onClick={() => {
                setConfirmOpen(false)
                logout()
              }}
            >
              <span>Chiqish</span>
            </button>
            <div style={{ height: 8 }} />
            <button className="btn btn-ghost btn-lg press" onClick={() => setConfirmOpen(false)}>
              <span>Bekor qilish</span>
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
