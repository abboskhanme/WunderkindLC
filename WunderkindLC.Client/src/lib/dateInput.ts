// Sana maydoni (`components/ui/DateInput`) qoidalari — sof funksiyalar, testlangan (`__tests__/dateInput.test.ts`).
//
// Brauzerning o'z `<input type="date">` i sanani BRAUZER TILIDA ko'rsatadi: inglizcha (AQSh) Chrome'da "10/15/2026"
// (oy birinchi). Markazda sana HAR DOIM kun.oy.yil — "15.10.2026". Qiymat (`value`/`onChange`) esa avvalgidek ISO
// "yyyy-MM-dd": server va formalar o'zgarmaydi.

const ISO = /^(\d{4})-(\d{2})-(\d{2})$/

/** Haqiqiy kalendar sanasimi (31.02 yo'q, 29.02 faqat kabisa yilida). */
export function isValidIso(iso: string): boolean {
  const m = ISO.exec(iso)
  if (!m) return false
  const [y, mo, d] = [Number(m[1]), Number(m[2]), Number(m[3])]
  if (y < 1900 || y > 2100 || mo < 1 || mo > 12 || d < 1) return false
  return d <= new Date(Date.UTC(y, mo, 0)).getUTCDate()
}

/** "2026-10-15" → "15.10.2026"; bo'sh/buzuq → "". */
export function isoToDmy(iso: string | null | undefined): string {
  const m = ISO.exec(iso ?? '')
  return m ? `${m[3]}.${m[2]}.${m[1]}` : ''
}

/**
 * Yozilgan matn → ko'rinish: faqat raqamlar (8 tagacha), nuqta KEYINGI raqam bilan qo'yiladi ("1510" → "15.10") —
 * shunda Backspace nuqtada "tiqilib" qolmaydi. Joylashtirilgan ISO ("2026-10-15") ham tushuniladi.
 */
export function formatTyped(raw: string): string {
  const iso = ISO.exec(raw.trim())
  const digits = iso ? `${iso[3]}${iso[2]}${iso[1]}` : raw.replace(/\D/g, '').slice(0, 8)
  if (digits.length <= 2) return digits
  if (digits.length <= 4) return `${digits.slice(0, 2)}.${digits.slice(2)}`
  return `${digits.slice(0, 2)}.${digits.slice(2, 4)}.${digits.slice(4)}`
}

/**
 * To'liq ko'rinish ("15.10.2026") → ISO; to'liq emas, kalendarda yo'q yoki [min, max] dan tashqari → null.
 * Bo'sh matn → "" (maydon tozalandi).
 */
export function dmyToIso(text: string, min?: string, max?: string): string | null {
  if (text === '') return ''
  const m = /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(text)
  if (!m) return null
  const iso = `${m[3]}-${m[2]}-${m[1]}`
  if (!isValidIso(iso)) return null
  if (min && isValidIso(min) && iso < min) return null
  if (max && isValidIso(max) && iso > max) return null
  return iso
}

/**
 * `className` — native `<input type="date">` uchun yozilgan. Joylashuv klasslari (kenglik, flex/grid, tashqi chekka)
 * o'ramga (wrapper) o'tadi, qolgani (ramka, fon, ichki chekka, shrift) ko'rinadigan maydonda qoladi — aks holda
 * `w-40` yoki `flex-1` bilan berilgan maydon o'ram ichida boshqacha kenglikda chiqardi.
 */
export function splitLayoutClasses(className?: string): { wrapper: string; input: string } {
  const layout = /^(?:[a-z0-9-]+:)*!?-?(?:w-|min-w-|max-w-|flex-|grow|shrink|basis-|col-span-|col-start-|row-span-|self-|justify-self-|order-|m[trblxyse]?-)/
  const wrapper: string[] = []
  const input: string[] = []
  for (const c of (className ?? '').split(/\s+/).filter(Boolean)) (layout.test(c) ? wrapper : input).push(c)
  // Bir xil kenglik "oilasi" takrorlansa (asosiy `w-full` + chaqiruvchining `w-[150px]`) — OXIRGISI qoladi: aks holda
  // CSS tartibi g'olibni tanlardi va 150px lik filtr butun qatorni egallardi (native maydonda o'ram yo'q edi).
  const family = (c: string) => /^((?:[a-z0-9-]+:)*)(min-w|max-w|w)-/.exec(c)?.slice(1, 3).join('|')
  const lastOf = new Map<string, number>()
  wrapper.forEach((c, i) => {
    const f = family(c)
    if (f) lastOf.set(f, i)
  })
  const kept = wrapper.filter((c, i) => {
    const f = family(c)
    return !f || lastOf.get(f) === i
  })
  return { wrapper: kept.join(' '), input: input.join(' ') }
}
