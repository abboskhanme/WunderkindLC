import type { InputHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import { maskPhone } from '@/lib/utils'

const base =
  'w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-800 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100'

interface FieldWrap {
  label?: string
  required?: boolean
}

function Label({ label, required, children }: FieldWrap & { children: React.ReactNode }) {
  return (
    <label className="block">
      {label && (
        <span className="mb-1 block text-sm font-medium text-slate-600">
          {label}
          {required && <span className="text-red-500"> *</span>}
        </span>
      )}
      {children}
    </label>
  )
}

interface PhoneInputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'onChange'>, FieldWrap {
  /** Masklanmagan raqam (backend formatida: "998901234567" yoki ""). */
  value: string
  /** Yangilanganda displayValue o'zgacha — maskalanmagan raqamni qaytaring. */
  onChange: (unmaskedValue: string) => void
}

/**
 * Telefon raqami input komponenti maskalash bilan.
 * - Placeholder: "(998) 90-123-45-67"
 * - Value (internal): masklanmagan "998901234567" yoki ""
 * - Display: maskalanmagan → "(998) 90-123-45-67"
 * - onChange: maskalanmagan raqam qaytaradi
 */
export function PhoneInput({ label, required, className, value, onChange, ...rest }: PhoneInputProps) {
  const displayValue = maskPhone(value ?? '')

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value
    const unmasked = raw.replace(/\D/g, '')

    // Butunlay bo'shatilsa — "998" majburan qaytarilmaydi, maydon TOZALANADI (placeholder ko'rinadi).
    // Yozilganda 998 prefiksi qayta paydo bo'ladi.
    if (unmasked === '') {
      onChange('')
      return
    }
    // Backspace bilan faqat "998" prefiksi qolganda yana o'chirishga urinilsa — to'liq tozalanadi
    // (aks holda "998" o'chmay qolib ketardi).
    const deleting = raw.length < displayValue.length
    if (deleting && unmasked === '998') {
      onChange('')
      return
    }

    // +998 boshlanmasa uni qo'sh
    let normalized = unmasked.startsWith('998') ? unmasked : '998' + unmasked

    // Maksimal 12 raqamga chegarala
    if (normalized.length > 12) normalized = normalized.slice(0, 12)

    onChange(normalized)
  }

  return (
    <Label label={label} required={required}>
      <input
        type="tel"
        className={cn(base, className)}
        placeholder="(998) 90-123-45-67"
        value={displayValue}
        onChange={handleChange}
        {...rest}
      />
    </Label>
  )
}
