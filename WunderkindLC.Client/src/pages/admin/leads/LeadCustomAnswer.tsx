import type { LeadEntryField } from '@/api/services/leadEntryForm'
import { Input, Select, Textarea } from '@/components/ui/Input'

/**
 * «Lid kiritish formasi»ning QO'SHIMCHA savoli — turiga qarab matn / raqam / select / radio /
 * checkbox. «Yangi lid» oynasi ham, lid sahifasining chap paneli ham AYNAN shuni chizadi.
 *
 * `inline` — yorliq TASHQARIDA (edutizimdagi "Yorliq: qiymat" qatori): komponent o'zi yorliq
 * chizmaydi, faqat boshqaruv elementini.
 */
export function LeadCustomAnswer({
  field,
  value,
  onSet,
  onToggle,
  inline,
}: {
  field: LeadEntryField
  value: string[]
  onSet: (vals: string[]) => void
  onToggle: (val: string) => void
  inline?: boolean
}) {
  const single = value[0] ?? ''
  const label = inline ? undefined : field.label

  if (field.kind === 'textarea')
    return (
      <Textarea
        label={label}
        required={field.required}
        rows={3}
        placeholder={field.placeholder}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      />
    )

  if (field.kind === 'number')
    return (
      <Input
        label={label}
        required={field.required}
        type="number"
        placeholder={field.placeholder}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      />
    )

  if (field.kind === 'select')
    return (
      <Select
        label={label}
        required={field.required}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      >
        <option value="">— tanlanmagan —</option>
        {field.options.map((o) => (
          <option key={o} value={o}>
            {o}
          </option>
        ))}
      </Select>
    )

  if (field.kind === 'radio' || field.kind === 'checkbox') {
    // checkbox — bir nechta javob, radio — bittasi (ommaviy formadagi bilan bir xil ko'rinish).
    const multiple = field.kind === 'checkbox'
    return (
      <div>
        {label && (
          <span className="mb-1 block text-sm font-medium text-slate-600">
            {label}
            {field.required && <span className="text-red-500"> *</span>}
          </span>
        )}
        <div className="space-y-2">
          {field.options.map((o) => {
            const selected = value.includes(o)
            return (
              <button
                key={o}
                type="button"
                onClick={() => (multiple ? onToggle(o) : onSet(selected ? [] : [o]))}
                className={
                  'flex w-full items-center gap-3 rounded-lg border px-3 py-2 text-left text-sm transition-colors ' +
                  (selected
                    ? 'border-brand-400 bg-brand-50 text-brand-800'
                    : 'border-slate-200 text-slate-700 hover:border-brand-300 hover:bg-slate-50')
                }
              >
                <span
                  className={
                    'h-4 w-4 shrink-0 border-2 ' +
                    (multiple ? 'rounded ' : 'rounded-full ') +
                    (selected ? 'border-brand-500 bg-brand-500' : 'border-slate-300')
                  }
                />
                <span className="flex-1">{o}</span>
              </button>
            )
          })}
        </div>
      </div>
    )
  }

  return (
    <Input
      label={label}
      required={field.required}
      placeholder={field.placeholder}
      value={single}
      onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
    />
  )
}
