import { useRef, useState } from 'react'
import { ImagePlus, Lightbulb, Send, TriangleAlert, X, Image as ImageIcon } from 'lucide-react'
import { sendStudentFeedback } from '@/api/services/studentPortal'
import { apiErrorMessage } from '@/lib/utils'
import posthog from '@/lib/posthog'
import { useBack, useToast } from '@/pages/student/hooks'
import { IconBox, PageHeader, Toast } from '@/pages/student/ui'

/* ============================================================
   Profil → TAKLIF VA SHIKOYAT — suggestion/complaint switch + text (≥ 5 chars) + optional photo.
   On error the form stays filled and the reason is shown (error-visibility.md); on success —
   a toast, then back.
   ============================================================ */

type FeedbackType = 'suggestion' | 'complaint'
const MIN = 5

export function StudentFeedbackScreen() {
  const back = useBack('/student/profile')
  const [type, setType] = useState<FeedbackType>('suggestion')
  const [text, setText] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useToast()
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  const len = text.trim().length
  const canSend = len >= MIN && !busy

  const submit = async () => {
    const t = text.trim()
    if (t.length < MIN) {
      setError('Matn juda qisqa — kamida 5 ta belgi yozing.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      await sendStudentFeedback(type, t, file)
      posthog.capture('student_feedback_submitted', { feedback_type: type, has_attachment: Boolean(file) })
      setToast('Yuborildi. Rahmat!')
      setTimeout(back, 700)
    } catch (e) {
      setBusy(false)
      setError(apiErrorMessage(e, "Yuborib bo'lmadi. Qayta urinib ko'ring."))
    }
  }

  return (
    <div className="screen">
      <PageHeader title="Taklif va shikoyat" back="/student/profile" />
      <div className="scroll">
        <div className="st-page" style={{ paddingTop: 8 }}>
          <div className="seg" style={{ marginBottom: 16 }} role="radiogroup" aria-label="Turi">
            <button type="button" role="radio" aria-checked={type === 'suggestion'} className={type === 'suggestion' ? 'on' : 'press'} onClick={() => setType('suggestion')}>
              <Lightbulb size={17} />
              Taklif
            </button>
            <button type="button" role="radio" aria-checked={type === 'complaint'} className={type === 'complaint' ? 'on' : 'press'} onClick={() => setType('complaint')}>
              <TriangleAlert size={17} />
              Shikoyat
            </button>
          </div>

          <label className="st-label" htmlFor="fb-text">
            {type === 'suggestion' ? 'Taklifingiz' : 'Shikoyatingiz'}
          </label>
          <textarea
            id="fb-text"
            className="ta"
            rows={6}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={type === 'suggestion' ? 'Nimani yaxshilashimiz mumkin?' : "Nima bo'ldi? Batafsil yozing…"}
          />
          <div className="faint" style={{ fontSize: 12.5, textAlign: 'right', marginTop: 4 }}>
            {len < MIN ? `Kamida ${MIN} ta belgi` : `${len} ta belgi`}
          </div>

          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            style={{ display: 'none' }}
            onChange={(e) => {
              setFile(e.target.files?.[0] || null)
              e.target.value = ''
            }}
          />
          <div style={{ marginTop: 10 }}>
            {file ? (
              <div className="card row gap12" style={{ padding: 12 }}>
                <IconBox icon={ImageIcon} tone="info" />
                <div className="st-wrap" style={{ flex: 1, minWidth: 0, fontSize: 14.5, fontWeight: 600 }}>{file.name}</div>
                <button type="button" className="iconbtn press" onClick={() => setFile(null)} aria-label="Rasmni olib tashlash">
                  <X size={18} />
                </button>
              </div>
            ) : (
              <button
                type="button"
                className="press row gap10"
                onClick={() => fileInputRef.current?.click()}
                style={{ width: '100%', minHeight: 56, padding: 14, background: 'var(--surface)', border: '1.5px dashed var(--borderStrong)', borderRadius: 14, justifyContent: 'center', color: 'var(--accent)', fontSize: 15, fontWeight: 700 }}
              >
                <ImagePlus size={20} />
                Rasm biriktirish (ixtiyoriy)
              </button>
            )}
          </div>

          {error && (
            <div role="alert" className="st-banner" style={{ marginTop: 14 }}>
              {error}
            </div>
          )}

          <button className="btn btn-primary btn-lg press" disabled={!canSend} onClick={submit} style={{ marginTop: 16 }}>
            <Send size={18} />
            {busy ? 'Yuborilmoqda…' : 'Yuborish'}
          </button>
        </div>
      </div>
      <Toast text={toast} />
    </div>
  )
}
