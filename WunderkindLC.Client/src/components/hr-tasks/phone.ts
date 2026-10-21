/**
 * Phone helpers of the HR pages (ported from LMS `lib/phone.ts`). Display goes through LC's
 * `formatPhone` (`lib/numberFormat.ts`); these two read any stored spelling — LC's
 * `PhoneInput` value `998901234567`, `+998 90 123 45 67`, a bare local `901234567`.
 */
const COUNTRY = '998'

function hasCountryCode(raw: string | null | undefined): boolean {
  const s = raw ?? ''
  if (/^\s*\+\s*998/.test(s)) return true
  const d = s.replace(/\D/g, '')
  return d.length >= 12 && d.startsWith(COUNTRY)
}

/** The local nine digits of any spelling. */
export function localDigits(raw: string | null | undefined): string {
  let d = (raw ?? '').replace(/\D/g, '')
  if (hasCountryCode(raw)) d = d.slice(COUNTRY.length)
  return d.slice(0, 9)
}

/** `tel:` link target: `+998976666666`. */
export function phoneHref(raw: string | null | undefined): string {
  const d = localDigits(raw)
  return d.length === 9 ? `tel:+${COUNTRY}${d}` : `tel:${(raw ?? '').replace(/[^\d+]/g, '')}`
}
