import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { changeStudentPassword } from '@/api/services/studentPortal'
import { Icon } from '@/pages/student/lib'

/* ============================================================
   O'quvchi portali — PAROLNI O'ZGARTIRISH.
   Dizayn: student.html ACCOUNT. .student-app shell.
   ============================================================ */

interface FieldProps {
  label: string
  placeholder: string
  value: string
  onChange: (v: string) => void
}

function PasswordField({ label, placeholder, value, onChange }: FieldProps) {
  const [show, setShow] = useState(false)
  return (
    <>
      <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--muted)', padding: '0 2px 7px' }}>
        {label}
      </div>
      <div className="field" style={{ marginBottom: 12 }}>
        <Icon name="lock" size={20} color="var(--faint)" />
        <input
          type={show ? 'text' : 'password'}
          placeholder={placeholder}
          value={value}
          onChange={(e) => onChange(e.target.value)}
        />
        <button className="press" type="button" onClick={() => setShow((s) => !s)}>
          <Icon name={show ? 'eyeOff' : 'eye'} size={20} color="var(--faint)" />
        </button>
      </div>
    </>
  )
}

export function StudentAccountScreen() {
  const nav = useNavigate()
  const [cur, setCur] = useState('')
  const [nw, setNw] = useState('')
  const [rep, setRep] = useState('')
  const [err, setErr] = useState('')
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState('')

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
      setTimeout(() => nav(-1), 1200)
    } catch (e) {
      setErr((e as Error)?.message || "O'zgartirib bo'lmadi")
      setBusy(false)
    }
  }

  return (
    <div className="screen">
      <div className="hd">
        <div className="row gap10" style={{ minHeight: 38 }}>
          <button className="iconbtn press" onClick={() => nav(-1)}>
            <Icon name="chevL" size={22} />
          </button>
          <div className="hd-sm" style={{ flex: 1 }}>
            Parolni o'zgartirish
          </div>
        </div>
      </div>

      <div className="scroll pad" style={{ paddingBottom: 24 }}>
        <PasswordField label="Joriy parol" placeholder="Joriy parol" value={cur} onChange={setCur} />
        <PasswordField label="Yangi parol" placeholder="Kamida 8 belgi" value={nw} onChange={setNw} />
        <PasswordField
          label="Yangi parolni takrorlang"
          placeholder="Takror"
          value={rep}
          onChange={setRep}
        />

        {err && (
          <div
            style={{
              color: 'var(--red)',
              fontSize: 13,
              fontWeight: 600,
              margin: '4px 0 12px',
            }}
          >
            {err}
          </div>
        )}

        <div style={{ height: 4 }} />
        <button className="btn btn-primary btn-lg press" disabled={busy} onClick={save}>
          <span>{busy ? 'Saqlanmoqda…' : 'Saqlash'}</span>
        </button>
      </div>

      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}
