import { useMemo, useState } from 'react'
import { Check, ChevronDown, Search } from 'lucide-react'
import { cn } from '@/lib/utils'

export interface PickOption {
  id: string
  label: string
  hint?: string
}

/**
 * Ixcham ko'p tanlov (guruh / kurs / o'qituvchi filtrlari): tugma «Guruh (2)» → qidiruvli ro'yxat.
 * `single` — bitta qiymat (yana bosilsa tozalanadi).
 */
export function MultiPick({
  label,
  options,
  value,
  onChange,
  single = false,
  placeholder = 'Hammasi',
}: {
  label: string
  options: PickOption[]
  value: string[]
  onChange: (next: string[]) => void
  single?: boolean
  placeholder?: string
}) {
  const [open, setOpen] = useState(false)
  const [q, setQ] = useState('')
  const shown = useMemo(() => {
    const n = q.trim().toLowerCase()
    return n ? options.filter((o) => o.label.toLowerCase().includes(n)) : options
  }, [options, q])

  const toggle = (id: string) => {
    if (single) onChange(value.includes(id) ? [] : [id])
    else onChange(value.includes(id) ? value.filter((x) => x !== id) : [...value, id])
    if (single) setOpen(false)
  }

  const summary =
    value.length === 0
      ? placeholder
      : value.length === 1
        ? options.find((o) => o.id === value[0])?.label ?? '1 ta'
        : `${value.length} ta tanlandi`

  return (
    <div className="relative">
      <span className="mb-1 block text-[13px] font-semibold text-[#333]">{label}</span>
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className={cn(
          'flex min-h-[34px] w-full items-center justify-between gap-2 rounded-lg border bg-white px-3 py-1.5 text-left text-[13px]',
          value.length > 0 ? 'border-brand-400 text-brand-700' : 'border-black/25 text-slate-600',
        )}
      >
        <span className="truncate">{summary}</span>
        <ChevronDown className={cn('h-4 w-4 shrink-0 transition-transform', open && 'rotate-180')} />
      </button>
      {open && (
        <>
          <div className="fixed inset-0 z-30" onClick={() => setOpen(false)} />
          <div className="absolute z-40 mt-1 w-full min-w-[240px] rounded-xl border border-slate-200 bg-white p-2 shadow-lg">
            {options.length > 8 && (
              <div className="relative mb-1.5">
                <Search className="pointer-events-none absolute left-2.5 top-2 h-4 w-4 text-slate-400" />
                <input
                  autoFocus
                  value={q}
                  onChange={(e) => setQ(e.target.value)}
                  placeholder="Qidirish..."
                  className="w-full rounded-lg border border-slate-200 py-1.5 pl-8 pr-2 text-sm outline-none focus:border-brand-400"
                />
              </div>
            )}
            <div className="max-h-64 space-y-0.5 overflow-y-auto">
              {shown.length === 0 && <p className="py-3 text-center text-xs text-slate-400">Topilmadi</p>}
              {shown.map((o) => {
                const on = value.includes(o.id)
                return (
                  <button
                    key={o.id}
                    type="button"
                    onClick={() => toggle(o.id)}
                    className={cn(
                      'flex w-full items-center gap-2 rounded-lg px-2 py-1.5 text-left text-sm',
                      on ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-50',
                    )}
                  >
                    <span
                      className={cn(
                        'flex h-4 w-4 shrink-0 items-center justify-center rounded border',
                        on ? 'border-brand-500 bg-brand-500 text-white' : 'border-slate-300',
                      )}
                    >
                      {on && <Check className="h-3 w-3" />}
                    </span>
                    <span className="min-w-0 flex-1 truncate">{o.label}</span>
                    {o.hint && <span className="shrink-0 text-xs text-slate-400">{o.hint}</span>}
                  </button>
                )
              })}
            </div>
            {value.length > 0 && (
              <button
                type="button"
                onClick={() => onChange([])}
                className="mt-1.5 w-full rounded-lg py-1 text-xs text-slate-500 hover:bg-slate-50"
              >
                Tozalash
              </button>
            )}
          </div>
        </>
      )}
    </div>
  )
}
