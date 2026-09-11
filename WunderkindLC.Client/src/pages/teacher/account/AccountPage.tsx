import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ArrowLeft, Lock, Eye, EyeOff, Check } from 'lucide-react'
import { updateAccount } from '@/api/services/auth'

/**
 * O'qituvchi — Parolni almashtirish. Joriy parol + yangi parol (>=8) + takror.
 * Muvaffaqiyatda toast + ~600ms keyin orqaga qaytadi.
 */
export function TeacherAccountPage() {
  const nav = useNavigate()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const [showCurrent, setShowCurrent] = useState(false)
  const [showNext, setShowNext] = useState(false)
  const [showRepeat, setShowRepeat] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 2500)
    return () => clearTimeout(t)
  }, [toast])

  const submit = async () => {
    setError(null)
    if (!current || !next || !repeat) {
      setError("Barcha maydonlarni to'ldiring")
      return
    }
    if (next.length < 8) {
      setError("Yangi parol kamida 8 ta belgidan iborat bo'lishi kerak")
      return
    }
    if (next !== repeat) {
      setError('Yangi parollar mos kelmadi')
      return
    }
    setBusy(true)
    try {
      await updateAccount({ currentPassword: current, newPassword: next })
      setToast("Parol o'zgartirildi")
      setTimeout(() => nav(-1), 600)
    } catch (e) {
      setBusy(false)
      setError((e as Error)?.message || "Parolni o'zgartirib bo'lmadi")
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
        <p className="text-[17px] font-extrabold text-ink">Parolni almashtirish</p>
      </div>

      {/* Joriy parol */}
      <p className="px-0.5 pb-2 text-[13px] font-bold text-ink">Joriy parol</p>
      <div className="mb-4 flex items-center gap-2.5 rounded-[16px] border border-line bg-white p-3.5 shadow-[var(--shadow-card)] focus-within:border-teal-400">
        <Lock className="h-[18px] w-[18px] shrink-0 text-faint" />
        <input
          type={showCurrent ? 'text' : 'password'}
          value={current}
          onChange={(e) => setCurrent(e.target.value)}
          placeholder="••••••••"
          className="min-w-0 flex-1 bg-transparent text-[14px] text-ink outline-none"
        />
        <button
          type="button"
          onClick={() => setShowCurrent((v) => !v)}
          className="tap-scale shrink-0 text-faint"
        >
          {showCurrent ? <EyeOff className="h-[18px] w-[18px]" /> : <Eye className="h-[18px] w-[18px]" />}
        </button>
      </div>

      {/* Yangi parol */}
      <p className="px-0.5 pb-2 text-[13px] font-bold text-ink">Yangi parol</p>
      <div className="mb-4 flex items-center gap-2.5 rounded-[16px] border border-line bg-white p-3.5 shadow-[var(--shadow-card)] focus-within:border-teal-400">
        <Lock className="h-[18px] w-[18px] shrink-0 text-faint" />
        <input
          type={showNext ? 'text' : 'password'}
          value={next}
          onChange={(e) => setNext(e.target.value)}
          placeholder="Kamida 8 ta belgi"
          className="min-w-0 flex-1 bg-transparent text-[14px] text-ink outline-none"
        />
        <button
          type="button"
          onClick={() => setShowNext((v) => !v)}
          className="tap-scale shrink-0 text-faint"
        >
          {showNext ? <EyeOff className="h-[18px] w-[18px]" /> : <Eye className="h-[18px] w-[18px]" />}
        </button>
      </div>

      {/* Yangi parolni takrorlang */}
      <p className="px-0.5 pb-2 text-[13px] font-bold text-ink">Yangi parolni takrorlang</p>
      <div className="mb-4 flex items-center gap-2.5 rounded-[16px] border border-line bg-white p-3.5 shadow-[var(--shadow-card)] focus-within:border-teal-400">
        <Lock className="h-[18px] w-[18px] shrink-0 text-faint" />
        <input
          type={showRepeat ? 'text' : 'password'}
          value={repeat}
          onChange={(e) => setRepeat(e.target.value)}
          placeholder="••••••••"
          className="min-w-0 flex-1 bg-transparent text-[14px] text-ink outline-none"
        />
        <button
          type="button"
          onClick={() => setShowRepeat((v) => !v)}
          className="tap-scale shrink-0 text-faint"
        >
          {showRepeat ? <EyeOff className="h-[18px] w-[18px]" /> : <Eye className="h-[18px] w-[18px]" />}
        </button>
      </div>

      {/* Xato */}
      {error && (
        <div className="mb-4 rounded-[16px] border border-red-200 bg-red-50 p-3 text-[13px] font-semibold text-red-600">
          {error}
        </div>
      )}

      {/* Saqlash */}
      <button
        type="button"
        disabled={busy}
        onClick={submit}
        className="tap-scale inline-flex h-[52px] w-full items-center justify-center gap-2 rounded-2xl bg-teal-600 text-[15.5px] font-bold text-white disabled:opacity-45"
      >
        <Check className="h-[18px] w-[18px]" />
        {busy ? 'Saqlanmoqda...' : 'Saqlash'}
      </button>

      {toast && (
        <div className="fixed bottom-[78px] left-1/2 z-50 -translate-x-1/2 rounded-xl bg-ink px-4 py-2.5 text-[13px] font-semibold text-white shadow-lg">
          {toast}
        </div>
      )}
    </div>
  )
}
