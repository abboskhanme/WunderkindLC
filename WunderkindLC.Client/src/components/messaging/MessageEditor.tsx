import { useRef } from 'react'

/** Matnga qo'yiladigan o'rinbosar (token) ta'rifi. */
export interface TokenDef {
  token: string
  label: string
}

/** Tayyor matn (shablon) chipi. */
export interface TemplateDef {
  name: string
  text: string
}

/**
 * Yagona xabar matni tahrirlagichi: textarea + token chiplari (kursor joyiga qo'yadi) +
 * ixtiyoriy shablon chiplari. Composer, Telegram xabar oynasi va avto xabar qoidasi — hammasi shu
 * komponentni ishlatadi. (SMS belgi/bo'lak hisoblagichi SMS kanali bilan birga olib tashlandi.)
 */
export function MessageEditor({
  value,
  onChange,
  tokens = [],
  templates,
  onTemplatePick,
  rows = 5,
  placeholder,
  label,
  hint,
  className,
}: {
  value: string
  onChange: (v: string) => void
  /** Token chiplari (bo'sh — chiplar ko'rinmaydi) */
  tokens?: TokenDef[]
  /** Shablon chiplari (berilsa — matn ustida ko'rsatiladi) */
  templates?: TemplateDef[]
  /** Shablon bosilganda (berilmasa — matn to'g'ridan-to'g'ri qo'yiladi) */
  onTemplatePick?: (text: string, name: string) => void
  rows?: number
  placeholder?: string
  /** Textarea ustidagi yorliq */
  label?: string
  /** Pastdagi kichik izoh matni */
  hint?: string
  className?: string
}) {
  const taRef = useRef<HTMLTextAreaElement>(null)

  const insertToken = (token: string) => {
    const el = taRef.current
    if (!el) {
      onChange(value + token)
      return
    }
    const start = el.selectionStart ?? value.length
    const end = el.selectionEnd ?? value.length
    onChange(value.slice(0, start) + token + value.slice(end))
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  return (
    <div className={className}>
      {label && <label className="mb-1 block text-sm font-medium text-slate-600">{label}</label>}

      {templates && templates.length > 0 && (
        <div className="mb-1.5 flex flex-wrap gap-1.5">
          {templates.map((t, i) => (
            <button
              key={`${t.name}-${i}`}
              type="button"
              onClick={() => (onTemplatePick ? onTemplatePick(t.text, t.name) : onChange(t.text))}
              className="rounded-full border border-slate-200 px-2.5 py-1 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
              title={t.text}
            >
              {t.name}
            </button>
          ))}
        </div>
      )}

      <textarea
        ref={taRef}
        rows={rows}
        className="w-full resize-none rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />

      {tokens.length > 0 && (
        <div className="mt-1 flex items-start justify-between gap-2">
          <div className="flex flex-wrap gap-1.5">
            {tokens.map((t) => (
              <button
                key={t.token}
                type="button"
                onClick={() => insertToken(t.token)}
                className="rounded-md bg-slate-100 px-2 py-1 font-mono text-xs font-medium text-slate-600 transition-colors hover:bg-brand-50 hover:text-brand-700"
                title={t.token}
              >
                {t.label}
              </button>
            ))}
          </div>
        </div>
      )}

      {hint && <p className="mt-1 text-xs text-slate-400">{hint}</p>}
    </div>
  )
}
