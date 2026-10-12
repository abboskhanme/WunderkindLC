import { useState } from 'react'
import {
  Award,
  CalendarDays,
  FileText,
  GraduationCap,
  LifeBuoy,
  ListChecks,
  LogOut,
  MapPin,
  MessageSquare,
  Phone,
  Settings,
  Sparkles,
  UserRound,
  type LucideIcon,
} from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { getStudentMe, getStudentPortalGroups, getStudentSchool, getStudentCertificates } from '@/api/services/studentPortal'
import { getMyAchievements } from '@/api/services/achievements'
import { formatPhone } from '@/lib/numberFormat'
import { fmtDate } from '@/pages/student/lib'
import { courseTitle } from '@/pages/student/format'
import { useApi } from '@/pages/student/hooks'
import { Avatar, ErrorState, ListRow, PageHeader, Skeleton, StatusChip } from '@/pages/student/ui'

/* ============================================================
   PROFIL — who the account belongs to (the same account is used by the student and the parents),
   their groups, personal data, and every secondary screen (documents, tools, contact, settings).
   Lessons, results and payments live in the bottom navigation, so they are not repeated here.
   ============================================================ */

const loadMe = () => getStudentMe()
const loadGroups = () => getStudentPortalGroups()
const loadSchool = () => getStudentSchool()
const loadCertCount = async () => {
  // Badge only — certificates + achievements; failures just hide the badge.
  const [c, a] = await Promise.all([getStudentCertificates().catch(() => []), getMyAchievements().catch(() => [])])
  return c.length + a.length
}

interface MenuEntry {
  icon: LucideIcon
  label: string
  sub?: string
  color: string
  to: string
}

const MENU: { title: string; items: MenuEntry[] }[] = [
  {
    title: 'Hujjatlar',
    items: [
      { icon: Award, label: 'Sertifikatlar va yutuqlar', color: '#d97706', to: '/student/certificates' },
      { icon: FileText, label: 'Shartnoma', color: '#2563eb', to: '/student/contracts' },
    ],
  },
  {
    title: "O'qish",
    items: [
      { icon: ListChecks, label: 'Baholash mezonlari', sub: "Har dars va oy bo'yicha ball", color: '#0d9488', to: '/student/grading' },
      { icon: Sparkles, label: 'AI tekshiruv', sub: 'Writing va Speaking', color: '#7c3aed', to: '/student/ai-check' },
      { icon: LifeBuoy, label: 'Support darslar', sub: "Qo'shimcha dars uchun vaqt band qilish", color: '#0284c7', to: '/student/support' },
    ],
  },
  {
    title: 'Markaz bilan aloqa',
    items: [
      { icon: MessageSquare, label: 'Taklif va shikoyat', color: '#0d9488', to: '/student/feedback' },
      { icon: MapPin, label: 'Uy joylashuvi', color: '#dc2626', to: '/student/location' },
      { icon: Settings, label: 'Sozlamalar', sub: 'Mavzu, til, parol', color: '#64748b', to: '/student/settings' },
    ],
  },
]

const GROUP_STATE: Record<string, { label: string; tone: 'ok' | 'violet' | 'info' | 'neutral' }> = {
  active: { label: 'Faol', tone: 'ok' },
  trial: { label: 'Sinov', tone: 'violet' },
  frozen: { label: 'Muzlatilgan', tone: 'info' },
}

export function StudentProfileScreen() {
  const { user, logout } = useAuth()
  const me = useApi(loadMe)
  const groups = useApi(loadGroups)
  const school = useApi(loadSchool)
  const certCount = useApi(loadCertCount)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const p = me.data
  const fullName = p?.fullName || user?.fullName || ''
  const current = (groups.data ?? []).filter((g) => g.state in GROUP_STATE)
  const gender = p?.gender ? (p.gender === 'male' ? "O'g'il bola" : 'Qiz bola') : ''
  const info: [LucideIcon, string, string][] = [
    p?.birthDate ? [CalendarDays, "Tug'ilgan sana", fmtDate(p.birthDate)] : null,
    p?.enrollmentDate ? [GraduationCap, "O'qishga qabul qilingan", fmtDate(p.enrollmentDate)] : null,
    gender ? [UserRound, 'Jinsi', gender] : null,
  ].filter((x): x is [LucideIcon, string, string] => !!x)

  return (
    <div className="screen">
      <PageHeader title="Profil" />
      <div className="scroll">
        <div className="st-page">
          {me.error && !p ? (
            <div className="card flush" style={{ marginBottom: 12 }}>
              <ErrorState message={me.error} onRetry={me.reload} />
            </div>
          ) : (
            <div className="card" style={{ padding: 18 }}>
              <div className="row gap12" style={{ alignItems: 'center' }}>
                {fullName ? <Avatar name={fullName} size={60} /> : <Skeleton h={60} w={60} r={30} />}
                <div style={{ flex: 1, minWidth: 0 }}>
                  {fullName ? (
                    <div className="st-wrap" style={{ fontSize: 20, fontWeight: 800, lineHeight: 1.25, letterSpacing: '-0.3px' }}>
                      {fullName}
                    </div>
                  ) : (
                    <Skeleton h={20} w="70%" />
                  )}
                  <div className="muted" style={{ fontSize: 13.5, marginTop: 3 }}>
                    O'quvchi{school.data?.name ? ` · ${school.data.name}` : ''}
                  </div>
                </div>
              </div>

              {/* Groups — full names, wrapped (never cut) */}
              {current.length > 0 && (
                <div className="col" style={{ gap: 8, marginTop: 16 }}>
                  {current.map((g) => {
                    const st = GROUP_STATE[g.state]
                    return (
                      <div key={g.groupId} className="row gap10" style={{ alignItems: 'flex-start', padding: '10px 12px', borderRadius: 14, background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                        <div style={{ flex: 1, minWidth: 0 }}>
                          <div className="st-wrap" style={{ fontSize: 14.5, fontWeight: 700, lineHeight: 1.3 }}>{courseTitle(g)}</div>
                          <div className="muted st-wrap" style={{ fontSize: 13, marginTop: 2 }}>
                            {[g.teacherName, g.name !== courseTitle(g) ? g.name : ''].filter(Boolean).join(' · ')}
                          </div>
                        </div>
                        <StatusChip tone={st.tone}>{st.label}</StatusChip>
                      </div>
                    )
                  })}
                </div>
              )}

              {info.length > 0 && (
                <div style={{ marginTop: 14 }}>
                  {info.map(([Ic, label, value], i) => (
                    <div key={label} className="row gap12" style={{ padding: '10px 0', borderTop: i === 0 ? '1px solid var(--border)' : undefined, borderBottom: i < info.length - 1 ? '1px solid var(--border)' : undefined }}>
                      <Ic size={18} color="var(--faint)" style={{ flex: 'none' }} />
                      <span className="muted" style={{ flex: 1, fontSize: 14 }}>{label}</span>
                      <span style={{ fontSize: 14, fontWeight: 700, textAlign: 'right' }}>{value}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* Parent */}
          {p?.parentFullName && (
            <>
              <div className="st-label" style={{ marginTop: 20, textTransform: 'uppercase', letterSpacing: '0.4px', fontSize: 12.5 }}>
                Ota-ona
              </div>
              <div className="card row gap12" style={{ padding: 14 }}>
                <Avatar name={p.parentFullName} size={44} gradient="linear-gradient(135deg,#0d9488,#0f766e)" />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="st-wrap" style={{ fontSize: 15, fontWeight: 700 }}>{p.parentFullName}</div>
                  {p.parentPhone && <div className="muted" style={{ fontSize: 13.5 }}>{formatPhone(p.parentPhone)}</div>}
                </div>
                {p.parentPhone && (
                  <a className="st-ic press" href={`tel:${p.parentPhone.replace(/[^\d+]/g, '')}`} aria-label="Qo'ng'iroq qilish" style={{ background: 'var(--greenSoft)', color: 'var(--green)' }}>
                    <Phone size={19} />
                  </a>
                )}
              </div>
            </>
          )}

          {MENU.map((sec) => (
            <div key={sec.title}>
              <div className="st-label" style={{ marginTop: 20, textTransform: 'uppercase', letterSpacing: '0.4px', fontSize: 12.5 }}>
                {sec.title}
              </div>
              <div className="card flush">
                {sec.items.map((m) => (
                  <ListRow
                    key={m.to}
                    to={m.to}
                    icon={m.icon}
                    iconColor={m.color}
                    title={m.label}
                    sub={m.sub}
                    end={
                      m.to === '/student/certificates' && (certCount.data ?? 0) > 0 ? (
                        <span className="st-chip warn">{certCount.data}</span>
                      ) : undefined
                    }
                  />
                ))}
              </div>
            </div>
          ))}

          <button className="btn btn-danger press" onClick={() => setConfirmOpen(true)} style={{ marginTop: 24 }}>
            <LogOut size={18} />
            Akkauntdan chiqish
          </button>
          <div className="faint" style={{ textAlign: 'center', fontSize: 12.5, marginTop: 12 }}>
            Wunderkind · v1.0.0
          </div>
        </div>
      </div>

      {confirmOpen && (
        <div className="scrim" onClick={(e) => e.target === e.currentTarget && setConfirmOpen(false)}>
          <div className="sheet" role="dialog" aria-label="Chiqishni tasdiqlang">
            <div className="grab" />
            <div style={{ fontSize: 19, fontWeight: 800, letterSpacing: '-0.3px' }}>Akkauntdan chiqasizmi?</div>
            <div className="muted" style={{ fontSize: 14.5, margin: '6px 0 18px', lineHeight: 1.45 }}>
              Qayta kirish uchun login va parol kerak bo'ladi.
            </div>
            <button
              className="btn btn-danger btn-lg press"
              onClick={() => {
                setConfirmOpen(false)
                logout()
              }}
            >
              Chiqish
            </button>
            <div style={{ height: 8 }} />
            <button className="btn btn-ghost btn-lg press" onClick={() => setConfirmOpen(false)}>
              Bekor qilish
            </button>
          </div>
        </div>
      )}
    </div>
  )
}
