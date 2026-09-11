/** Tailwind class'larni shartli birlashtirish */
export function cn(...classes: Array<string | false | null | undefined>): string {
  return classes.filter(Boolean).join(' ')
}

/**
 * API xatosidan foydalanuvchiga ko'rsatiladigan xabarni chiqaradi.
 * MUHIM: avval backend `{message: "..."}` javobini tekshiradi, keyin `Error.message`ga qaytadi —
 * aksincha emas. Axios'ning `AxiosError` klassi `Error`dan meros oladi, shuning uchun
 * `err instanceof Error` HAR DOIM true qaytaradi va agar shu tekshiruv birinchi bo'lsa, backend'dan
 * kelgan aniq xabar (masalan JournalPolicy taqig'i) o'rniga umumiy "Request failed with status code
 * 400" ko'rsatiladi.
 */
export function apiErrorMessage(err: unknown, fallback: string): string {
  const backendMessage = (err as any)?.response?.data?.message
  if (typeof backendMessage === 'string' && backendMessage.length > 0) return backendMessage
  return err instanceof Error ? err.message : fallback
}

/** Mock API kechikishini simulyatsiya qilish */
export function delay(ms = 500): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/**
 * ISO sanani DD.MM.YYYY ko'rinishida chiqarish.
 * MUHIM: saqlangan satrni TO'G'RIDAN-TO'G'RI o'qiymiz (yyyy-MM-dd qismi) — `new Date()` orqali emas.
 * Shu sabab brauzer vaqt mintaqasidan QAT'I NAZAR server (Toshkent) sanasi aynan ko'rinadi (siljimaydi).
 * Noma'lum formatlar uchun `new Date()` ga qaytamiz.
 */
export function formatDate(iso: string): string {
  if (!iso) return ''
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso) // "yyyy-MM-dd" yoki "yyyy-MM-ddTHH:mm:ss" boshlanishi
  if (m) return `${m[3]}.${m[2]}.${m[1]}`
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  const dd = String(d.getDate()).padStart(2, '0')
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  return `${dd}.${mm}.${d.getFullYear()}`
}

/**
 * ISO timestamp ("yyyy-MM-ddTHH:mm:ss") → "DD.MM.YYYY HH:mm". Vaqt qismi bo'lmasa — faqat sana.
 * Butun ilovada YAGONA sana+vaqt formati. Satrdan o'qiydi (TZ siljishisiz — server vaqti aynan ko'rinadi).
 */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const time = /T(\d{2}:\d{2})/.exec(iso) // "Thh:mm"
  const date = formatDate(iso)
  return time ? `${date} ${time[1]}` : date
}

/** ISO timestamp → "HH:mm" (faqat vaqt). Satrdan o'qiydi (TZ siljishisiz). */
export function formatTime(iso: string | null | undefined): string {
  if (!iso) return ''
  const m = /T(\d{2}:\d{2})/.exec(iso)
  return m ? m[1] : ''
}

/**
 * Telefon raqamini maskalash: +(998) 90-123-45-67
 * Input: "998901234567" yoki "+998901234567" yoki "901234567"
 * Output: "(998) 90-123-45-67" (+998 prefix avtomatik qo'shiladi)
 */
export function maskPhone(raw: string): string {
  if (!raw) return ''
  // Faqat raqamlar qol
  let digits = raw.replace(/\D/g, '')

  // +998 prefix qo'sh (agar yo'q bo'lsa)
  if (!digits.startsWith('998')) digits = '998' + digits

  // Maksimal 12 raqamga chegarala (998 + 9 raqam)
  if (digits.length > 12) digits = digits.slice(0, 12)

  // Format: (998) XX-XXX-XX-XX
  let formatted = ''
  if (digits.length <= 3) {
    formatted = digits.length > 0 ? '(' + digits : ''
  } else if (digits.length <= 5) {
    formatted = '(' + digits.slice(0, 3) + ') ' + digits.slice(3)
  } else if (digits.length <= 8) {
    formatted = '(' + digits.slice(0, 3) + ') ' + digits.slice(3, 5) + '-' + digits.slice(5)
  } else if (digits.length <= 10) {
    formatted = '(' + digits.slice(0, 3) + ') ' + digits.slice(3, 5) + '-' + digits.slice(5, 8) + '-' + digits.slice(8)
  } else {
    formatted = '(' + digits.slice(0, 3) + ') ' + digits.slice(3, 5) + '-' + digits.slice(5, 8) + '-' + digits.slice(8, 10) + '-' + digits.slice(10)
  }

  return formatted
}

/**
 * Formatlanmish telefon raqamidan (+998 bilan faqat raqamlarni ol.
 * Input: "(998) 90-123-45-67"
 * Output: "998901234567" (backend'ga yuborish uchun)
 */
export function unmaskPhone(formatted: string): string {
  if (!formatted) return ''
  return formatted.replace(/\D/g, '')
}

/** Ma'lumotlarni CSV faylga yuklab olish (Excel uchun UTF-8 BOM bilan) */
export function exportToCsv(filename: string, headers: string[], rows: string[][]): void {
  const escape = (v: string) => `"${String(v).replace(/"/g, '""')}"`
  const csv = [headers, ...rows].map((r) => r.map(escape).join(',')).join('\r\n')
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8;' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

/** Oddiy noyob ID */
export function uid(): string {
  return crypto.randomUUID()
}

/** Pulni "850 000 so'm" ko'rinishida formatlash */
export function formatMoney(n: number): string {
  return `${new Intl.NumberFormat('ru-RU').format(n)} so'm`
}

/* ─────────── PER-GURUH BALANS RANGI (jurnal, davomat, guruh ro'yxatlari) ───────────
 * Rang shkalasi (og'irlik bo'yicha, YUQORIDAGISI USTUN):
 *   1) 2+ OYLIK QARZ  → binafsha-pushti (fuchsia) — eng og'ir holat, qizildan USTUN;
 *   2) qarzdor (balance < 0) → qizil;
 *   3) to'lagan → yashil.
 * `debtMonths` — SHU GURUH bo'yicha to'liq yopilmagan oylar soni (server: GroupBalanceService).
 * Eski chaqiruvlar (debtMonths berilmagan) avvalgidek qizil/yashil bo'lib qolaveradi. */

/** Nechta oylik qarzdan boshlab "og'ir qarzdor" (binafsha-pushti) hisoblanadi. */
export const HEAVY_DEBT_MONTHS = 2

/** Ism/matn rangi: 2+ oylik qarz → fuchsia, qarzdor → qizil, aks holda yashil. */
export function balanceTextCls(balance: number, debtMonths = 0): string {
  if (debtMonths >= HEAVY_DEBT_MONTHS) return 'text-fuchsia-600'
  return balance < 0 ? 'text-red-600' : 'text-emerald-700'
}

/** Yonidagi nuqta (yoki fon) rangi — matn rangi bilan bir xil shkala. */
export function balanceDotCls(balance: number, debtMonths = 0): string {
  if (debtMonths >= HEAVY_DEBT_MONTHS) return 'bg-fuchsia-500'
  return balance < 0 ? 'bg-red-500' : 'bg-emerald-500'
}

/** Tooltip matni: 2+ oylik qarzda necha oyligi ham yoziladi ("3 oylik qarz (shu guruh): …"). */
export function balanceTitle(balance: number, debtMonths = 0): string {
  if (debtMonths >= HEAVY_DEBT_MONTHS)
    return `${debtMonths} oylik qarz (shu guruh): ${formatMoney(balance)}`
  return balance < 0 ? `Qarz (shu guruh): ${formatMoney(balance)}` : "Shu guruh uchun to'langan"
}

/**
 * 1-5 ball uchun BUTUN TIZIMDA yagona rang shkalasi — faqat YASHIL (emerald), 1 uchun eng OCH,
 * 5 uchun eng TO'Q. Avval har bir sahifa o'zicha qizil/sariq/ko'k/yashil ("svetofor") mustaqil
 * nusxasini ishlatardi (masalan 1-2 baho qizil edi) — endi hammasi shu ikkita funksiyaga tayanadi.
 * Kasr qiymatlar (o'rtacha baho, masalan 4.3) eng yaqin butun ballga yaxlitlanadi.
 */
function gradeStep(grade: number): number {
  return Math.min(4, Math.max(0, Math.round(grade) - 1)) // 0..4 indeks (1-ball..5-ball)
}

/** Baho katakcha/belgisi uchun fon+matn klassi (masalan jurnal kataklari, baho chiplar). */
export function gradeBadgeCls(grade: number): string {
  const steps = [
    'bg-emerald-50 text-emerald-600',
    'bg-emerald-100 text-emerald-700',
    'bg-emerald-200 text-emerald-800',
    'bg-emerald-400 text-white',
    'bg-emerald-600 text-white',
  ]
  return steps[gradeStep(grade)]
}

/** Faqat matn rangi kerak bo'lgan joylar uchun (masalan jadvaldagi o'rtacha baho ustuni). */
export function gradeTextCls(grade: number): string {
  const steps = ['text-emerald-500', 'text-emerald-600', 'text-emerald-700', 'text-emerald-800', 'text-emerald-900']
  return steps[gradeStep(grade)]
}

/** `gradeTextCls` bilan bir xil shkala, lekin hex qiymat (o'quvchi portali — style/SVG uchun). */
export function gradeHex(grade: number): string {
  const steps = ['#10b981', '#059669', '#047857', '#065f46', '#064e3b']
  return steps[gradeStep(grade)]
}

export interface TelegramTargets {
  /** Native ilova deep-link (tg://) — Telegram ilovasini to'g'ridan-to'g'ri ochadi. */
  app: string
  /** Web havola (https://t.me/...) — brauzer/zaxira. */
  web: string
}

/**
 * Telegram kanal manzilidan (@username / username / t.me/... / +invite / to'liq URL)
 * native ilova (tg://) va web (https://t.me) havolalarini quradi.
 */
export function telegramTargets(raw: string): TelegramTargets {
  const s = (raw || '').trim()
  if (!s) return { app: '', web: '' }
  // Kanal "yo'lini" (username yoki +invite/joinchat) ajratamiz.
  let rest = s
  if (/^https?:\/\//i.test(s)) {
    try {
      rest = new URL(s).pathname.replace(/^\/+/, '')
    } catch {
      rest = ''
    }
  } else {
    rest = s.replace(/^t\.me\//i, '').replace(/^@/, '')
  }
  rest = rest.replace(/\/+$/, '')
  const web = rest ? `https://t.me/${rest}` : /^https?:\/\//i.test(s) ? s : ''
  let app = ''
  if (/^\+/.test(rest)) app = `tg://join?invite=${encodeURIComponent(rest.slice(1))}`
  else if (/^joinchat\//i.test(rest)) app = `tg://join?invite=${encodeURIComponent(rest.replace(/^joinchat\//i, ''))}`
  else if (rest) app = `tg://resolve?domain=${encodeURIComponent(rest.split('/')[0])}`
  return { app, web }
}

/** Telegram kanal manzilini to'liq web havolaga aylantiradi (eski API — zaxira). */
export function telegramUrl(raw: string): string {
  return telegramTargets(raw).web
}

/**
 * Bosilganda Telegram ilovasini kanalga ochadi (Flutter WebView ichida ham).
 * Tartib: (1) native ko'prik (window.openExternalUrl) bo'lsa — tashqi ilovada ochadi;
 * (2) aks holda tg:// deep-link bilan ilovani ochishga urinadi, ~1.2s ichida ochilmasa
 * (3) https://t.me web havolasiga o'tadi.
 */
export function openTelegram(raw: string): void {
  const { app, web } = telegramTargets(raw)
  if (!app && !web) return
  const target = app || web

  // (1) Native ko'prik — Flutter runJavaScript bilan window.openExternalUrl o'rnatsa,
  // url_launcher (externalApplication) orqali Telegram ilovasi ochiladi. Eng ishonchli.
  const hook = (window as unknown as { openExternalUrl?: (u: string) => void }).openExternalUrl
  if (typeof hook === 'function') {
    try {
      hook(target)
      return
    } catch {
      /* ko'prik ishlamadi — pastdagi usulga o'tamiz */
    }
  }

  // Ko'prik yo'q: deep-link bilan ilovani ochishga urinamiz, ochilmasa web havolaga zaxira.
  if (!app) {
    window.location.href = web
    return
  }
  let switched = false
  const onHide = () => {
    if (document.visibilityState === 'hidden') {
      switched = true
      cleanup()
    }
  }
  const cleanup = () => document.removeEventListener('visibilitychange', onHide)
  document.addEventListener('visibilitychange', onHide)
  window.setTimeout(() => {
    cleanup()
    if (!switched && web) window.location.href = web
  }, 1200)
  window.location.href = app
}

/** Tasodifiy parol (chalkashtirmaydigan belgilardan — 0/O, 1/l/I yo'q) */
export function randomPassword(length = 6): string {
  const alphabet = 'abcdefghijkmnpqrstuvwxyz23456789'
  const bytes = new Uint8Array(length)
  crypto.getRandomValues(bytes)
  return Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('')
}

/** Matnni clipboardga nusxalash (xatoni yutadi) */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
}
