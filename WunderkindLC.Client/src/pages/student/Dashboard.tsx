import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Bell,
  CalendarCheck,
  CalendarDays,
  ChevronRight,
  CircleCheck,
  ClipboardList,
  Clock,
  MapPin,
  Send,
  Trophy,
  UserRound,
  Wallet,
  X,
} from 'lucide-react'
import {
  getStudentDashboard,
  getStudentPortalGroups,
  getStudentSchool,
  getStudentTestResults,
  getStudentNotifications,
  markStudentNotificationsRead,
  confirmStudentNotification,
  type NotificationsResponse,
  type StudentTestResult,
} from '@/api/services/studentPortal'
import { getStudentRatingSummary } from '@/api/services/rating'
import { getMyAchievements } from '@/api/services/achievements'
import { useAuth } from '@/context/auth-context'
import { telegramUrl, openTelegram, apiErrorMessage } from '@/lib/utils'
import { fmtScore, monthKeyOf } from '@/lib/ratingPeriods'
import { fmtDate, fmtMoney, fmtTime } from '@/pages/student/lib'
import {
  balanceState,
  courseTitle,
  dayLabel,
  greeting,
  liveGroups,
  nextLesson,
  percentTone,
  timeRange,
  todayLine,
  toneColors,
} from '@/pages/student/format'
import { useApi, useNow } from '@/pages/student/hooks'
import { Avatar, IconBox, SectionTitle, Skeleton, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Student / parent cabinet — HOME.
   What a parent opens the app for, top to bottom: who (child + groups) and the next lesson ·
   payment status with a pay action · this month (attendance, rating, tests) · latest test
   results · achievements. Every block loads independently (one failing request never blanks
   the page). Data: existing student endpoints only.
   ============================================================ */

const NOTIF_ICON: Record<string, typeof Bell> = {
  grade: ClipboardList,
  attendance: CalendarCheck,
  payment: Wallet,
  announcement: Bell,
  pickup: UserRound,
}

// Stable fetchers (useApi effect dependencies).
const loadDash = () => getStudentDashboard()
const loadGroups = () => getStudentPortalGroups()
const loadSchool = () => getStudentSchool()
const loadTests = () => getStudentTestResults()
const loadAchievements = () => getMyAchievements()
const loadMonth = () => getStudentRatingSummary({ period: 'month', key: monthKeyOf(new Date()) })

const pct = (r: StudentTestResult) => (r.score === null || r.maxScore <= 0 ? null : Math.round((r.score / r.maxScore) * 100))

export function StudentDashboardScreen() {
  const { user } = useAuth()
  const now = useNow()
  const dash = useApi(loadDash)
  const groups = useApi(loadGroups)
  const school = useApi(loadSchool)
  const tests = useApi(loadTests)
  const achievements = useApi(loadAchievements)
  const month = useApi(loadMonth)

  const [notifs, setNotifs] = useState<NotificationsResponse | null>(null)
  const [notifOpen, setNotifOpen] = useState(false)
  /** Announcement being confirmed and its error (shown under that notification). */
  const [confirming, setConfirming] = useState<string | null>(null)
  const [confirmError, setConfirmError] = useState<{ id: string; message: string } | null>(null)

  useEffect(() => {
    let on = true
    getStudentNotifications()
      .then((n) => on && setNotifs(n))
      // Background read: without it the bell simply shows no badge.
      .catch(() => {})
    return () => {
      on = false
    }
  }, [])

  // Opening the bell marks everything as read.
  const openNotifs = () => {
    setNotifOpen(true)
    if (notifs && notifs.unread > 0) {
      // Secondary "read" marker — allowed to fail silently (error-visibility.md §4).
      markStudentNotificationsRead().catch(() => {})
      setNotifs((p) => (p ? { unread: 0, items: p.items.map((i) => ({ ...i, read: true })) } : p))
    }
  }

  // ⚠️ "Tasdiqlandi" appears ONLY after the server accepted it (the admin sees this state).
  const confirmNotif = async (id: string) => {
    if (confirming) return
    setConfirming(id)
    setConfirmError(null)
    try {
      await confirmStudentNotification(id)
      setNotifs((p) => (p ? { ...p, items: p.items.map((i) => (i.id === id ? { ...i, confirmed: true } : i)) } : p))
    } catch (e) {
      setConfirmError({ id, message: apiErrorMessage(e, "Tasdiqlab bo'lmadi. Qayta urinib ko'ring.") })
    } finally {
      setConfirming(null)
    }
  }

  const fullName = dash.data?.profile?.fullName || user?.fullName || ''
  const live = useMemo(() => liveGroups(groups.data ?? []), [groups.data])
  const next = useMemo(() => (groups.data ? nextLesson(groups.data, now) : null), [groups.data, now])
  const channel = (school.data?.telegramChannel || '').trim()
  const latestTests = useMemo(
    () => [...(tests.data ?? [])].sort((a, b) => (b.date || '').localeCompare(a.date || '')).slice(0, 3),
    [tests.data],
  )

  return (
    <div className="screen">
      <div className="st-hd">
        <div className="st-hd-row">
          <div style={{ flex: 1, minWidth: 0 }}>
            <div className="st-hd-sub" style={{ marginTop: 0 }}>{todayLine(now)}</div>
            <div className="st-hd-title" style={{ fontSize: 24 }}>{greeting(now)}!</div>
          </div>
          <button className="iconbtn press" onClick={openNotifs} aria-label="Bildirishnomalar">
            <Bell size={20} />
            {notifs && notifs.unread > 0 && <span className="nbadge">{notifs.unread > 9 ? '9+' : notifs.unread}</span>}
          </button>
        </div>
      </div>

      <div className="scroll">
        <div className="st-page">
          {/* Who + next lesson */}
          <div className="hero" style={{ padding: 16 }}>
            <div className="row gap12" style={{ alignItems: 'center' }}>
              {fullName ? (
                <Avatar name={fullName} size={52} gradient="rgba(255,255,255,.18)" />
              ) : (
                <Skeleton h={52} w={52} r={26} style={{ opacity: 0.4 }} />
              )}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="st-wrap" style={{ fontSize: 19, fontWeight: 800, lineHeight: 1.25, letterSpacing: '-0.2px' }}>
                  {fullName || ' '}
                </div>
                <div className="st-wrap" style={{ fontSize: 13.5, fontWeight: 600, opacity: 0.85, marginTop: 2, lineHeight: 1.35 }}>
                  {live.length ? live.map(courseTitle).join(' · ') : school.data?.name || "O'quvchi kabineti"}
                </div>
              </div>
            </div>
            <NextLessonPanel loading={groups.loading && !groups.data} error={groups.error} next={next} />
          </div>

          {/* Payment */}
          <PaymentCard loading={dash.loading && !dash.data} error={dash.error} onRetry={dash.reload} balance={dash.data?.balance ?? 0} monthlyFee={dash.data?.monthlyFee ?? 0} />

          {/* This month */}
          <SectionTitle title={month.data ? `${month.data.period.label}` : 'Bu oy'} sub="Davomat, reyting va testlar" to="/student/rating" action="Batafsil" />
          {month.loading && !month.data ? (
            <div className="st-kpis">
              {[0, 1, 2].map((i) => (
                <div key={i} className="st-kpi">
                  <Skeleton h={22} w="60%" />
                  <Skeleton h={12} w="80%" style={{ marginTop: 8 }} />
                </div>
              ))}
            </div>
          ) : month.data ? (
            <div className="st-kpis">
              <Kpi
                to="/student/attendance"
                value={month.data.attendance.percent != null ? `${month.data.attendance.percent}%` : '—'}
                label="Davomat"
                sub={month.data.attendance.held > 0 ? `${month.data.attendance.attended}/${month.data.attendance.held} dars` : "dars o'tilmagan"}
                tone={attendanceTone(month.data.attendance.percent)}
              />
              <Kpi
                to="/student/rating"
                value={month.data.centre.rank != null ? `${month.data.centre.rank}-o'rin` : '—'}
                label="Reyting"
                sub={month.data.centre.rank != null ? `${month.data.centre.total} o'quvchidan` : "hali o'rin yo'q"}
                tone="violet"
              />
              <Kpi
                to="/student/tests"
                value={month.data.components.testAvg != null ? `${fmtScore(month.data.components.testAvg)}%` : '—'}
                label="Testlar"
                sub={month.data.components.testCount > 0 ? `${month.data.components.testCount} ta test` : "test bo'lmagan"}
                tone={percentTone(month.data.components.testAvg)}
              />
            </div>
          ) : (
            <div className="card" style={{ padding: 14 }}>
              <div className="muted" style={{ fontSize: 14 }}>
                Oylik ko'rsatkichlarni yuklab bo'lmadi.{' '}
                <button className="press" onClick={month.reload} style={{ color: 'var(--accent)', fontWeight: 700 }}>
                  Qayta urinish
                </button>
              </div>
            </div>
          )}

          {/* Latest test results */}
          <SectionTitle title="So'nggi test natijalari" to={latestTests.length ? '/student/tests' : undefined} />
          {tests.loading && !tests.data ? (
            <div className="card flush">
              {[0, 1].map((i) => (
                <div key={i} className="st-li">
                  <Skeleton h={40} w={40} r={12} />
                  <div style={{ flex: 1 }}>
                    <Skeleton h={14} w="55%" />
                    <Skeleton h={12} w="35%" style={{ marginTop: 8 }} />
                  </div>
                </div>
              ))}
            </div>
          ) : latestTests.length ? (
            <div className="card flush">
              {latestTests.map((r) => {
                const p = pct(r)
                const tone = toneColors(percentTone(p))
                return (
                  <Link key={r.testId} to="/student/tests" className="st-li press">
                    <IconBox icon={ClipboardList} tone={percentTone(p) === 'neutral' ? 'info' : percentTone(p)} />
                    <div className="st-li-main">
                      <div className="st-li-title">{r.name}</div>
                      <div className="st-li-sub">{fmtDate(r.date)}{r.groupName ? ` · ${r.groupName}` : ''}</div>
                    </div>
                    <div className="st-li-end">
                      <div className="st-num" style={{ fontSize: 18, color: tone.fg }}>{p === null ? '—' : `${p}%`}</div>
                      <div className="faint" style={{ fontSize: 12, fontWeight: 600 }}>
                        {r.score === null ? 'kiritilmagan' : `${fmtScore(r.score)}/${r.maxScore}`}
                      </div>
                    </div>
                  </Link>
                )
              })}
            </div>
          ) : (
            <div className="card row gap12">
              <IconBox icon={ClipboardList} tone="neutral" />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 15, fontWeight: 700 }}>{tests.error ? "Natijalarni yuklab bo'lmadi" : "Hali test natijasi yo'q"}</div>
                <div className="muted" style={{ fontSize: 13, marginTop: 2 }}>
                  {tests.error ? tests.error : "O'qituvchi oylik test ballarini kiritgach shu yerda ko'rinadi."}
                </div>
              </div>
            </div>
          )}

          {/* Achievements — only when there are any (no empty clutter) */}
          {(achievements.data ?? []).length > 0 && (
            <>
              <SectionTitle title="Yutuqlar" to="/student/certificates" />
              <div className="card flush">
                {(achievements.data ?? []).slice(0, 3).map((a) => (
                  <Link key={a.id} to="/student/certificates" className="st-li press">
                    <IconBox icon={Trophy} color="#d97706" />
                    <div className="st-li-main">
                      <div className="st-li-title">{a.title}</div>
                      <div className="st-li-sub">{a.kindLabel}{a.date ? ` · ${fmtDate(a.date)}` : ''}</div>
                    </div>
                    {a.result && <div className="st-li-end st-num" style={{ fontSize: 15, color: 'var(--amber)' }}>{a.result}</div>}
                  </Link>
                ))}
              </div>
            </>
          )}

          {/* Telegram channel (configured; student account) */}
          {channel && user?.role === 'student' && (
            <a
              href={telegramUrl(channel)}
              onClick={(e) => {
                e.preventDefault()
                openTelegram(channel)
              }}
              className="card press row gap12"
              style={{ marginTop: 22, textDecoration: 'none', color: 'inherit' }}
            >
              <div className="st-ic" style={{ background: '#229ED9', color: '#fff' }}>
                <Send size={20} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 15, fontWeight: 700 }}>Telegram kanalimiz</div>
                <div className="muted" style={{ fontSize: 13 }}>Markaz e'lonlari va yangiliklari</div>
              </div>
              <ChevronRight size={18} color="var(--faint)" />
            </a>
          )}
        </div>
      </div>

      {notifOpen && (
        <div className="scrim" onClick={() => setNotifOpen(false)}>
          <div className="sheet" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Bildirishnomalar">
            <div className="grab" />
            <div className="row sp" style={{ marginBottom: 12 }}>
              <div style={{ fontSize: 18, fontWeight: 800 }}>Bildirishnomalar</div>
              <button className="iconbtn press" onClick={() => setNotifOpen(false)} aria-label="Yopish">
                <X size={18} />
              </button>
            </div>
            {!notifs || notifs.items.length === 0 ? (
              <div className="empty" style={{ padding: '24px 12px' }}>
                <div className="empty-ic">
                  <Bell size={26} />
                </div>
                <div className="st-empty-title">Bildirishnoma yo'q</div>
                <div className="st-empty-sub">Yangi e'lonlar va baholar shu yerda ko'rinadi.</div>
              </div>
            ) : (
              <div className="col" style={{ gap: 8, paddingBottom: 8 }}>
                {notifs.items.map((n) => {
                  const Ic = NOTIF_ICON[n.type] || Bell
                  return (
                    <div key={n.id} className="row gap12" style={{ alignItems: 'flex-start', padding: 12, borderRadius: 14, background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                      <IconBox icon={Ic} tone="info" size={36} />
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div className="st-wrap" style={{ fontSize: 15, fontWeight: 700 }}>{n.title}</div>
                        {n.body && <div className="muted st-wrap" style={{ fontSize: 14, marginTop: 2, lineHeight: 1.45 }}>{n.body}</div>}
                        <div className="faint" style={{ fontSize: 12, marginTop: 4 }}>
                          {fmtDate(n.createdAt)} · {fmtTime(n.createdAt)}
                        </div>
                        {n.type === 'announcement' &&
                          (n.confirmed ? (
                            <div style={{ marginTop: 8 }}>
                              <StatusChip tone="ok" icon={CircleCheck}>Tasdiqlandi</StatusChip>
                            </div>
                          ) : (
                            <>
                              <button
                                className="btn btn-primary btn-sm press"
                                onClick={() => confirmNotif(n.id)}
                                disabled={confirming === n.id}
                                style={{ marginTop: 10 }}
                              >
                                {confirming === n.id ? 'Yuborilmoqda...' : 'Tasdiqlash'}
                              </button>
                              {confirmError?.id === n.id && (
                                <div role="alert" style={{ marginTop: 6, color: 'var(--red)', fontSize: 13, fontWeight: 600 }}>
                                  {confirmError.message}
                                </div>
                              )}
                            </>
                          ))}
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  )
}

function attendanceTone(p: number | null) {
  if (p == null) return 'neutral' as const
  if (p >= 90) return 'ok' as const
  if (p >= 75) return 'info' as const
  if (p >= 50) return 'warn' as const
  return 'bad' as const
}

function Kpi({ to, value, label, sub, tone }: { to: string; value: string; label: string; sub: string; tone: Parameters<typeof toneColors>[0] }) {
  const c = toneColors(tone)
  return (
    <Link to={to} className="st-kpi press">
      <div className="st-kpi-v" style={{ color: value === '—' ? 'var(--faint)' : c.fg === 'var(--muted)' ? 'var(--text)' : c.fg }}>{value}</div>
      <div className="st-kpi-l" style={{ color: 'var(--text)' }}>{label}</div>
      <div className="st-kpi-s">{sub}</div>
    </Link>
  )
}

function NextLessonPanel({ loading, error, next }: { loading: boolean; error: string | null; next: ReturnType<typeof nextLesson> }) {
  const panel = { marginTop: 14, padding: '12px 14px', borderRadius: 16, background: 'rgba(255,255,255,.14)' } as const
  if (loading)
    return (
      <div style={panel}>
        <Skeleton h={12} w="35%" style={{ opacity: 0.35 }} />
        <Skeleton h={16} w="70%" style={{ opacity: 0.35, marginTop: 10 }} />
      </div>
    )
  if (!next)
    return (
      <Link to="/student/schedule" style={{ ...panel, display: 'flex', alignItems: 'center', gap: 10, color: '#fff', textDecoration: 'none' }}>
        <CalendarDays size={20} style={{ flex: 'none', opacity: 0.9 }} />
        <span style={{ flex: 1, fontSize: 14, fontWeight: 600 }}>{error ? "Dars jadvalini yuklab bo'lmadi" : 'Dars jadvali hali kiritilmagan'}</span>
        <ChevronRight size={18} style={{ flex: 'none', opacity: 0.8 }} />
      </Link>
    )
  const g = next.group
  return (
    <Link to="/student/schedule" style={{ ...panel, display: 'block', color: '#fff', textDecoration: 'none' }}>
      <div className="row sp" style={{ flexWrap: 'wrap', gap: 6 }}>
        <span style={{ fontSize: 12, fontWeight: 800, letterSpacing: '0.6px', textTransform: 'uppercase', opacity: 0.85, whiteSpace: 'nowrap' }}>
          {next.status === 'now' ? 'Hozir darsda' : 'Keyingi dars'}
        </span>
        <span style={{ fontSize: 13, fontWeight: 800, padding: '2px 10px', borderRadius: 999, background: 'rgba(255,255,255,.2)', whiteSpace: 'nowrap' }}>
          {dayLabel(next.dayOffset, next.day)} · {timeRange(g)}
        </span>
      </div>
      <div className="st-wrap" style={{ fontSize: 16.5, fontWeight: 800, marginTop: 8, lineHeight: 1.3 }}>{courseTitle(g)}</div>
      <div className="row" style={{ flexWrap: 'wrap', gap: '4px 14px', marginTop: 6, fontSize: 13.5, fontWeight: 600, opacity: 0.9 }}>
        {g.teacherName && (
          <span className="row gap6">
            <UserRound size={15} /> {g.teacherName}
          </span>
        )}
        {g.room && (
          <span className="row gap6">
            <MapPin size={15} /> {g.room}
          </span>
        )}
        {g.state === 'trial' && (
          <span className="row gap6">
            <Clock size={15} /> Sinov darsi
          </span>
        )}
      </div>
    </Link>
  )
}

function PaymentCard({
  loading,
  error,
  onRetry,
  balance,
  monthlyFee,
}: {
  loading: boolean
  error: string | null
  onRetry: () => void
  balance: number
  monthlyFee: number
}) {
  if (loading)
    return (
      <div className="card" style={{ marginTop: 12 }}>
        <div className="row gap12">
          <Skeleton h={44} w={44} r={13} />
          <div style={{ flex: 1 }}>
            <Skeleton h={12} w="40%" />
            <Skeleton h={22} w="55%" style={{ marginTop: 8 }} />
          </div>
        </div>
      </div>
    )
  if (error)
    return (
      <div className="card row gap12" style={{ marginTop: 12 }}>
        <IconBox icon={Wallet} tone="neutral" />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 700 }}>To'lov holatini yuklab bo'lmadi</div>
          <div className="muted" style={{ fontSize: 13 }}>{error}</div>
        </div>
        <button className="btn btn-soft btn-sm press" onClick={onRetry}>
          Qayta
        </button>
      </div>
    )
  const st = balanceState(balance)
  const c = toneColors(st.tone)
  const debt = balance < 0
  return (
    <div className="card" style={{ marginTop: 12, borderColor: debt ? 'color-mix(in srgb, var(--red) 28%, var(--border))' : undefined }}>
      <Link to="/student/finance" className="row gap12" style={{ color: 'inherit', textDecoration: 'none' }}>
        <IconBox icon={debt ? Wallet : CircleCheck} tone={st.tone} size={44} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="muted" style={{ fontSize: 13.5, fontWeight: 600 }}>{st.label}</div>
          <div className="st-num" style={{ fontSize: 23, lineHeight: 1.2, color: st.amount > 0 ? c.fg : 'var(--text)' }}>
            {st.amount > 0 ? `${fmtMoney(st.amount)} so'm` : "Hammasi to'langan"}
          </div>
          {monthlyFee > 0 && <div className="faint" style={{ fontSize: 13, marginTop: 1 }}>Oylik to'lov: {fmtMoney(monthlyFee)} so'm</div>}
        </div>
        {!debt && <ChevronRight size={18} color="var(--faint)" style={{ flex: 'none' }} />}
      </Link>
      {debt && (
        <Link to="/student/finance" className="btn btn-primary press" style={{ marginTop: 14 }}>
          <Wallet size={18} />
          To'lash
        </Link>
      )}
    </div>
  )
}
