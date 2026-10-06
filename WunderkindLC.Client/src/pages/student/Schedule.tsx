import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentPortalGroups, type StudentGroupItem } from '@/api/services/studentPortal'
import { Icon } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — DARS JADVALI (2026-10-07 uchrashuvi: Mini App'da jadval).
   Faqat hozir o'qiyotgan guruhlar (faol + sinov) — muzlatilgan va tugaganlar jadvalga kirmaydi.
   Hafta kunlari 0=Du … 6=Ya (server `Group.Days`), bugungi kun tepada belgilanadi.
   ============================================================ */

const DAY_NAMES = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']

/** Bugun hafta kuni (0=Du … 6=Ya). */
const todayIndex = () => (new Date().getDay() + 6) % 7

export function StudentScheduleScreen() {
  const navigate = useNavigate()
  const [groups, setGroups] = useState<StudentGroupItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    getStudentPortalGroups()
      .then((d) => alive && setGroups(d))
      .catch((e) => alive && setError(e?.response?.data?.message || e?.message || String(e)))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [])

  const today = todayIndex()
  // Kun → shu kuni dars bo'ladigan guruhlar (vaqt bo'yicha). Bugundan boshlab tartiblanadi.
  const byDay = useMemo(() => {
    const live = groups.filter((g) => g.state === 'active' || g.state === 'trial')
    return [0, 1, 2, 3, 4, 5, 6]
      .map((i) => (today + i) % 7)
      .map((day) => ({
        day,
        lessons: live
          .filter((g) => g.days.includes(day))
          .sort((a, b) => (a.startTime || '').localeCompare(b.startTime || '')),
      }))
      .filter((d) => d.lessons.length > 0)
  }, [groups, today])

  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10" style={{ minHeight: 38 }}>
          <button className="iconbtn press" onClick={() => navigate(-1)}>
            <Icon name="chevL" size={22} />
          </button>
          <div className="hd-sm" style={{ flex: 1 }}>Dars jadvali</div>
        </div>
      </div>
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
          ) : byDay.length === 0 ? (
            <div className="center" style={{ minHeight: '55dvh', flexDirection: 'column', gap: 10, textAlign: 'center' }}>
              <Icon name="calendar" size={40} color="var(--faint)" />
              <div style={{ fontSize: 17, fontWeight: 800 }}>Dars jadvali yo'q</div>
              <div className="muted" style={{ fontSize: 13 }}>Faol guruh yo'q yoki guruh vaqti kiritilmagan.</div>
            </div>
          ) : (
            byDay.map(({ day, lessons }) => (
              <div key={day} style={{ marginBottom: 16 }}>
                <div className="row gap8" style={{ marginBottom: 8, alignItems: 'center' }}>
                  <div style={{ fontSize: 14, fontWeight: 800 }}>{DAY_NAMES[day]}</div>
                  {day === today && (
                    <span style={{ fontSize: 11, fontWeight: 800, color: '#fff', background: '#2563EB', borderRadius: 8, padding: '2px 8px' }}>
                      BUGUN
                    </span>
                  )}
                </div>
                {lessons.map((g) => (
                  <div key={g.groupId} className="card" style={{ borderRadius: 16, padding: '12px 14px', marginBottom: 8, borderLeft: '4px solid #2563EB' }}>
                    <div className="row gap12" style={{ alignItems: 'flex-start' }}>
                      <div style={{ flex: 'none', minWidth: 64 }}>
                        <div style={{ fontSize: 16, fontWeight: 800 }}>{g.startTime || '—'}</div>
                        <div className="faint" style={{ fontSize: 12 }}>{g.endTime}</div>
                      </div>
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: 14.5, fontWeight: 800, lineHeight: 1.25 }}>{g.courseName || g.name}</div>
                        <div className="muted" style={{ fontSize: 12.5, marginTop: 2 }}>
                          {g.teacherName}
                          {g.room ? ` · ${g.room}` : ''}
                        </div>
                        {g.state === 'trial' && (
                          <div style={{ fontSize: 11.5, fontWeight: 700, color: '#D97706', marginTop: 4 }}>Sinov darsi</div>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
