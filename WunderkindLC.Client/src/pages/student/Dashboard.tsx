import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  getStudentDashboard,
  getStudentNotebook,
  getStudentSchool,
  getStudentNotifications,
  markStudentNotificationsRead,
  confirmStudentNotification,
  type StudentDashboard,
  type StudentNotebook,
  type NotificationsResponse,
} from '@/api/services/studentPortal'
import { useAuth } from '@/context/auth-context'
import { Icon, fmtDate, fmtMoney, fmtTime, gradeColor } from '@/pages/student/lib'
import { telegramUrl, openTelegram } from '@/lib/utils'

const NOTIF_ICON: Record<string, string> = {
  grade: 'chart',
  attendance: 'checkCircle',
  payment: 'wallet',
  announcement: 'bell',
  pickup: 'user',
}

/* ============================================================
   O'quvchi Dashboard — to'liq salom + bildirishnoma + qisqacha
   ko'rsatkichlar (dars qoldirish / balans / guruh) + umumiy statistika.
   (O'quv dasturi yo'l-xaritasi "Progress" tabига ko'chirildi.)
   ============================================================ */

const WD = ['Yakshanba', 'Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba']
const MO = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr']
function todayLine(): string {
  const d = new Date()
  return `${d.getDate()}-${MO[d.getMonth()]}, ${WD[d.getDay()]}`
}
const num = (v: unknown): number => (typeof v === 'number' && isFinite(v) ? v : 0)

export function StudentDashboardScreen() {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [dash, setDash] = useState<StudentDashboard | null>(null)
  const [nb, setNb] = useState<StudentNotebook | null>(null)
  const [channel, setChannel] = useState('')
  const [notifs, setNotifs] = useState<NotificationsResponse | null>(null)
  const [loading, setLoading] = useState(true)
  const [notifOpen, setNotifOpen] = useState(false)

  useEffect(() => {
    let on = true
    Promise.all([
      getStudentDashboard().catch(() => null),
      getStudentNotebook().catch(() => null),
      getStudentSchool().catch(() => null),
      getStudentNotifications().catch(() => null),
    ])
      .then(([d, n, s, nt]) => {
        if (!on) return
        setDash(d)
        setNb(n)
        setChannel(s?.telegramChannel || '')
        setNotifs(nt)
      })
      .finally(() => on && setLoading(false))
    return () => {
      on = false
    }
  }, [])

  // Qo'ng'iroq bosilganda — panel ochiladi va o'qilgan deb belgilanadi.
  const openNotifs = () => {
    setNotifOpen(true)
    if (notifs && notifs.unread > 0) {
      markStudentNotificationsRead().catch(() => {})
      setNotifs((p) => (p ? { unread: 0, items: p.items.map((i) => ({ ...i, read: true })) } : p))
    }
  }

  // "Tasdiqlash" tugmasi (admin e'loni uchun) — admin shu holatni ko'radi.
  const confirmNotif = (id: string) => {
    confirmStudentNotification(id).catch(() => {})
    setNotifs((p) => (p ? { ...p, items: p.items.map((i) => (i.id === id ? { ...i, confirmed: true } : i)) } : p))
  }

  const fullName = dash?.profile?.fullName || user?.fullName || "O'quvchi"
  const className = dash?.profile?.className || '—'
  const balance = dash?.balance ?? 0

  // Umumiy statistika (notebook'dan, bo'sh bo'lsa 0)
  const avg = num(nb?.avgGrade)
  const attended = num(nb?.attended)
  const conducted = num(nb?.conducted)
  const missed = Math.max(0, conducted - attended)
  const attPct = num(nb?.attendancePct)
  const hwDone = num(nb?.homeworkDone)
  const hwMissed = num(nb?.homeworkMissed)
  const hwPct = hwDone + hwMissed > 0 ? Math.round((hwDone / (hwDone + hwMissed)) * 100) : 0

  return (
    <div className="screen">
      {/* Salom + bildirishnoma */}
      <div className="hd" style={{ paddingTop: 10, paddingBottom: 6 }}>
        <div className="row sp gap10">
          <div style={{ minWidth: 0, flex: 1 }}>
            <div className="muted" style={{ fontSize: 12.5, fontWeight: 600 }}>{todayLine()}</div>
            <div style={{ fontSize: 21, fontWeight: 800, letterSpacing: '-.3px', lineHeight: 1.15 }}>
              Salom, <span style={{ color: 'var(--accent)' }}>{fullName}</span> {'\u{1F44B}'}
            </div>
          </div>
          <button className="iconbtn press" onClick={openNotifs} aria-label="Bildirishnomalar">
            <Icon name="bell" size={20} color="var(--text)" />
            {notifs && notifs.unread > 0 && (
              <span className="nbadge">{notifs.unread > 9 ? '9+' : notifs.unread}</span>
            )}
          </button>
        </div>
      </div>

      <div className="scroll">
        {loading ? (
          <div className="center" style={{ minHeight: '40dvh' }}>
            <div className="spin" />
          </div>
        ) : (
          <div className="pad" style={{ paddingBottom: 28 }}>
            {/* Qisqacha: dars qoldirish · balans · guruh */}
            <div style={{ display: 'flex', gap: 10, marginTop: 4 }}>
              <Quick
                icon="alert"
                label="Dars qoldirdi"
                value={`${missed}`}
                tone={missed > 0 ? 'var(--amber)' : 'var(--muted)'}
              />
              <Quick
                icon="wallet"
                label="Balans"
                value={fmtMoney(balance)}
                tone={balance < 0 ? 'var(--red)' : balance > 0 ? 'var(--green)' : 'var(--muted)'}
                small
              />
              <Quick icon="school" label="Guruh" value={className} tone="var(--accent)" small />
            </div>

            {/* Telegram kanal (sozlangan bo'lsa, faqat o'quvchi) */}
            {channel.trim() && user?.role === 'student' && (
              <a
                href={telegramUrl(channel)}
                onClick={(e) => {
                  e.preventDefault()
                  openTelegram(channel)
                }}
                className="card press"
                style={{ marginTop: 14, display: 'flex', alignItems: 'center', gap: 12, textDecoration: 'none' }}
              >
                <div
                  style={{
                    width: 42,
                    height: 42,
                    borderRadius: 13,
                    background: '#229ED9',
                    color: '#fff',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flex: 'none',
                  }}
                >
                  <Icon name="send" size={20} color="#fff" />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--text)' }}>Telegram kanalimiz</div>
                  <div className="muted" style={{ fontSize: 12 }}>
                    Markaz e'lonlari — kanalga o'ting
                  </div>
                </div>
                <Icon name="chevR" size={18} color="var(--faint)" />
              </a>
            )}

            {/* Umumiy statistika */}
            <div className="sh" style={{ marginTop: 18 }}>
              <div className="sh-title">Umumiy statistika</div>
              <button
                className="sh-act press"
                onClick={() => navigate('/student/statistics')}
                style={{ background: 'none' }}
              >
                Batafsil
                <Icon name="chevR" size={16} color="var(--accent)" />
              </button>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 8 }}>
              <Stat icon="chart" label="O'rtacha baho" value={avg > 0 ? avg.toFixed(2) : '—'} color={gradeColor(avg)} />
              <Stat
                icon="checkCircle"
                label="Davomat"
                value={`${attPct}%`}
                color="var(--green)"
                sub={conducted > 0 ? `${attended}/${conducted} dars` : undefined}
              />
              <Stat icon="checkCircle" label="Uy vazifa" value={`${hwPct}%`} color="var(--violet)" />
            </div>
          </div>
        )}
      </div>

      {/* Bildirishnomalar paneli */}
      {notifOpen && (
        <div className="scrim" onClick={() => setNotifOpen(false)}>
          <div className="sheet" onClick={(e) => e.stopPropagation()}>
            <div className="grab" />
            <div className="row sp" style={{ marginBottom: 8 }}>
              <div style={{ fontSize: 17, fontWeight: 800 }}>Bildirishnomalar</div>
              <button className="iconbtn press" onClick={() => setNotifOpen(false)} aria-label="Yopish">
                <Icon name="x" size={18} color="var(--text)" />
              </button>
            </div>
            {!notifs || notifs.items.length === 0 ? (
              <div className="empty" style={{ paddingTop: 24, paddingBottom: 24 }}>
                <div className="empty-ic">
                  <Icon name="bell" size={26} color="var(--faint)" />
                </div>
                <div style={{ fontWeight: 800, fontSize: 15 }}>Bildirishnoma yo'q</div>
                <div className="muted" style={{ fontSize: 13, marginTop: 6 }}>
                  Yangi e'lon va baholar shu yerda ko'rinadi.
                </div>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8, paddingBottom: 8 }}>
                {notifs.items.map((n) => (
                  <div
                    key={n.id}
                    className="row gap10"
                    style={{
                      alignItems: 'flex-start',
                      padding: 12,
                      borderRadius: 14,
                      background: 'var(--surface2)',
                      border: '1px solid var(--border)',
                    }}
                  >
                    <div
                      style={{
                        width: 36,
                        height: 36,
                        borderRadius: 11,
                        background: 'var(--accentSoft)',
                        color: 'var(--accent)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        flex: 'none',
                      }}
                    >
                      <Icon name={NOTIF_ICON[n.type] || 'bell'} size={18} color="var(--accent)" />
                    </div>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div style={{ fontSize: 14, fontWeight: 800, color: 'var(--text)' }}>{n.title}</div>
                      {n.body && (
                        <div className="muted" style={{ fontSize: 13, marginTop: 2, lineHeight: 1.4 }}>
                          {n.body}
                        </div>
                      )}
                      <div className="faint" style={{ fontSize: 11, marginTop: 4 }}>
                        {fmtDate(n.createdAt)} · {fmtTime(n.createdAt)}
                      </div>
                      {n.type === 'announcement' &&
                        (n.confirmed ? (
                          <div
                            style={{ marginTop: 8, display: 'flex', alignItems: 'center', gap: 4, fontSize: 12, fontWeight: 700, color: 'var(--green)' }}
                          >
                            <Icon name="checkCircle" size={14} color="var(--green)" /> Tasdiqlandi
                          </div>
                        ) : (
                          <button
                            className="press"
                            onClick={() => confirmNotif(n.id)}
                            style={{ marginTop: 8, padding: '7px 16px', borderRadius: 10, background: 'var(--accent)', color: '#fff', fontSize: 13, fontWeight: 700 }}
                          >
                            Tasdiqlash
                          </button>
                        ))}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function Quick({
  icon,
  label,
  value,
  tone,
  small,
}: {
  icon: string
  label: string
  value: string
  tone: string
  small?: boolean
}) {
  return (
    <div className="card" style={{ flex: 1, minWidth: 0, padding: 12 }}>
      <div
        style={{
          width: 30,
          height: 30,
          borderRadius: 9,
          background: `color-mix(in srgb,${tone} 14%,transparent)`,
          color: tone,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Icon name={icon} size={17} color={tone} />
      </div>
      <div
        style={{
          marginTop: 8,
          fontSize: small ? 14 : 20,
          fontWeight: 800,
          color: tone,
          whiteSpace: 'nowrap',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        }}
      >
        {value}
      </div>
      <div className="muted" style={{ fontSize: 10.5, marginTop: 2 }}>
        {label}
      </div>
    </div>
  )
}

function Stat({
  icon,
  label,
  value,
  color,
  sub,
}: {
  icon: string
  label: string
  value: string
  color: string
  sub?: string
}) {
  return (
    <div className="card" style={{ padding: 14 }}>
      <div className="row sp">
        <div
          style={{
            width: 32,
            height: 32,
            borderRadius: 10,
            background: `color-mix(in srgb,${color} 14%,transparent)`,
            color,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <Icon name={icon} size={18} color={color} />
        </div>
      </div>
      <div style={{ fontSize: 22, fontWeight: 800, color, marginTop: 8 }}>{value}</div>
      <div className="muted" style={{ fontSize: 11.5, marginTop: 1 }}>
        {label}
      </div>
      {sub && (
        <div className="faint" style={{ fontSize: 10.5, marginTop: 1 }}>
          {sub}
        </div>
      )}
    </div>
  )
}
