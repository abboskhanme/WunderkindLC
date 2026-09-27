/**
 * RAQAM va TELEFONNI kiritish/ko'rsatish — yagona qoidalar (O'zbekiston odatlari).
 *
 * - Summa: raqamlar 3 xonadan BO'SH JOY bilan ajratiladi — `1 250 000`; kasr ajratgichi VERGUL — `12,5`.
 * - Telefon: `+998 90 123 45 67` (davlat kodi · operator · 3 · 2 · 2).
 *
 * ⚠️ Maydon BO'SH bo'la oladi: bo'sh matn → `null` (ilgari `Number('') === 0` sababli
 * o'chirilgan maydonga "0" qaytib yozilib qolardi — foydalanuvchi uni o'chira olmasdi).
 *
 * Sof funksiyalar — komponent `components/ui/NumberInput.tsx` da (eslint
 * `react-refresh/only-export-components`).
 */

/** Kiritish maydonidagi guruh ajratgichi — oddiy bo'sh joy (kursor bilan tahrirlashga qulay). */
export const GROUP_SEP = ' '

export interface NumberFormatOptions {
  /** Kasr xonalari soni (0 — faqat butun son). */
  decimals?: number
  /** 3 xonadan ajratilsinmi (summa — ha; yil, port, kod — yo'q). */
  group?: boolean
  /** Manfiy son kiritish mumkinmi. */
  allowNegative?: boolean
}

/** "1250000" → "1 250 000". Faqat raqamlar qatori kutiladi. */
export function groupDigits(digits: string): string {
  return digits.replace(/\B(?=(\d{3})+(?!\d))/g, GROUP_SEP)
}

/**
 * Yozish PAYTIDA matnni tozalaydi va formatlaydi: begona belgilar tashlanadi, boshidagi ortiqcha
 * nollar olib tashlanadi ("05" → "5"), kasr qismi `decimals` bilan cheklanadi, butun qism guruhlanadi.
 * Kasr ajratgichi sifatida nuqta ham, vergul ham qabul qilinadi — ko'rsatishda vergul.
 */
export function formatNumberInput(raw: string, opts: NumberFormatOptions = {}): string {
  const { decimals = 0, group = true, allowNegative = false } = opts
  const neg = allowNegative && raw.trim().startsWith('-')
  const s = raw.replace(/[^\d.,]/g, '')

  let intPart = s
  let frac: string | null = null
  if (decimals > 0) {
    const i = s.search(/[.,]/)
    if (i >= 0) {
      intPart = s.slice(0, i)
      frac = s.slice(i + 1).replace(/[.,]/g, '').slice(0, decimals)
    }
  } else {
    intPart = s.replace(/[.,]/g, '')
  }
  intPart = intPart.replace(/^0+(?=\d)/, '')
  if (intPart === '' && frac !== null) intPart = '0'

  let out = group ? groupDigits(intPart) : intPart
  if (frac !== null) out += ',' + frac
  return (neg ? '-' : '') + out
}

/** Formatlangan matn → son. Bo'sh (yoki faqat "-") → `null`. */
export function parseNumberInput(text: string): number | null {
  const t = text.replace(/\s/g, '').replace(',', '.')
  if (t === '' || t === '-' || t === '.') return null
  const n = Number(t)
  return Number.isFinite(n) ? n : null
}

/** Son → maydonda ko'rinadigan matn. `null`/`undefined` → bo'sh. */
export function formatNumberValue(n: number | null | undefined, opts: NumberFormatOptions = {}): string {
  if (n === null || n === undefined || !Number.isFinite(n)) return ''
  const { decimals = 0, group = true, allowNegative = false } = opts
  const neg = n < 0 && allowNegative
  const abs = Math.abs(n)
  const factor = 10 ** decimals
  const rounded = Math.round(abs * factor) / factor
  const [intPart, fracPart] = rounded.toFixed(decimals).split('.')
  let out = group ? groupDigits(intPart) : intPart
  // Kasr qismidagi ortiqcha nollar ko'rsatilmaydi: 12,50 → 12,5; 12,00 → 12.
  const frac = (fracPart ?? '').replace(/0+$/, '')
  if (frac) out += ',' + frac
  return (neg ? '-' : '') + out
}

/** Ko'rsatish uchun: 1250000 → "1 250 000" (bo'sh joy bilan, "so'm"siz). */
export function formatNumber(n: number | null | undefined, decimals = 0): string {
  return formatNumberValue(n, { decimals, group: true, allowNegative: true })
}

/* =========================================================================================
 *  TELEFON — "+998 90 123 45 67"
 * ========================================================================================= */

/**
 * Yozish PAYTIDAGI telefon ko'rinishi (qisman ham): raqamlardan `+998 90 123 45 67`.
 * `998` bilan boshlanmasa — qo'shiladi (mahalliy 9 xonali raqam yozilgan). Ko'pi bilan 12 raqam.
 */
export function formatPhoneInput(raw: string): string {
  let d = (raw || '').replace(/\D/g, '')
  if (!d) return ''
  if (!d.startsWith('998')) d = '998' + d
  d = d.slice(0, 12)
  const parts = ['+' + d.slice(0, 3), d.slice(3, 5), d.slice(5, 8), d.slice(8, 10), d.slice(10, 12)]
  return parts.filter(Boolean).join(' ')
}

/**
 * Yozilayotgan telefon maydoni uchun: `next` — maydonning yangi matni, `prev` — oldingisi.
 * O'chirishda faqat "+998" qolsa maydon BUTUNLAY tozalanadi (aks holda prefiks o'chmay qolardi),
 * aks holda `+998 90 123 45 67` ko'rinishiga keltiriladi. `PhoneInput` bilan bir xil xatti-harakat
 * — o'z ko'rinishini saqlashi kerak bo'lgan maydonlar (ommaviy sahifalar) uchun.
 */
export function typedPhoneInput(next: string, prev: string): string {
  if (next.length < prev.length && next.replace(/\D/g, '') === '998') return ''
  return formatPhoneInput(next)
}

/**
 * KO'RSATISH uchun telefon: O'zbek raqami (9 yoki 998 bilan 12 xonali) → `+998 90 123 45 67`.
 * Boshqa (xorijiy, qisqa, noto'g'ri) raqam — o'zgarmasdan qaytadi: uni "998" bilan buzmaymiz.
 */
export function formatPhone(raw: string | null | undefined): string {
  const s = raw ?? ''
  const d = s.replace(/\D/g, '')
  if (d.length === 12 && d.startsWith('998')) return formatPhoneInput(d)
  if (d.length === 9) return formatPhoneInput('998' + d)
  return s
}
