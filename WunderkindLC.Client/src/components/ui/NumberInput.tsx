import { useEffect, useLayoutEffect, useRef, useState, type ComponentType, type InputHTMLAttributes, type Ref } from 'react'
import { Input } from './Input'
import {
  formatNumberInput,
  formatNumberValue,
  parseNumberInput,
  type NumberFormatOptions,
} from '@/lib/numberFormat'

type FieldProps = InputHTMLAttributes<HTMLInputElement> & {
  ref?: Ref<HTMLInputElement>
  label?: string
  required?: boolean
}

export interface NumberInputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'defaultValue' | 'onChange' | 'type'>,
    Omit<NumberFormatOptions, 'decimals'> {
  /** Son yoki bo'sh (`null`/`undefined` — maydon BO'SH ko'rinadi, "0" emas). */
  value: number | null | undefined
  /** Bo'shatilganda `null` keladi — chaqiruvchi o'zi hal qiladi (saqlashda 0 deb olish, xato berish...). */
  onChange: (value: number | null) => void
  label?: string
  /** Qaysi maydon ko'rinishida chizilsin: `Input` (standart), `DrawerInput` yoki yalang `'input'`. */
  as?: ComponentType<FieldProps> | 'input'
  /**
   * ⚠️ ISTISNO — FAQAT KPI formulasining koeffitsiyentlari uchun (jarima −0,2 · pog'ona 1,2 ·
   * chegara 35,99%): ular formulaning o'zida kasr, butun son qilinsa KPI hisobi buziladi.
   * Boshqa HECH QAYERDA ishlatilmaydi — foydalanuvchi qarori: kasr son kiritilmaydi.
   */
  fractional?: boolean
}

/**
 * RAQAM maydoni — `type="number"` O'RNIGA.
 *
 * Nega `type="number"` emas: u bo'sh qiymatni `''` qilib beradi, `Number('')` esa `0` — ya'ni
 * maydonni o'chirib bo'lmasdi ("0" qaytib yozilardi), ustiga u guruh ajratgichini ko'rsata olmaydi.
 * Bu yerda matn maydoni + `inputMode` (telefonda raqamli klaviatura): yozish paytida formatlanadi
 * (`group` bo'lsa `1 250 000`), bo'shatilsa `null` qaytadi, kursor joyidan sakramaydi.
 *
 * ⚠️ FAQAT BUTUN SON (foydalanuvchi qarori, 2026-09-27: "kasr son kiritilmaydi umuman").
 * Vergul/nuqta yozib bo'lmaydi. Tashqaridan kasrli qiymat kelsa (masalan server qisman oy qarzini
 * kasr bilan hisoblagan) — maydon YAXLITLANGAN sonni ko'rsatadi va shu sonni `onChange` bilan
 * formaga ham qaytaradi: ekrandagi son bilan serverga ketadigan son hech qachon farq qilmaydi.
 */
export function NumberInput({
  value,
  onChange,
  group = false,
  allowNegative = false,
  as = Input,
  label,
  fractional = false,
  ...rest
}: NumberInputProps) {
  const opts: NumberFormatOptions = { decimals: fractional ? 2 : 0, group, allowNegative }
  const [text, setText] = useState(() => formatNumberValue(value, opts))
  // Oxirgi ko'rilgan TASHQI qiymat — tashqaridan o'zgarganini bilish uchun (derived state naqshi).
  const [seen, setSeen] = useState<number | null>(value ?? null)
  const ref = useRef<HTMLInputElement>(null)
  const caret = useRef<number | null>(null)

  // Tashqaridan qiymat o'zgarsa (forma tozalandi, tahrirlash uchun ochildi) — matn yangilanadi.
  // O'zimiz yozgan qiymat qaytib kelsa matn TEGILMAYDI, aks holda yozilayotgan matn (masalan manfiy
  // son boshidagi "-" yoki KPI koeffitsiyentidagi "1,") qayta formatlanib yo'qolardi.
  const current = value ?? null
  if (current !== seen) {
    setSeen(current)
    if (parseNumberInput(text) !== current) setText(formatNumberValue(current, opts))
  }

  // Kasrli tashqi qiymat → yaxlitlab formaga qaytaramiz (ko'rinadigan son = yuboriladigan son).
  useEffect(() => {
    if (!fractional && current !== null && !Number.isInteger(current)) onChange(Math.round(current))
  }, [fractional, current, onChange])

  // Formatlashdan keyin kursorni o'sha raqam oldiga qaytaramiz (bo'sh joy qo'shilsa oxiriga sakramasin).
  useLayoutEffect(() => {
    const el = ref.current
    if (caret.current !== null && el && document.activeElement === el) {
      el.setSelectionRange(caret.current, caret.current)
    }
    caret.current = null
  })

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const raw = e.target.value
    const pos = e.target.selectionStart ?? raw.length
    const significant = (s: string) => s.replace(/[^\d.,-]/g, '').length
    const before = significant(raw.slice(0, pos))
    const next = formatNumberInput(raw, opts)
    let idx = 0
    for (let count = 0; idx < next.length && count < before; idx++) if (/[\d.,-]/.test(next[idx])) count++
    caret.current = idx
    setText(next)
    const n = parseNumberInput(next)
    setSeen(n)
    onChange(n)
  }

  const props: FieldProps = {
    ...rest,
    ref,
    type: 'text',
    inputMode: fractional ? 'decimal' : 'numeric',
    autoComplete: 'off',
    value: text,
    onChange: handleChange,
  }
  if (as === 'input') return <input {...props} />
  const Comp = as
  return label !== undefined ? <Comp {...props} label={label} /> : <Comp {...props} />
}

/** SUMMA maydoni: 3 xonadan ajratiladi (`1 250 000`), manfiy yo'q. */
export function MoneyInput(props: Omit<NumberInputProps, 'group'>) {
  return <NumberInput {...props} group />
}
