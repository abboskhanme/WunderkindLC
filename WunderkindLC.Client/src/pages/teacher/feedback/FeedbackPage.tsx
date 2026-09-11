import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Sparkles, AlertTriangle, ImageIcon, X, Send } from 'lucide-react'
import { sendTeacherFeedback } from '@/api/services/teacher'
import { cn } from '@/lib/utils'

/**
 * O'qituvchi — Taklif va shikoyat. Taklif/Shikoyat segmenti + matn (>=5 belgi) +
 * ixtiyoriy rasm → admin "Taklif va shikoyatlar" bo'limida ko'radi.
 */
type FeedbackType = 'suggestion' | 'complaint'

export function TeacherFeedbackPage() {
  const nav = useNavigate()
  const [type, setType] = useState<FeedbackType>('suggestion')
  const [text, setText] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const fileRef = useRef<HTMLInputElement | null>(null)

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
      await sendTeacherFeedback(type, t, file)
      setToast('Yuborildi. Rahmat!')
      setTimeout(() => nav(-1), 600)
    } catch (e) {
      setBusy(false)
      setToast((e as Error)?.message || String(e))
    }
  }

  return (
    <div className="px-4 pt-3 pb-6">
      {/* Sarlavha */}
      <div className="mb-4 flex items-center gap-2.5">
        <button
          type="button"
          onClick={() => nav(-1)}
          className="tap-scale flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-line bg-white text-mute shadow-[var(--shadow-card)]"
        >
          <ArrowLeft className="h-5 w-5" />
        </button>
        <p className="text-[17px] font-extrabold text-ink">Taklif va shikoyat</p>
      </div>

      {/* Turi segmenti */}
      <div className="mb-4 grid grid-cols-2 gap-2 rounded-2xl border border-line bg-white p-1.5 shadow-[var(--shadow-card)]">
        <button
          type="button"
          onClick={() => setType('suggestion')}
          className={cn(
            'flex items-center justify-center gap-1.5 rounded-xl py-2.5 text-[13.5px] font-bold transition-colors',
            type === 'suggestion' ? 'bg-teal-600 text-white' : 'text-mute',
          )}
        >
          <Sparkles className="h-4 w-4" /> Taklif
        </button>
        <button
          type="button"
          onClick={() => setType('complaint')}
          className={cn(
            'flex items-center justify-center gap-1.5 rounded-xl py-2.5 text-[13.5px] font-bold transition-colors',
            type === 'complaint' ? 'bg-red-500 text-white' : 'text-mute',
          )}
        >
          <AlertTriangle className="h-4 w-4" /> Shikoyat
        </button>
      </div>

      {/* Matn */}
      <p className="px-0.5 pb-2 text-[13px] font-bold text-ink">Matn</p>
      <textarea
        rows={6}
        value={text}
        onChange={(e) => setText(e.target.value)}
        placeholder="Fikringizni yozing…"
        className="w-full resize-none rounded-[16px] border border-line bg-white p-3.5 text-[14px] text-ink shadow-[var(--shadow-card)] outline-none focus:border-teal-400"
      />

      {/* Rasm biriktirish */}
      <input
        ref={fileRef}
        type="file"
        accept="image/*"
        hidden
        onChange={(e) => {
          setFile(e.target.files?.[0] || null)
          e.target.value = ''
        }}
      />
      <div className="mt-3.5">
        {file ? (
          <div className="flex items-center gap-3 rounded-[16px] border border-line bg-white p-3 shadow-[var(--shadow-card)]">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-tealsoft text-teal-700">
              <ImageIcon className="h-5 w-5" />
            </div>
            <p className="min-w-0 flex-1 truncate text-[13.5px] font-semibold text-ink">{file.name}</p>
            <button type="button" onClick={() => setFile(null)} className="tap-scale p-1 text-faint">
              <X className="h-[18px] w-[18px]" />
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="tap-scale flex w-full items-center justify-center gap-2 rounded-[16px] border border-dashed border-line bg-white p-4 text-[14px] font-semibold text-teal-700 shadow-[var(--shadow-card)]"
          >
            <ImageIcon className="h-[22px] w-[22px]" />
            Rasm biriktirish (ixtiyoriy)
          </button>
        )}
      </div>

      {/* Yuborish */}
      <button
        type="button"
        disabled={!canSend}
        onClick={submit}
        className="tap-scale mt-5 inline-flex h-[52px] w-full items-center justify-center gap-2 rounded-2xl bg-teal-600 text-[15.5px] font-bold text-white disabled:opacity-45"
      >
        <Send className="h-[18px] w-[18px]" />
        {busy ? 'Yuborilmoqda…' : 'Yuborish'}
      </button>

      {toast && (
        <div className="fixed bottom-[78px] left-1/2 z-50 -translate-x-1/2 rounded-xl bg-ink px-4 py-2.5 text-[13px] font-semibold text-white shadow-lg">
          {toast}
        </div>
      )}
    </div>
  )
}
