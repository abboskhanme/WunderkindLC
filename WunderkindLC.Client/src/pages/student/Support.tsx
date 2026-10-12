import { useState } from 'react'
import { ChevronDown, ChevronRight, Clock, LifeBuoy, X } from 'lucide-react'
import { fmtDate } from '@/pages/student/lib'
import { getStudentSupport, bookSupportSlot, cancelSupportSlot, type StudentSupportTeacher } from '@/api/services/support'
import { apiErrorMessage } from '@/lib/utils'
import { useApi } from '@/pages/student/hooks'
import { Avatar, EmptyState, ErrorState, PageHeader, SectionTitle, SkeletonList, StatusChip } from '@/pages/student/ui'

/* ============================================================
   Profil → SUPPORT DARSLAR — support teachers publish free slots, the student books one.
   Slots are an accordion per teacher, grouped by day. Booking / cancelling errors are shown
   (they used to be swallowed — error-visibility.md).
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
    .map(([date, daySlots]) => ({ date, slots: [...daySlots].sort((a, b) => a.startTime.localeCompare(b.startTime)) }))
}

function TeacherAvatar({ t }: { t: StudentSupportTeacher }) {
  if (t.photoUrl) return <img src={t.photoUrl} alt="" style={{ width: 44, height: 44, borderRadius: 14, objectFit: 'cover', flex: 'none' }} />
  return <Avatar name={t.fullName} size={44} gradient="linear-gradient(135deg,#0284c7,#2563eb)" />
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
    <div className="card flush">
      <button type="button" className="st-li press" onClick={onToggle} aria-expanded={expanded}>
        <TeacherAvatar t={t} />
        <div className="st-li-main">
          <div className="st-li-title">{t.fullName}</div>
          {t.subject && <div className="st-li-sub">{t.subject}</div>}
        </div>
        {slotCount > 0 && <StatusChip tone="info">{slotCount} bo'sh vaqt</StatusChip>}
        {expanded ? <ChevronDown size={18} color="var(--faint)" /> : <ChevronRight size={18} color="var(--faint)" />}
      </button>

      {expanded && (
        <div style={{ borderTop: '1px solid var(--border)', padding: '12px 16px 16px', display: 'flex', flexDirection: 'column', gap: 14 }}>
          {groups.length === 0 ? (
            <div className="muted" style={{ textAlign: 'center', padding: '12px 0', fontSize: 14 }}>
              Bu o'qituvchida hozircha bo'sh vaqt yo'q
            </div>
          ) : (
            groups.map(({ date, slots }) => (
              <div key={date}>
                <div className="muted" style={{ fontSize: 13, fontWeight: 700, marginBottom: 8 }}>{fmtDate(date, true)}</div>
                <div className="col" style={{ gap: 8 }}>
                  {slots.map((s) => (
                    <div key={s.id} className="row gap10" style={{ padding: '8px 8px 8px 12px', borderRadius: 14, background: 'var(--surface2)', border: '1px solid var(--border)' }}>
                      <Clock size={16} color="var(--accent)" style={{ flex: 'none' }} />
                      <div className="st-num" style={{ flex: 1, fontSize: 15, fontWeight: 700 }}>
                        {s.startTime}–{s.endTime}
                      </div>
                      <button className="btn btn-primary btn-sm press" onClick={() => onBook(s.id)} disabled={!!busy}>
                        {busy === s.id ? 'Band qilinmoqda…' : 'Band qilish'}
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
  const { data, error, loading, reload } = useApi(getStudentSupport)
  const [busy, setBusy] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const run = async (id: string, fn: (id: string) => Promise<unknown>, fallback: string) => {
    if (busy) return
    setBusy(id)
    setActionError(null)
    try {
      await fn(id)
      reload()
    } catch (e) {
      setActionError(apiErrorMessage(e, fallback))
    } finally {
      setBusy(null)
    }
  }
  const book = (id: string) => run(id, bookSupportSlot, "Band qilib bo'lmadi")
  const cancel = (id: string) => run(id, cancelSupportSlot, "Bekor qilib bo'lmadi")

  const myBookings = data?.myBookings ?? []
  const supports = data?.supports ?? []

  return (
    <div className="screen">
      <PageHeader title="Support darslar" back="/student/profile" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {actionError && (
            <div role="alert" className="st-banner" style={{ marginBottom: 12 }}>
              {actionError}
            </div>
          )}
          {error && !data ? (
            <ErrorState message={error} onRetry={reload} />
          ) : loading && !data ? (
            <SkeletonList rows={2} />
          ) : (
            <>
              {myBookings.length > 0 && (
                <>
                  <SectionTitle title="Band qilingan vaqtlarim" style={{ marginTop: 4 }} />
                  <div className="col" style={{ gap: 10 }}>
                    {myBookings.map((b) => {
                      const done = b.status === 'done'
                      return (
                        <div key={b.id} className="card" style={{ padding: 14 }}>
                          <div className="row gap10" style={{ alignItems: 'flex-start' }}>
                            <div style={{ flex: 1, minWidth: 0 }}>
                              <div className="st-wrap" style={{ fontSize: 15.5, fontWeight: 700 }}>{b.teacherName}</div>
                              <div className="muted" style={{ fontSize: 13.5, marginTop: 2 }}>
                                {fmtDate(b.date, true)} · {b.startTime}–{b.endTime}
                              </div>
                            </div>
                            <StatusChip tone={done ? 'ok' : 'info'}>{done ? "O'tildi" : 'Band qilindi'}</StatusChip>
                          </div>
                          {done && (b.topic || b.notes) && (
                            <div style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: 8 }}>
                              {b.topic && (
                                <div>
                                  <div className="muted" style={{ fontSize: 12.5, fontWeight: 700 }}>Mavzu</div>
                                  <div className="st-wrap" style={{ fontSize: 14, marginTop: 2 }}>{b.topic}</div>
                                </div>
                              )}
                              {b.notes && (
                                <div>
                                  <div className="muted" style={{ fontSize: 12.5, fontWeight: 700 }}>Izoh</div>
                                  <div className="st-wrap" style={{ fontSize: 14, marginTop: 2, lineHeight: 1.5 }}>{b.notes}</div>
                                </div>
                              )}
                            </div>
                          )}
                          {b.status === 'booked' && (
                            <button className="btn btn-ghost btn-sm press" onClick={() => cancel(b.id)} disabled={busy === b.id} style={{ marginTop: 12, width: '100%' }}>
                              <X size={16} />
                              {busy === b.id ? 'Bekor qilinmoqda…' : 'Bekor qilish'}
                            </button>
                          )}
                        </div>
                      )
                    })}
                  </div>
                </>
              )}

              <SectionTitle title="Support o'qituvchilar" sub="Qo'shimcha dars uchun bo'sh vaqtni band qiling" style={myBookings.length ? undefined : { marginTop: 4 }} />
              {supports.length === 0 ? (
                <div className="card flush">
                  <EmptyState icon={LifeBuoy} title="Hozircha support o'qituvchi yo'q" sub="Markaz support darslarini e'lon qilganda shu yerda ko'rinadi." />
                </div>
              ) : (
                <div className="col" style={{ gap: 10 }}>
                  {supports.map((t) => (
                    <TeacherCard
                      key={t.teacherId}
                      t={t}
                      expanded={expandedId === t.teacherId}
                      onToggle={() => setExpandedId((prev) => (prev === t.teacherId ? null : t.teacherId))}
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
