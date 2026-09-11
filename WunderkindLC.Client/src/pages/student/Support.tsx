import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Icon, fmtDate, initials } from '@/pages/student/lib'
import {
  getStudentSupport,
  bookSupportSlot,
  cancelSupportSlot,
  type StudentSupport,
  type StudentSupportTeacher,
} from '@/api/services/support'

/* ============================================================
   O'quvchi portali — SUPPORT.
   Support o'qituvchilari bo'sh vaqt e'lon qiladi; o'quvchi
   tanlab bron qiladi. Slotlar o'qituvchi bo'yicha accordion,
   har o'qituvchi ichida kun bo'yicha guruhlangan.
   ============================================================ */

type OpenSlot = StudentSupportTeacher['openSlots'][number]

function groupByDate(slots: OpenSlot[]): { date: string; slots: OpenSlot[] }[] {
  const map = new Map<string, OpenSlot[]>()
  for (const s of slots) {
    const arr = map.get(s.date) ?? []
    arr.push(s)
    map.set(s.date, arr)
  }
  return Array.from(map.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, daySlots]) => ({
      date,
      slots: [...daySlots].sort((a, b) => a.startTime.localeCompare(b.startTime)),
    }))
}

function TeacherAvatar({ t }: { t: StudentSupportTeacher }) {
  if (t.photoUrl) {
    return (
      <img
        src={t.photoUrl}
        alt=""
        style={{ width: 44, height: 44, borderRadius: 14, objectFit: 'cover', flex: 'none' }}
      />
    )
  }
  return (
    <div
      style={{
        width: 44,
        height: 44,
        borderRadius: 14,
        background: 'var(--accentSoft)',
        color: 'var(--accent)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: 15,
        fontWeight: 800,
        flex: 'none',
      }}
    >
      {initials(t.fullName)}
    </div>
  )
}

function TeacherCard({
  t,
  expanded,
  onToggle,
  busy,
  onBook,
}: {
  t: StudentSupportTeacher
  expanded: boolean
  onToggle: () => void
  busy: string | null
  onBook: (id: string) => void
}) {
  const groups = groupByDate(t.openSlots)
  const slotCount = t.openSlots.length

  return (
    <div className="card" style={{ borderRadius: 18, padding: 0, overflow: 'hidden' }}>
      {/* O'qituvchi sarlavhasi — accordion trigger */}
      <button
        className="press"
        onClick={onToggle}
        style={{
          width: '100%',
          background: 'none',
          border: 'none',
          padding: '14px 16px',
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          cursor: 'pointer',
          textAlign: 'left',
        }}
      >
        <TeacherAvatar t={t} />

        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 14.5, fontWeight: 800, lineHeight: 1.3 }}>{t.fullName}</div>
          {t.subject ? (
            <div
              style={{
                display: 'inline-block',
                marginTop: 4,
                fontSize: 11.5,
                fontWeight: 700,
                padding: '2px 8px',
                borderRadius: 999,
                background: 'var(--accentSoft)',
                color: 'var(--accent)',
              }}
            >
              {t.subject}
            </div>
          ) : null}
        </div>

        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            flex: 'none',
          }}
        >
          {slotCount > 0 && (
            <span
              style={{
                fontSize: 11.5,
                fontWeight: 700,
                padding: '4px 9px',
                borderRadius: 999,
                background: 'rgba(37,99,235,.1)',
                color: 'var(--accent)',
                whiteSpace: 'nowrap',
              }}
            >
              {slotCount} bo'sh
            </span>
          )}
          <Icon
            name={expanded ? 'chevD' : 'chevR'}
            size={18}
            color="var(--muted, #94a3b8)"
          />
        </div>
      </button>

      {/* Kengaytirilgan qism */}
      {expanded && (
        <div
          style={{
            borderTop: '1px solid var(--border)',
            padding: '12px 16px 14px',
            display: 'flex',
            flexDirection: 'column',
            gap: 14,
          }}
        >
          {groups.length === 0 ? (
            <div
              className="muted"
              style={{ textAlign: 'center', padding: '16px 0', fontSize: 13 }}
            >
              Bu o'qituvchida hozircha bo'sh vaqt yo'q
            </div>
          ) : (
            groups.map(({ date, slots }) => (
              <div key={date}>
                {/* Kun sarlavhasi */}
                <div
                  style={{
                    fontSize: 12,
                    fontWeight: 700,
                    opacity: 0.65,
                    marginBottom: 8,
                    letterSpacing: 0.2,
                  }}
                >
                  {fmtDate(date, true)}
                </div>

                {/* O'sha kundagi slotlar */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: 7 }}>
                  {slots.map((s) => (
                    <div
                      key={s.id}
                      className="row gap10"
                      style={{
                        alignItems: 'center',
                        padding: '9px 12px',
                        borderRadius: 14,
                        background: 'var(--surface3, rgba(0,0,0,.03))',
                      }}
                    >
                      <Icon name="clock" size={15} color="var(--accent)" />
                      <div style={{ flex: 1, fontSize: 13.5, fontWeight: 600 }}>
                        {s.startTime}–{s.endTime}
                      </div>
                      <button
                        className="btn btn-primary press"
                        onClick={() => onBook(s.id)}
                        disabled={!!busy}
                        style={{
                          flex: 'none',
                          width: 'auto',
                          padding: '7px 15px',
                          fontSize: 12.5,
                          opacity: busy === s.id ? 0.6 : 1,
                        }}
                      >
                        {busy === s.id ? '...' : 'Bron qilish'}
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ))
          )}
        </div>
      )}
    </div>
  )
}

export function StudentSupportScreen() {
  const nav = useNavigate()
  const [data, setData] = useState<StudentSupport | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string | null>(null)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const load = async () => {
    try {
      const d = await getStudentSupport()
      setData(d)
    } catch {
      /* bo'sh holat ko'rsatamiz */
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
  }, [])

  const book = async (id: string) => {
    if (busy) return
    setBusy(id)
    try {
      await bookSupportSlot(id)
      await load()
    } catch {
      /* e'tibor bermaymiz */
    } finally {
      setBusy(null)
    }
  }

  const cancel = async (id: string) => {
    if (busy) return
    setBusy(id)
    try {
      await cancelSupportSlot(id)
      await load()
    } catch {
      /* e'tibor bermaymiz */
    } finally {
      setBusy(null)
    }
  }

  const myBookings = data?.myBookings ?? []
  const supports = data?.supports ?? []

  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10" style={{ minHeight: 38 }}>
          <button className="iconbtn press" onClick={() => nav('/student/profile')}>
            <Icon name="chevL" size={22} />
          </button>
          <div className="hd-sm" style={{ flex: 1 }}>
            Support
          </div>
        </div>
      </div>

      <div className="scroll" style={{ paddingBottom: 28 }}>
        <div className="pad">
          {loading ? (
            <div
              className="muted"
              style={{ textAlign: 'center', padding: '48px 0', fontSize: 13.5 }}
            >
              Yuklanmoqda...
            </div>
          ) : (
            <>
              {/* ===== Mening bronlarim ===== */}
              {myBookings.length > 0 && (
                <div style={{ marginBottom: 22 }}>
                  <div
                    style={{
                      fontSize: 12.5,
                      fontWeight: 800,
                      marginBottom: 10,
                      opacity: 0.65,
                      letterSpacing: 0.3,
                      textTransform: 'uppercase',
                    }}
                  >
                    Mening bronlarim
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                    {myBookings.map((b) => {
                      const done = b.status === 'done'
                      return (
                        <div key={b.id} className="card" style={{ borderRadius: 18 }}>
                          <div className="row gap10" style={{ alignItems: 'flex-start' }}>
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div style={{ fontSize: 14.5, fontWeight: 800 }}>{b.teacherName}</div>
                              <div
                                className="muted"
                                style={{ fontSize: 12.5, marginTop: 3 }}
                              >
                                {fmtDate(b.date, true)} · {b.startTime}–{b.endTime}
                              </div>
                            </div>
                            <span
                              style={{
                                flex: 'none',
                                fontSize: 11.5,
                                fontWeight: 700,
                                padding: '4px 10px',
                                borderRadius: 999,
                                background: done
                                  ? 'var(--greenSoft, #dcfce7)'
                                  : 'var(--accentSoft)',
                                color: done ? 'var(--green, #16a34a)' : 'var(--accent)',
                              }}
                            >
                              {done ? "O'tildi" : 'Bron qilindi'}
                            </span>
                          </div>

                          {done && (b.topic || b.notes) && (
                            <div
                              style={{
                                marginTop: 12,
                                paddingTop: 12,
                                borderTop: '1px solid var(--border)',
                                display: 'flex',
                                flexDirection: 'column',
                                gap: 8,
                              }}
                            >
                              {b.topic && (
                                <div>
                                  <div
                                    className="muted"
                                    style={{ fontSize: 11.5, fontWeight: 700 }}
                                  >
                                    Mavzu
                                  </div>
                                  <div style={{ fontSize: 13.5, marginTop: 2 }}>{b.topic}</div>
                                </div>
                              )}
                              {b.notes && (
                                <div>
                                  <div
                                    className="muted"
                                    style={{ fontSize: 11.5, fontWeight: 700 }}
                                  >
                                    Izoh
                                  </div>
                                  <div
                                    style={{ fontSize: 13.5, marginTop: 2, lineHeight: 1.5 }}
                                  >
                                    {b.notes}
                                  </div>
                                </div>
                              )}
                            </div>
                          )}

                          {b.status === 'booked' && (
                            <button
                              className="btn btn-soft press"
                              onClick={() => cancel(b.id)}
                              disabled={busy === b.id}
                              style={{ marginTop: 12 }}
                            >
                              <Icon name="x" size={17} color="var(--accent)" />
                              {busy === b.id ? 'Bekor qilinmoqda...' : 'Bekor qilish'}
                            </button>
                          )}
                        </div>
                      )
                    })}
                  </div>
                </div>
              )}

              {/* ===== Support o'qituvchilar ro'yxati ===== */}
              <div
                style={{
                  fontSize: 12.5,
                  fontWeight: 800,
                  marginBottom: 10,
                  opacity: 0.65,
                  letterSpacing: 0.3,
                  textTransform: 'uppercase',
                }}
              >
                Support o'qituvchilar
              </div>

              {supports.length === 0 ? (
                <div
                  className="muted"
                  style={{ textAlign: 'center', padding: '36px 0', fontSize: 13 }}
                >
                  Hozircha support o'qituvchi yo'q
                </div>
              ) : (
                <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                  {supports.map((t) => (
                    <TeacherCard
                      key={t.teacherId}
                      t={t}
                      expanded={expandedId === t.teacherId}
                      onToggle={() =>
                        setExpandedId((prev) =>
                          prev === t.teacherId ? null : t.teacherId
                        )
                      }
                      busy={busy}
                      onBook={book}
                    />
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  )
}
