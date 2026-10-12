import { useRef } from 'react'
import { Bold, Eraser, Italic, Link2, Underline } from 'lucide-react'

/** Formatlash teglari (Telegram HTML oq ro'yxati) — «Formatni tozalash» shularni olib tashlaydi. */
const FORMAT_TAG_RX = /<\/?(?:b|strong|i|em|u|ins|s|strike|del|code|pre|a)(?:\s[^<>]*)?>/gi

/** Tanlangan qismni teg bilan o'raydi (sof funksiya — kursor joyini ham qaytaradi). */
function wrapSelection(
  value: string, start: number, end: number, open: string, close: string, fallback = '',
): { text: string; selStart: number; selEnd: number } {
  const inner = value.slice(start, end) || fallback
  const text = value.slice(0, start) + open + inner + close + value.slice(end)
  return { text, selStart: start + open.length, selEnd: start + open.length + inner.length }
}

/** Formatlash teglarini olib tashlaydi (tanlangan qismdan, tanlov bo'lmasa — butun matndan). */
function clearFormatting(value: string, start: number, end: number): string {
  if (start === end) return value.replace(FORMAT_TAG_RX, '')
  return value.slice(0, start) + value.slice(start, end).replace(FORMAT_TAG_RX, '') + value.slice(end)
}

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
  formatting = false,
  error,
}: {
  value: string
  onChange: (v: string) => void
  /** Telegram formatlash paneli («Qalin», «Kursiv», «Tagiga chizilgan», «Havola», «Formatni tozalash»). */
  formatting?: boolean
  /** Shablon xatolari (server preview'idan) — textarea ostida qizil. */
  error?: string | null
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

  const selection = () => {
    const el = taRef.current
    return {
      el,
      start: el?.selectionStart ?? value.length,
      end: el?.selectionEnd ?? value.length,
    }
  }

  const restore = (el: HTMLTextAreaElement | null, s: number, e: number) =>
    requestAnimationFrame(() => {
      if (!el) return
      el.focus()
      el.setSelectionRange(s, e)
    })

  const wrap = (open: string, close: string, fallback = '') => {
    const { el, start, end } = selection()
    const r = wrapSelection(value, start, end, open, close, fallback)
    onChange(r.text)
    restore(el, r.selStart, r.selEnd)
  }

  const addLink = () => {
    const url = window.prompt("Havola manzili (https://...)", 'https://')
    if (!url || !url.trim() || url.trim() === 'https://') return
    // Qo'shtirnoq HTML atributini buzmasin — server baribir tekshiradi (faqat http/https/tg).
    const safe = url.trim().replace(/"/g, '%22')
    wrap(`<a href="${safe}">`, '</a>', safe)
  }

  const clear = () => {
    const { el, start, end } = selection()
    const next = clearFormatting(value, start, end)
    onChange(next)
    restore(el, Math.min(start, next.length), Math.min(start, next.length))
  }

  return (
    <div className={className}>
      {label && <label className="mb-1 block text-sm font-medium text-slate-600">{label}</label>}

      {formatting && (
        <div className="mb-1.5 flex flex-wrap items-center gap-1">
          <ToolBtn title="Qalin" onClick={() => wrap('<b>', '</b>')}>
            <Bold className="h-3.5 w-3.5" /> Qalin
          </ToolBtn>
          <ToolBtn title="Kursiv" onClick={() => wrap('<i>', '</i>')}>
            <Italic className="h-3.5 w-3.5" /> Kursiv
          </ToolBtn>
          <ToolBtn title="Tagiga chizilgan" onClick={() => wrap('<u>', '</u>')}>
            <Underline className="h-3.5 w-3.5" /> Tagiga chizilgan
          </ToolBtn>
          <ToolBtn title="Havola" onClick={addLink}>
            <Link2 className="h-3.5 w-3.5" /> Havola
          </ToolBtn>
          <ToolBtn title="Formatni tozalash" onClick={clear}>
            <Eraser className="h-3.5 w-3.5" /> Formatni tozalash
          </ToolBtn>
        </div>
      )}

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
      {error && <p className="mt-1 text-sm font-medium text-red-600">{error}</p>}

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

function ToolBtn({ title, onClick, children }: { title: string; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      type="button"
      title={title}
      // Fokus textarea'dan ketmasin — tanlangan qism saqlansin.
      onMouseDown={(e) => e.preventDefault()}
      onClick={onClick}
      className="inline-flex items-center gap-1 rounded-md border border-slate-200 bg-white px-2 py-1 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:bg-brand-50 hover:text-brand-700"
    >
      {children}
    </button>
  )
}
