import { useState } from 'react'
import { Loader2, Send } from 'lucide-react'
import type { SendTestResultsResult } from '@/types'
import { apiErrorMessage, cn } from '@/lib/utils'
import { formatSentAt, sendSummary } from '@/lib/testScoreGrid'

/**
 * «Ota-onalarga yuborish» (CRM U4a) — test natijalarini ota-onalarga Telegram orqali (outbox, `test_score` qoidasi).
 * Faqat O'ZGARGAN natijalar qayta ketadi; hammasini qayta yuborish — alohida, tasdiq bilan. Admin va o'qituvchi
 * bir xil panelni ishlatadi (`send` — qaysi endpoint).
 */
export function SendResultsPanel({
  resultsSentAt,
  beforeSend,
  send,
  onSent,
  className,
}: {
  resultsSentAt?: string | null
  /** Yuborishdan OLDIN — jadvaldagi saqlanmagan natijalarni saqlab olish. */
  beforeSend?: () => Promise<void>
  send: (resend: boolean) => Promise<SendTestResultsResult>
  /** Yuborilgach (yangi «Yuborilgan» vaqti uchun tafsilotni qayta yuklash). */
  onSent?: () => void
  className?: string
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [result, setResult] = useState<SendTestResultsResult | null>(null)

  const run = async (resend: boolean) => {
    if (resend && !window.confirm("Barcha natijalar ota-onalarga QAYTADAN yuboriladi (o'zgarmaganlari ham). Davom etasizmi?")) return
    setBusy(true)
    setError('')
    try {
      await beforeSend?.()
      const r = await send(resend)
      setResult(r)
      if (r.queued > 0) onSent?.()
    } catch (e) {
      setError(apiErrorMessage(e, "Natijalarni yuborib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  const sentLabel = formatSentAt(resultsSentAt)

  return (
    <div className={cn('rounded-xl border border-line bg-white p-3', className)}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="min-w-0 text-[12px] text-mute">
          {sentLabel ? (
            <>Yuborilgan: <b className="text-ink">{sentLabel}</b></>
          ) : (
            'Natijalar hali ota-onalarga yuborilmagan'
          )}
        </div>
        <button
          type="button"
          onClick={() => run(false)}
          disabled={busy}
          className="inline-flex items-center gap-1.5 rounded-lg bg-brand-600 px-3 py-2 text-[13px] font-semibold text-white transition-colors hover:bg-brand-700 disabled:opacity-50"
        >
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
          Ota-onalarga yuborish
        </button>
      </div>
      {result && (
        <p className="mt-2 text-[12px] text-emerald-700">
          {sendSummary(result)}
          {result.queued === 0 && result.skippedUnchanged > 0 && (
            <>
              {' '}
              <button type="button" onClick={() => run(true)} disabled={busy} className="font-semibold text-brand-600 underline">
                Hammasini qayta yuborish
              </button>
            </>
          )}
        </p>
      )}
      {error && <p className="mt-2 text-[12px] font-semibold text-rose-600">{error}</p>}
      <p className="mt-1.5 text-[11px] text-faint">
        Faqat yangi yoki o'zgargan natijalar yuboriladi. Xabar matni — «Xabarlar → Avto xabarlar» dagi «Test natijasi
        (ota-onaga)» qoidasi.
      </p>
    </div>
  )
}
