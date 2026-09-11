import { useState } from 'react'
import { CheckCircle2, Copy, KeyRound, XCircle } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import type { EnvSecret } from '@/api/services/settings'

/**
 * MAXFIY QIYMAT (kalit/token/parol) — UI'dan KIRITILMAYDI.
 *
 * Barcha kalitlar serverdagi `.env` faylida turadi va bazaga umuman yozilmaydi (backend:
 * `AppSecrets`). Shuning uchun sozlamalar sahifasida kiritish maydoni o'rniga shu kartochka
 * chiqadi: sozlanganmi, `.env` ga qaysi qatorni qo'shish kerak va qanday qo'llash kerak.
 */
export function EnvSecretField({
  label,
  secret,
  hint,
  sample = '...',
}: {
  label: string
  /** Backend qaytargan holat: `.env` o'zgaruvchisi nomi + qiymat berilganmi. */
  secret?: EnvSecret | null
  /** Qo'shimcha izoh (kalitni qayerdan olish kerakligi). */
  hint?: React.ReactNode
  /** Namuna qiymat (`.env` qatorida ko'rsatiladi). */
  sample?: string
}) {
  const [copied, setCopied] = useState(false)
  const envKey = secret?.envKey ?? ''
  const configured = !!secret?.configured
  const line = `${envKey}=${sample}`

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(line)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      /* clipboard mavjud emas (http) — qo'lda ko'chiriladi */
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-slate-50 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <KeyRound className="h-4 w-4 text-slate-400" />
        <span className="text-sm font-medium text-slate-700">{label}</span>
        {configured ? (
          <Badge tone="green">
            <CheckCircle2 className="h-3.5 w-3.5" /> Sozlangan
          </Badge>
        ) : (
          <Badge tone="default">
            <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
          </Badge>
        )}
      </div>

      <p className="mt-1.5 text-xs text-slate-500">
        Bu maxfiy qiymat <b>serverdagi .env faylida</b> saqlanadi — bazaga yozilmaydi va bu yerda
        ko'rsatilmaydi (zaxira nusxa yoki baza dump'i orqali sizib chiqmasligi uchun).
      </p>
      {hint && <p className="mt-1 text-xs text-slate-400">{hint}</p>}

      {envKey && (
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <code className="rounded-lg border border-slate-200 bg-white px-2 py-1 font-mono text-[11.5px] text-slate-600">
            {line}
          </code>
          <button
            type="button"
            onClick={copy}
            className="inline-flex items-center gap-1 rounded-lg border border-slate-200 bg-white px-2 py-1 text-xs font-medium text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-700"
            title=".env qatorini nusxalash"
          >
            <Copy className="h-3.5 w-3.5" /> {copied ? 'Nusxalandi' : 'Nusxalash'}
          </button>
        </div>
      )}

      <p className="mt-2 text-xs text-slate-400">
        O'zgartirish: serverdagi <code className="rounded bg-slate-100 px-1">.env</code> faylini
        tahrirlang, so'ng <code className="rounded bg-slate-100 px-1">docker compose up -d</code>.
      </p>
    </div>
  )
}
