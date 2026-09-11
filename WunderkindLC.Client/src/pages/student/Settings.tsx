import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getStudentSettings, saveStudentSettings } from '@/api/services/studentPortal'
import { getStudentTheme, setStudentTheme } from '@/components/layout/StudentMobileLayout'
import { Icon } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — SOZLAMALAR.
   Dizayn: student.html SETTINGS. .student-app shell.
   ============================================================ */

const LANGS: [string, string][] = [
  ['uz', "O'zbek"],
  ['ru', 'Русский'],
  ['en', 'English'],
]

function getPush(): boolean {
  return localStorage.getItem('student_push') !== 'off'
}

export function StudentSettingsScreen() {
  const nav = useNavigate()
  const [dark, setDark] = useState<boolean>(() => getStudentTheme() === 'dark')
  const [lang, setLang] = useState<string>('uz')
  const [push, setPush] = useState<boolean>(getPush)

  useEffect(() => {
    let alive = true
    getStudentSettings()
      .then((s) => {
        if (!alive) return
        if (s?.language) setLang(s.language)
        if (typeof s?.notificationsEnabled === 'boolean') {
          setPush(s.notificationsEnabled)
          localStorage.setItem('student_push', s.notificationsEnabled ? 'on' : 'off')
        }
      })
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])

  const toggleDark = () => {
    const next = !dark
    setDark(next)
    setStudentTheme(next ? 'dark' : 'light')
  }

  const pickLang = (l: string) => {
    setLang(l)
    saveStudentSettings({ language: l }).catch(() => {})
  }

  const togglePush = () => {
    const next = !push
    setPush(next)
    localStorage.setItem('student_push', next ? 'on' : 'off')
    saveStudentSettings({ notificationsEnabled: next }).catch(() => {})
  }

  const IconBox = ({ icon, color }: { icon: string; color: string }) => (
    <div
      style={{
        width: 32,
        height: 32,
        borderRadius: 9,
        background: color + '22',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flex: 'none',
      }}
    >
      <Icon name={icon} size={17} color={color} />
    </div>
  )

  const Group = ({ title, children }: { title: string; children: React.ReactNode }) => (
    <div style={{ marginBottom: 18 }}>
      <div
        className="muted"
        style={{ fontSize: 13, fontWeight: 800, letterSpacing: '.3px', padding: '0 4px 8px' }}
      >
        {title.toUpperCase()}
      </div>
      <div className="card" style={{ padding: 4, borderRadius: 18 }}>
        {children}
      </div>
    </div>
  )

  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10" style={{ minHeight: 38 }}>
          <button className="iconbtn press" onClick={() => nav(-1)}>
            <Icon name="chevL" size={22} />
          </button>
          <div className="hd-sm" style={{ flex: 1 }}>
            Sozlamalar
          </div>
        </div>
      </div>

      <div className="scroll pad" style={{ paddingBottom: 24 }}>
        {/* Ko'rinish */}
        <Group title="Ko'rinish">
          <div className="row gap12" style={{ padding: '12px 11px' }}>
            <IconBox icon="moon" color="#7C3AED" />
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 14.5, fontWeight: 700 }}>Tungi rejim</div>
              <div className="muted" style={{ fontSize: 12 }}>
                {dark ? 'Yoqilgan' : "O'chirilgan"}
              </div>
            </div>
            <div className={'switch press' + (dark ? ' on' : '')} onClick={toggleDark}>
              <i />
            </div>
          </div>
        </Group>

        {/* Til */}
        <Group title="Til">
          {LANGS.map((l, i) => (
            <button
              key={l[0]}
              className="press row"
              onClick={() => pickLang(l[0])}
              style={{
                width: '100%',
                padding: '13px 11px',
                borderBottom: i < LANGS.length - 1 ? '1px solid var(--border)' : undefined,
              }}
            >
              <span style={{ flex: 1, textAlign: 'left', fontSize: 15, fontWeight: 700 }}>
                {l[1]}
              </span>
              {lang === l[0] && <Icon name="check" size={20} color="var(--accent)" />}
            </button>
          ))}
        </Group>

        {/* Bildirishnomalar */}
        <Group title="Bildirishnomalar">
          <div className="row gap12" style={{ padding: '12px 11px' }}>
            <IconBox icon="bell" color="#EA580C" />
            <div style={{ flex: 1 }}>
              <div style={{ fontSize: 14.5, fontWeight: 700 }}>Push bildirishnoma</div>
              <div className="muted" style={{ fontSize: 12 }}>
                Yangi baho va xabar
              </div>
            </div>
            <div className={'switch press' + (push ? ' on' : '')} onClick={togglePush}>
              <i />
            </div>
          </div>
        </Group>

        {/* Akkaunt */}
        <Group title="Akkaunt">
          <button
            className="press row gap12"
            onClick={() => nav('/student/account')}
            style={{ width: '100%', padding: '12px 11px', textAlign: 'left' }}
          >
            <IconBox icon="lock" color="#2563EB" />
            <span style={{ flex: 1, fontSize: 14.5, fontWeight: 700 }}>Parolni o'zgartirish</span>
            <Icon name="chevR" size={18} color="var(--faint)" />
          </button>
        </Group>

        <div className="faint" style={{ textAlign: 'center', fontSize: 12 }}>
          Wunderkind · v1.0.0
        </div>
      </div>
    </div>
  )
}
