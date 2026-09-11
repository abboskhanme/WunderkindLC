import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { sendStudentFeedback } from '@/api/services/studentPortal'
import { Icon } from '@/pages/student/lib'
import posthog from '@/lib/posthog'

/* ============================================================
   O'quvchi portali — Taklif va shikoyat ekrani.
   Taklif/Shikoyat segment + matn (>=5 belgi) + ixtiyoriy rasm.
   Yuborilgach toast + ortga.
   ============================================================ */

type FeedbackType = 'suggestion' | 'complaint'

export function StudentFeedbackScreen() {
  const navigate = useNavigate()
  const [type, setType] = useState<FeedbackType>('suggestion')
  const [text, setText] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(t)
  }, [toast])

  const canSend = text.trim().length >= 5 && !busy

  const submit = async () => {
    const t = text.trim()
    if (t.length < 5) {
      setToast('Matn juda qisqa')
      return
    }
    setBusy(true)
    try {
      await sendStudentFeedback(type, t, file)
      posthog.capture('student_feedback_submitted', {
        feedback_type: type,
        has_attachment: Boolean(file),
      })
      setToast('Yuborildi. Rahmat!')
      setTimeout(() => navigate(-1), 600)
    } catch (e) {
      setBusy(false)
      setToast((e as Error)?.message || String(e))
    }
  }

  const head = (
    <div className="hd">
      <div className="row gap10" style={{ minHeight: 38 }}>
        <button className="iconbtn press" onClick={() => navigate(-1)}>
          <Icon name="chevL" size={22} />
        </button>
        <div className="hd-sm" style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          Taklif va shikoyat
        </div>
      </div>
    </div>
  )

  return (
    <div className="screen">
      {head}
      <div className="scroll" style={{ paddingBottom: 24 }}>
        <div className="pad">
          {/* Segment */}
          <div className="seg" style={{ marginBottom: 16 }}>
            <button className={type === 'suggestion' ? 'on press' : 'press'} onClick={() => setType('suggestion')}>
              <Icon name="sparkle" size={16} color={type === 'suggestion' ? '#fff' : 'var(--muted)'} />
              Taklif
            </button>
            <button className={type === 'complaint' ? 'on press' : 'press'} onClick={() => setType('complaint')}>
              <Icon name="alert" size={16} color={type === 'complaint' ? '#fff' : 'var(--muted)'} />
              Shikoyat
            </button>
          </div>

          <div style={{ fontSize: 14, fontWeight: 800, padding: '0 2px 8px' }}>Matn</div>
          <textarea
            className="ta"
            rows={6}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="Fikringizni yozing…"
          />

          <div style={{ height: 14 }} />

          {/* Rasm biriktirish */}
          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            style={{ display: 'none' }}
            onChange={(e) => {
              const f = e.target.files?.[0] || null
              setFile(f)
              e.target.value = ''
            }}
          />
          {file ? (
            <div className="card row gap12" style={{ borderRadius: 16, padding: 13 }}>
              <div
                style={{
                  width: 40,
                  height: 40,
                  borderRadius: 11,
                  background: 'var(--accentSoft)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  flex: 'none',
                }}
              >
                <Icon name="image" size={20} color="var(--accent)" />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontSize: 14, fontWeight: 700, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {file.name}
                </div>
              </div>
              <button className="press" onClick={() => setFile(null)}>
                <Icon name="x" size={18} color="var(--faint)" />
              </button>
            </div>
          ) : (
            <button
              className="press row gap10"
              onClick={() => fileInputRef.current?.click()}
              style={{
                width: '100%',
                padding: 16,
                background: 'var(--surface)',
                border: '1.5px dashed var(--borderStrong)',
                borderRadius: 16,
                justifyContent: 'center',
              }}
            >
              <Icon name="gallery" size={22} color="var(--accent)" />
              <span style={{ fontWeight: 700 }}>Rasm biriktirish (ixtiyoriy)</span>
            </button>
          )}

          <div style={{ height: 16 }} />
          <button className="btn btn-primary btn-lg press" disabled={!canSend} onClick={submit}>
            <Icon name="send" size={18} />
            <span>{busy ? 'Yuborilmoqda…' : 'Yuborish'}</span>
          </button>
        </div>
      </div>
      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}
