import { useState } from 'react'
import { Eye, EyeOff, Info, Lock } from 'lucide-react'
import { changeStudentPassword } from '@/api/services/studentPortal'
import { useAuth } from '@/context/auth-context'
import { apiErrorMessage } from '@/lib/utils'
import { useBack, useToast } from '@/pages/student/hooks'
import { PageHeader, Toast } from '@/pages/student/ui'

/* ============================================================
   Sozlamalar → PAROLNI O'ZGARTIRISH.
   ============================================================ */

interface FieldProps {
  id: string
  label: string
  placeholder: string
  value: string
  onChange: (v: string) => void
  autoComplete: string
}

function PasswordField({ id, label, placeholder, value, onChange, autoComplete }: FieldProps) {
  const [show, setShow] = useState(false)
  return (
    <div style={{ marginBottom: 14 }}>
      <label className="st-label" htmlFor={id}>
        {label}
      </label>
      <div className="field">
        <Lock size={19} color="var(--faint)" style={{ flex: 'none' }} />
        <input id={id} type={show ? 'text' : 'password'} placeholder={placeholder} value={value} autoComplete={autoComplete} onChange={(e) => onChange(e.target.value)} />
        <button type="button" className="press" onClick={() => setShow((s) => !s)} aria-label={show ? 'Parolni yashirish' : "Parolni ko'rsatish"} style={{ display: 'flex', padding: 6, margin: -6 }}>
          {show ? <EyeOff size={20} color="var(--faint)" /> : <Eye size={20} color="var(--faint)" />}
        </button>
      </div>
    </div>
  )
}

export function StudentAccountScreen() {
  const back = useBack('/student/settings')
  const { user } = useAuth()
  const [cur, setCur] = useState('')
  const [nw, setNw] = useState('')
  const [rep, setRep] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useToast()

  const save = async () => {
    if (busy) return
    if (!cur || !nw) {
      setErr("Maydonlarni to'ldiring")
      return
    }
    if (nw.length < 8) {
      setErr("Yangi parol kamida 8 belgidan iborat bo'lsin")
      return
    }
    if (nw !== rep) {
      setErr('Parollar mos kelmadi')
      return
    }
    setErr('')
    setBusy(true)
    try {
      await changeStudentPassword(cur, nw)
      setToast('Parol almashtirildi')
      setTimeout(back, 1200)
    } catch (e) {
      setErr(apiErrorMessage(e, "Parolni o'zgartirib bo'lmadi"))
      setBusy(false)
    }
  }

  return (
    <div className="screen">
      <PageHeader title="Parolni o'zgartirish" back="/student/settings" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          <PasswordField id="pw-cur" label="Joriy parol" placeholder="Hozirgi parolingiz" value={cur} onChange={setCur} autoComplete="current-password" />
          <PasswordField id="pw-new" label="Yangi parol" placeholder="Kamida 8 belgi" value={nw} onChange={setNw} autoComplete="new-password" />
          <PasswordField id="pw-rep" label="Yangi parolni takrorlang" placeholder="Yangi parol yana bir marta" value={rep} onChange={setRep} autoComplete="new-password" />

          {/* CRM Phase 6: the student password is kept in the vault and visible to admins — said upfront.
              Parent accounts are not stored (SPEC §10 #10), so the note is for the student only. */}
          {user?.role === 'student' && (
            <div className="st-banner info" style={{ marginBottom: 14, fontWeight: 500 }}>
              <Info size={18} style={{ flex: 'none', marginTop: 1 }} />
              <span>Eslatma: markaz administratorlari yangi parolni ko'ra oladi.</span>
            </div>
          )}

          {err && (
            <div role="alert" className="st-banner" style={{ marginBottom: 14 }}>
              {err}
            </div>
          )}

          <button className="btn btn-primary btn-lg press" disabled={busy} onClick={save}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </div>
      <Toast text={toast} />
    </div>
  )
}
