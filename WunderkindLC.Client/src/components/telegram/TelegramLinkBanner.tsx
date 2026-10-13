import { useContext, useEffect, useState } from 'react'
import { BellRing } from 'lucide-react'
import { AuthContext } from '@/context/auth-context'
import { apiErrorMessage } from '@/lib/utils'
import { getTelegramLinkStatus, isKeyboardLaunch, sendBotLinkRequest } from '@/lib/telegramLink'

/**
 * «🔔 Bot xabarlarini yoqish» — only in a Mini App opened from the bot's reply keyboard (no initData there) while the
 * account receives no bot messages yet. The button sends a one-time token through `Telegram.WebApp.sendData`; the bot
 * links the chat and answers there (the Mini App closes). Rules: `.claude/rules/messaging.md` §0.5.
 */
export function TelegramLinkBanner({ variant }: { variant: 'student' | 'teacher' }) {
  // Soft read: a secondary widget must never break the shell it sits in (e.g. a layout rendered without the provider).
  const userId = useContext(AuthContext)?.user?.id
  const [show, setShow] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!userId || !isKeyboardLaunch()) return
    let alive = true
    getTelegramLinkStatus()
      .then((s) => {
        if (alive) setShow(s.configured && !s.linked)
      })
      // Background read: on failure the banner simply stays hidden (error-visibility.md §4).
      .catch(() => {})
    return () => {
      alive = false
    }
  }, [userId])

  if (!show) return null

  const link = async () => {
    setBusy(true)
    setError('')
    try {
      await sendBotLinkRequest()
    } catch (err) {
      setError(apiErrorMessage(err, "Ulab bo'lmadi — qayta urinib ko'ring"))
    } finally {
      setBusy(false)
    }
  }

  const button = (
    <button
      type="button"
      onClick={link}
      disabled={busy}
      className={
        variant === 'teacher'
          ? 'tap-scale mt-2 w-full rounded-lg bg-brand-600 px-3 py-2 text-[13px] font-semibold text-white disabled:opacity-60'
          : 'mt-2 w-full rounded-lg px-3 py-2 text-[13px] font-bold disabled:opacity-60'
      }
      style={variant === 'student' ? { background: 'var(--accent)', color: 'var(--onAccent)' } : undefined}
    >
      {busy ? 'Yuborilmoqda…' : '🔔 Bot xabarlarini yoqish'}
    </button>
  )

  const body = (
    <>
      <div className="flex items-start gap-2.5">
        <BellRing className="mt-0.5 h-4 w-4 shrink-0" style={{ color: variant === 'student' ? 'var(--accent)' : undefined }} />
        <p className="text-[13px] leading-snug">
          <b>Telegram xabarlari hali ulanmagan.</b> Davomat, to'lov va e'lonlarni shu botda olish uchun bir marta
          ulang — Panel yopiladi, bot «✅ ulandi» deb yozadi.
        </p>
      </div>
      {button}
      {error && <p className="mt-2 text-[12px] font-semibold text-rose-600">{error}</p>}
    </>
  )

  return variant === 'teacher' ? (
    <div className="mx-4 mt-3 rounded-xl border border-line bg-white p-3 text-ink shadow-[var(--shadow-card)]">{body}</div>
  ) : (
    <div
      className="mx-3 mt-2 rounded-xl p-3"
      style={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)', flexShrink: 0 }}
    >
      {body}
    </div>
  )
}
