import { FileText, Film, Loader2 } from 'lucide-react'
import { renderTelegramHtml } from '@/lib/telegramHtml'
import { formatBytes } from '@/lib/telegramDelivery'
import { cn } from '@/lib/utils'

export interface PreviewAttachment {
  kind: 'photo' | 'video' | 'document'
  fileName: string
  sizeBytes: number
  /** Rasm eskizi (lokal yoki blob URL) — bo'lmasa ikonka. */
  thumbUrl?: string | null
}

/**
 * Telegram'dagi ko'rinish (CRM upgrade P2.8): server preview'i qaytargan HTML oq ro'yxat orqali
 * chiziladi (`lib/telegramHtml.ts` — `dangerouslySetInnerHTML` YO'Q). Biriktirma bilan matn izoh
 * (caption) bo'ladi; matn izohga sig'masa (`separate`) — fayl va matn ikki alohida xabar.
 * «✅ Qabul qildim» tugmasi OXIRGI xabar ostida.
 */
export function TelegramPreview({
  html,
  mode,
  attachment,
  ackButton,
  loading,
  emptyHint = "Matn yozing — ko'rinish shu yerda chiqadi",
}: {
  html: string
  mode: 'text' | 'caption' | 'separate'
  attachment?: PreviewAttachment | null
  ackButton?: boolean
  loading?: boolean
  emptyHint?: string
}) {
  const hasText = html.trim().length > 0
  const separate = !!attachment && mode === 'separate'
  const button = ackButton ? (
    <div className="mt-1 rounded-lg bg-white/70 py-1.5 text-center text-[13px] font-medium text-sky-700 shadow-sm">
      ✅ Qabul qildim
    </div>
  ) : null

  return (
    <div className="relative rounded-2xl bg-[#d9e6f2] p-3" style={{ minHeight: 140 }}>
      {loading && <Loader2 className="absolute right-3 top-3 h-4 w-4 animate-spin text-slate-400" />}
      {!hasText && !attachment ? (
        <p className="py-10 text-center text-xs text-slate-500">{emptyHint}</p>
      ) : (
        <div className="max-w-[92%] space-y-1.5">
          {attachment && (
            <div className="overflow-hidden rounded-2xl rounded-bl-md bg-white shadow-sm">
              <AttachmentBody a={attachment} />
              {!separate && hasText && (
                <div className="whitespace-pre-wrap break-words px-3 py-2 text-[13.5px] leading-snug text-slate-800">
                  {renderTelegramHtml(html)}
                </div>
              )}
            </div>
          )}
          {!separate && attachment && button}
          {(!attachment || separate) && hasText && (
            <div>
              <div className="whitespace-pre-wrap break-words rounded-2xl rounded-bl-md bg-white px-3 py-2 text-[13.5px] leading-snug text-slate-800 shadow-sm">
                {renderTelegramHtml(html)}
              </div>
              {button}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function AttachmentBody({ a }: { a: PreviewAttachment }) {
  if (a.kind === 'photo' && a.thumbUrl)
    return <img src={a.thumbUrl} alt={a.fileName} className="max-h-56 w-full object-cover" />
  const Icon = a.kind === 'video' ? Film : FileText
  return (
    <div className={cn('flex items-center gap-2.5 px-3 py-2.5')}>
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-sky-500 text-white">
        <Icon className="h-5 w-5" />
      </span>
      <span className="min-w-0">
        <span className="block truncate text-[13px] font-medium text-slate-800">{a.fileName}</span>
        <span className="block text-[11.5px] text-slate-500">{formatBytes(a.sizeBytes)}</span>
      </span>
    </div>
  )
}
