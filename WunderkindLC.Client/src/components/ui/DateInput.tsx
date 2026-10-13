import { useRef, useState, type ChangeEvent, type InputHTMLAttributes } from 'react'
import { CalendarDays } from 'lucide-react'
import { cn } from '@/lib/utils'
import { dmyToIso, formatTyped, isoToDmy, splitLayoutClasses } from '@/lib/dateInput'

type DateInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>

/**
 * Sana maydoni — HAR DOIM «kun.oy.yil» (15.10.2026), brauzer tilidan qat'i nazar (`lib/dateInput.ts`).
 *
 * `<input type="date">` ning O'RNIGA, o'sha props bilan: `value`/`defaultValue` va `onChange` qiymati — ISO
 * "yyyy-MM-dd". `onChange` HAQIQIY `ChangeEvent` oladi (yashirin native date maydondan, `e.target.value` = ISO,
 * `e.target.name` = `name`), ya'ni `onChange={(e) => setX(e.target.value)}` va `FormData` o'zgarishsiz ishlaydi.
 *
 * - Yozish: faqat raqamlar, nuqtalar o'zi qo'yiladi ("15102026" → "15.10.2026"). Qiymat faqat TO'LIQ va to'g'ri
 *   sana bo'lganda (kalendarda bor, `min`/`max` ichida) yuboriladi; chala matn maydondan chiqqanda eski qiymatga qaytadi.
 * - O'ngdagi tugma — brauzerning kalendari (`showPicker`).
 */
export function DateInput({
  value,
  defaultValue,
  onChange,
  onBlur,
  className,
  style,
  min,
  max,
  name,
  disabled,
  readOnly,
  required,
  placeholder,
  ...rest
}: DateInputProps) {
  const controlled = value !== undefined
  const valueIso = controlled ? String(value ?? '') : ''
  const minIso = min === undefined ? undefined : String(min)
  const maxIso = max === undefined ? undefined : String(max)
  const nativeRef = useRef<HTMLInputElement>(null)
  const [text, setText] = useState(() => isoToDmy(controlled ? valueIso : String(defaultValue ?? '')))
  const [seen, setSeen] = useState(valueIso)

  // Tashqaridan o'zgargan qiymat (boshqa maydon, forma tozalandi, serverdan keldi) — render paytidagi moslash
  // (effektda setState emas). Foydalanuvchi yozib turgan chala matn o'z qiymatiga mos bo'lsa tegilmaydi.
  if (controlled && valueIso !== seen) {
    setSeen(valueIso)
    if (dmyToIso(text) !== valueIso) setText(isoToDmy(valueIso))
  }

  const currentIso = () => (controlled ? valueIso : (nativeRef.current?.value ?? ''))

  /** Yashirin native maydonga qiymat yozib, HAQIQIY input hodisasini chiqaradi — React uning `onChange`ini chaqiradi. */
  const emit = (iso: string) => {
    const el = nativeRef.current
    if (!el) return
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(el, iso)
    el.dispatchEvent(new Event('input', { bubbles: true }))
  }

  const onType = (e: ChangeEvent<HTMLInputElement>) => {
    const next = formatTyped(e.target.value)
    setText(next)
    const iso = dmyToIso(next, minIso, maxIso)
    if (iso !== null && iso !== currentIso()) emit(iso)
  }

  const onNative = (e: ChangeEvent<HTMLInputElement>) => {
    if (!controlled) setText(isoToDmy(e.target.value))
    onChange?.(e)
  }

  const openPicker = () => {
    const el = nativeRef.current
    if (!el) return
    try {
      el.showPicker()
    } catch {
      // Eski brauzer: showPicker yo'q — kalendar maydonning o'zidan ochiladi.
      el.focus()
      el.click()
    }
  }

  const invalid = text !== '' && dmyToIso(text, minIso, maxIso) === null
  const layout = splitLayoutClasses(className)

  return (
    <span className={cn('relative inline-block align-middle', layout.wrapper)}>
      <input
        {...rest}
        type="text"
        inputMode="numeric"
        autoComplete="off"
        spellCheck={false}
        maxLength={10}
        placeholder={placeholder ?? 'kk.oo.yyyy'}
        className={cn(layout.input, 'w-full')}
        style={{ paddingRight: 32, ...style }}
        value={text}
        disabled={disabled}
        readOnly={readOnly}
        required={required}
        aria-invalid={invalid || undefined}
        onChange={onType}
        onBlur={(e) => {
          // Chala yoki noto'g'ri sana — saqlangan qiymat qaytadi (jimgina yarim sana qolmasin).
          if (dmyToIso(text, minIso, maxIso) === null) setText(isoToDmy(currentIso()))
          onBlur?.(e)
        }}
      />
      <button
        type="button"
        tabIndex={-1}
        aria-label="Kalendardan tanlash"
        disabled={disabled || readOnly}
        onClick={openPicker}
        className="absolute right-1.5 top-1/2 flex h-6 w-6 -translate-y-1/2 items-center justify-center rounded opacity-60 hover:opacity-100 disabled:opacity-30"
      >
        <CalendarDays className="h-4 w-4" />
      </button>
      <input
        ref={nativeRef}
        type="date"
        tabIndex={-1}
        aria-hidden
        name={name}
        min={min}
        max={max}
        disabled={disabled}
        value={controlled ? valueIso : undefined}
        defaultValue={controlled ? undefined : String(defaultValue ?? '')}
        onChange={onNative}
        className="pointer-events-none absolute bottom-0 left-0 h-px w-full opacity-0"
      />
    </span>
  )
}
