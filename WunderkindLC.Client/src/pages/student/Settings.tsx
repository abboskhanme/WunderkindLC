import { useEffect, useState, type ReactNode } from 'react'
import { Check, Lock, Moon, SunMoon } from 'lucide-react'
import { getStudentSettings, saveStudentSettings } from '@/api/services/studentPortal'
import { getThemePref, setThemePref, telegramApp, type ThemePref } from '@/pages/student/theme'
import { apiErrorMessage } from '@/lib/utils'
import { IconBox, ListRow, PageHeader } from '@/pages/student/ui'

/* ============================================================
   Profil → SOZLAMALAR: theme (Avto / Yorug' / Tungi), language, password.
   Theme is a per-device choice (localStorage); "Avto" follows Telegram inside the Mini App and
   the phone's setting in a browser. Language is saved on the server.
   ============================================================ */

const LANGS: [string, string][] = [
  ['uz', "O'zbekcha"],
  ['ru', 'Русский'],
  ['en', 'English'],
]

const THEMES: [ThemePref, string][] = [
  ['auto', 'Avto'],
  ['light', "Yorug'"],
  ['dark', 'Tungi'],
]

const Group = ({ title, children, foot }: { title: string; children: ReactNode; foot?: ReactNode }) => (
  <div style={{ marginBottom: 20 }}>
    <div className="st-label" style={{ textTransform: 'uppercase', letterSpacing: '0.4px', fontSize: 12.5 }}>
      {title}
    </div>
    <div className="card flush">{children}</div>
    {foot && (
      <div className="muted" style={{ fontSize: 13, padding: '8px 4px 0', lineHeight: 1.45 }}>
        {foot}
      </div>
    )}
  </div>
)

export function StudentSettingsScreen() {
  const [theme, setTheme] = useState<ThemePref>(getThemePref)
  const [lang, setLang] = useState<string>('uz')
  /** Save error — NEVER silent: on failure the choice is rolled back too. */
  const [saveError, setSaveError] = useState('')
  const [saving, setSaving] = useState(false)
  const inTelegram = !!telegramApp()

  useEffect(() => {
    let alive = true
    getStudentSettings()
      .then((s) => {
        if (alive && s?.language) setLang(s.language)
      })
      // Background READ: on failure the page works with defaults (save errors are shown separately).
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [])

  const pickTheme = (t: ThemePref) => {
    setTheme(t)
    setThemePref(t)
  }

  // OPTIMISTIC update — rolled back on error with the reason shown (`error-visibility.md` §3).
  const pickLang = async (l: string) => {
    if (saving || l === lang) return
    const prev = lang
    setLang(l)
    setSaveError('')
    setSaving(true)
    try {
      await saveStudentSettings({ language: l })
    } catch (e) {
      setLang(prev)
      setSaveError(apiErrorMessage(e, "Tilni saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="screen">
      <PageHeader title="Sozlamalar" back="/student/profile" />

      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          {saveError && (
            <div role="alert" className="st-banner" style={{ marginBottom: 14 }}>
              {saveError}
            </div>
          )}

          <Group
            title="Ko'rinish"
            foot={
              theme === 'auto'
                ? inTelegram
                  ? 'Avto — Telegram mavzusiga moslashadi.'
                  : 'Avto — telefon sozlamasiga moslashadi.'
                : undefined
            }
          >
            <div className="row gap12" style={{ padding: '14px 16px' }}>
              <IconBox icon={theme === 'dark' ? Moon : SunMoon} tone="violet" />
              <div style={{ flex: 1, minWidth: 0, fontSize: 15, fontWeight: 700 }}>Mavzu</div>
            </div>
            <div style={{ padding: '0 16px 16px' }}>
              <div className="seg" role="radiogroup" aria-label="Mavzu">
                {THEMES.map(([k, label]) => (
                  <button key={k} type="button" role="radio" aria-checked={theme === k} className={theme === k ? 'on' : 'press'} onClick={() => pickTheme(k)}>
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </Group>

          <Group title="Til">
            {LANGS.map(([k, label]) => (
              <button key={k} type="button" className="st-li press" onClick={() => pickLang(k)} style={{ minHeight: 54 }}>
                <span className="st-li-main" style={{ fontSize: 15, fontWeight: lang === k ? 700 : 500 }}>
                  {label}
                </span>
                {lang === k && <Check size={20} color="var(--accent)" strokeWidth={2.6} />}
              </button>
            ))}
          </Group>

          <Group title="Akkaunt">
            <ListRow to="/student/account" icon={Lock} iconTone="info" title="Parolni o'zgartirish" />
          </Group>

          <div className="faint" style={{ textAlign: 'center', fontSize: 12.5 }}>Wunderkind · v1.0.0</div>
        </div>
      </div>
    </div>
  )
}
