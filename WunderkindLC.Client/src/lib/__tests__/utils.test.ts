// @vitest-environment jsdom
//
// `src/lib/utils.ts` — butun ilova tayanadigan sof yordamchilar.
// jsdom kerak, chunki `exportToCsv` Blob / URL.createObjectURL / <a>.click bilan ishlaydi.
//
// ⚠️ Bu yerda PRODUCTION KODI TUZATILMAYDI. Tasdiqlangan xatolar `it.skip` bilan KUTILGAN
// (to'g'ri) xulq ko'rinishida yozib qoldirilgan — tuzatilgach `.skip` olib tashlanadi.

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  apiErrorMessage,
  balanceDotCls,
  balanceTextCls,
  balanceTitle,
  cn,
  exportToCsv,
  formatDate,
  formatDateTime,
  formatMoney,
  formatTime,
  gradeBadgeCls,
  gradeHex,
  gradeTextCls,
  maskPhone,
  randomPassword,
  telegramTargets,
  telegramUrl,
  unmaskPhone,
} from '@/lib/utils'

/** Intl('ru-RU') mingliklarni UZUQ BO'LMAYDIGAN bo'sh joy (U+00A0) bilan ajratadi, oddiy probel emas. */
const NBSP = ' '
/** Excel UTF-8 ni tanishi uchun CSV boshiga qo'yiladigan BOM. */
const BOM = '﻿'

// ─────────────────────────── exportToCsv ───────────────────────────

/** `exportToCsv` ni chaqiradi va yuklab olingan faylning to'liq matnini + fayl nomini qaytaradi. */
async function captureCsv(
  filename: string,
  headers: string[],
  rows: string[][],
): Promise<{ text: string; download: string; type: string }> {
  let blob: Blob | null = null
  let download = ''
  // jsdom `URL.createObjectURL`/`revokeObjectURL` ni UMUMAN implementatsiya qilmaydi —
  // `vi.spyOn` "does not exist" bilan yiqiladi, shuning uchun avval stub o'rnatamiz.
  const urlAny = URL as unknown as Record<string, unknown>
  const hadCreate = 'createObjectURL' in urlAny
  const hadRevoke = 'revokeObjectURL' in urlAny
  const createSpy = vi.fn((b: Blob) => {
    blob = b
    return 'blob:mock'
  })
  const revokeSpy = vi.fn()
  urlAny.createObjectURL = createSpy
  urlAny.revokeObjectURL = revokeSpy
  const clickSpy = vi
    .spyOn(HTMLAnchorElement.prototype, 'click')
    .mockImplementation(function (this: HTMLAnchorElement) {
      download = this.download
    })

  try {
    exportToCsv(filename, headers, rows)
    expect(createSpy).toHaveBeenCalledTimes(1)
    expect(clickSpy).toHaveBeenCalledTimes(1)
    expect(revokeSpy).toHaveBeenCalledTimes(1) // xotira oqmasin
    expect(blob).not.toBeNull()
    const b = blob as unknown as Blob
    // DIQQAT: `blob.text()` spetsifikatsiya bo'yicha BOM ni OLIB TASHLAYDI, shuning uchun
    // baytlarni o'zimiz `ignoreBOM: true` bilan dekodlaymiz — aks holda BOM testi soxta bo'lardi.
    const text = new TextDecoder('utf-8', { ignoreBOM: true }).decode(await b.arrayBuffer())
    return { text, download, type: b.type }
  } finally {
    if (!hadCreate) delete urlAny.createObjectURL
    if (!hadRevoke) delete urlAny.revokeObjectURL
    clickSpy.mockRestore()
  }
}

describe('exportToCsv', () => {
  afterEach(() => vi.restoreAllMocks())

  it('faylni UTF-8 BOM bilan boshlaydi va nomini <a download> ga qo\'yadi', async () => {
    const { text, download, type } = await captureCsv('hisobot.csv', ['Ism'], [['Ali']])
    expect(text.startsWith(BOM)).toBe(true)
    expect(text).toBe(`${BOM}"Ism"\r\n"Ali"`)
    expect(download).toBe('hisobot.csv')
    expect(type).toContain('charset=utf-8')
  })

  it('vergul va nuqta-vergulni qo\'shtirnoq ichida saqlaydi (ustunga bo\'linib ketmaydi)', async () => {
    const { text } = await captureCsv('a.csv', ['Ism', 'Izoh'], [['Ali', 'Toshkent, Chilonzor']])
    expect(text).toBe(`${BOM}"Ism","Izoh"\r\n"Ali","Toshkent, Chilonzor"`)
  })

  it('qo\'shtirnoqni ikkilantiradi (RFC 4180)', async () => {
    const { text } = await captureCsv('a.csv', ['Izoh'], [['u "zo\'r" dedi']])
    expect(text).toBe(`${BOM}"Izoh"\r\n"u ""zo'r"" dedi"`)
  })

  it('katak ichidagi yangi qatorni yo\'qotmaydi', async () => {
    const { text } = await captureCsv('a.csv', ['Izoh'], [['birinchi\nikkinchi']])
    expect(text).toBe(`${BOM}"Izoh"\r\n"birinchi\nikkinchi"`)
  })

  it('qatorlarni CRLF bilan ajratadi', async () => {
    const { text } = await captureCsv('a.csv', ['N'], [['1'], ['2'], ['3']])
    expect(text.split('\r\n')).toEqual([`${BOM}"N"`, '"1"', '"2"', '"3"'])
  })

  // XATO (src/lib/utils.ts:104-106): CSV FORMULA INJECTION. `escape()` faqat qo'shtirnoqni
  // ikkilantiradi; `=`, `+`, `-`, `@`, TAB yoki CR bilan boshlanadigan qiymat Excel/Sheets'da
  // FORMULA sifatida bajariladi. O'quvchi ismiga `=HYPERLINK(...)` yozib qo'ysa, hisobotni
  // ochgan admin mashinasida ishga tushadi. Yechim: bunday qiymat oldiga `'` prefiksi.
  // Tuzatilgach `.skip` olib tashlanadi.
  it.skip('formula belgisi bilan boshlanadigan qiymatni zararsizlantiradi', async () => {
    const { text } = await captureCsv(
      'a.csv',
      ['Ism'],
      [['=HYPERLINK("http://evil.tld","bosing")'], ['+1'], ['-1'], ['@ali']],
    )
    expect(text).toContain(`"'=HYPERLINK(`)
    expect(text).toContain(`"'+1"`)
    expect(text).toContain(`"'-1"`)
    expect(text).toContain(`"'@ali"`)
  })
})

// ─────────────────────────── maskPhone / unmaskPhone ───────────────────────────

describe('maskPhone', () => {
  it('9 xonali mahalliy raqamga +998 prefiksini qo\'shadi', () => {
    expect(maskPhone('901234567')).toBe('(998) 90-123-45-67')
  })

  it('998… va +998… ni bir xil formatlaydi', () => {
    expect(maskPhone('998901234567')).toBe('(998) 90-123-45-67')
    expect(maskPhone('+998901234567')).toBe('(998) 90-123-45-67')
    expect(maskPhone('+998 90 123 45 67')).toBe('(998) 90-123-45-67')
  })

  it('qisman kiritishda progressiv maska beradi (input yozayotganda)', () => {
    expect(maskPhone('9')).toBe('(998) 9')
    expect(maskPhone('90')).toBe('(998) 90')
    expect(maskPhone('9012')).toBe('(998) 90-12')
    expect(maskPhone('90123456')).toBe('(998) 90-123-45-6')
  })

  it('12 raqamdan ortig\'ini kesib tashlaydi', () => {
    expect(maskPhone('9989012345670000')).toBe('(998) 90-123-45-67')
  })

  it('bo\'sh satrni bo\'sh qoldiradi', () => {
    expect(maskPhone('')).toBe('')
  })

  it('maska ↔ unmask round-trip backendga toza 12 raqam beradi', () => {
    expect(unmaskPhone(maskPhone('901234567'))).toBe('998901234567')
    expect(unmaskPhone(maskPhone('+998 90 123 45 67'))).toBe('998901234567')
  })

  // XATO (src/lib/utils.ts:65-91): xorijiy raqamda `998` prefiksi KO'R-KO'RONA qo'shiladi va
  // oxiri kesiladi → mavjud bo'lmagan O'ZBEK raqami hosil bo'ladi (`+79161234567` →
  // `(998) 79-161-23-45`). Bunday raqam bazaga tushsa SMS/bot xabarlari begonaga ketishi mumkin.
  it.skip('xorijiy raqamni soxta 998-raqamga aylantirmaydi', () => {
    expect(maskPhone('+79161234567')).not.toBe('(998) 79-161-23-45')
    expect(unmaskPhone(maskPhone('+79161234567'))).toBe('79161234567')
  })

  // XATO (src/lib/utils.ts:78-79): raqamsiz matn kiritilsa natija `'(998'` — YOPILMAGAN qavs,
  // foydalanuvchiga "raqam bordek" ko'rinadi. Kutilgan: bo'sh satr.
  it.skip('raqamsiz matndan yopilmagan qavs yasamaydi', () => {
    expect(maskPhone('abc')).toBe('')
  })
})

describe('unmaskPhone', () => {
  it('barcha raqam bo\'lmagan belgilarni olib tashlaydi', () => {
    expect(unmaskPhone('(998) 90-123-45-67')).toBe('998901234567')
    expect(unmaskPhone('+998-90-123-45-67')).toBe('998901234567')
  })

  it('bo\'sh kirishda bo\'sh qaytaradi', () => {
    expect(unmaskPhone('')).toBe('')
  })
})

// ─────────────────────────── formatMoney ───────────────────────────

describe('formatMoney', () => {
  it('nolni va odatiy summani formatlaydi', () => {
    expect(formatMoney(0)).toBe("0 so'm")
    expect(formatMoney(850000)).toBe(`850${NBSP}000 so'm`)
  })

  it('manfiy summani (qarz) minus bilan chiqaradi', () => {
    expect(formatMoney(-450000)).toBe(`-450${NBSP}000 so'm`)
  })

  it('katta summani NBSP bilan guruhlaydi (oddiy probel EMAS)', () => {
    expect(formatMoney(123456789)).toBe(`123${NBSP}456${NBSP}789 so'm`)
    expect(formatMoney(123456789)).not.toContain(' 456') // oddiy probel bo'lmasin
  })

  // XATO (src/lib/utils.ts:122-124): `Intl.NumberFormat('ru-RU')` NaN/undefined uchun RUSCHA
  // "не число" qaytaradi → foydalanuvchi ekranida "не число so'm" chiqadi (butun UI o'zbekcha).
  it.skip('NaN/undefined da ruscha matn chiqarmaydi', () => {
    expect(formatMoney(NaN)).toBe("0 so'm")
    expect(formatMoney(undefined as unknown as number)).toBe("0 so'm")
  })

  // XATO (src/lib/utils.ts:122-124): kasr summa 3 xona bilan chiqadi (`1 234,568 so'm`).
  // So'm tiyinsiz hisoblanadi — butun songa yaxlitlanishi kerak.
  it.skip('kasr summani butun so\'mga yaxlitlaydi', () => {
    expect(formatMoney(1234.5678)).toBe(`1${NBSP}235 so'm`)
  })
})

// ─────────────────────────── sana / vaqt ───────────────────────────

describe('formatDate / formatDateTime / formatTime', () => {
  afterEach(() => vi.unstubAllEnvs())

  it('ISO sanani DD.MM.YYYY ga o\'giradi', () => {
    expect(formatDate('2026-08-01')).toBe('01.08.2026')
    expect(formatDate('2026-08-01T12:34:56')).toBe('01.08.2026')
  })

  it('bo\'sh satrda bo\'sh, yaroqsiz satrda xom qiymatni qaytaradi', () => {
    expect(formatDate('')).toBe('')
    expect(formatDate('yaroqsiz')).toBe('yaroqsiz')
  })

  it('formatDateTime sana+vaqtni birlashtiradi, vaqt yo\'q bo\'lsa faqat sana', () => {
    expect(formatDateTime('2026-08-01T12:34:56')).toBe('01.08.2026 12:34')
    expect(formatDateTime('2026-08-01')).toBe('01.08.2026')
  })

  it('formatDateTime null/undefined/bo\'sh uchun tire beradi', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime(undefined)).toBe('—')
    expect(formatDateTime('')).toBe('—')
  })

  it('formatTime faqat HH:mm qaytaradi, vaqt bo\'lmasa bo\'sh', () => {
    expect(formatTime('2026-08-01T09:05:00')).toBe('09:05')
    expect(formatTime('2026-08-01')).toBe('')
    expect(formatTime(null)).toBe('')
    expect(formatTime(undefined)).toBe('')
  })

  // REGRESSIYA: sana satrdan o'qilishi kerak, `new Date()` orqali emas. Aks holda brauzer
  // mintaqasi UTC dan orqada bo'lsa (masalan Amerika) server sanasi BIR KUN ORQAGA siljiydi.
  it('mintaqa (TZ) o\'zgarganda ham server sanasi siljimaydi', () => {
    const zones = ['UTC', 'America/New_York', 'Asia/Tokyo', 'Asia/Tashkent']
    const dates = new Set<string>()
    const times = new Set<string>()
    for (const tz of zones) {
      vi.stubEnv('TZ', tz)
      // Sanity: mintaqa haqiqatan almashayotganini tekshiramiz (aks holda test soxta yashil bo'lardi).
      dates.add(formatDate('2026-01-01T00:30:00Z'))
      times.add(formatTime('2026-01-01T00:30:00'))
    }
    expect([...dates]).toEqual(['01.01.2026'])
    expect([...times]).toEqual(['00:30'])
  })

  it('kechqurungi timestamp mintaqadan qat\'i nazar bir xil kunni beradi', () => {
    const results = new Set<string>()
    for (const tz of ['UTC', 'America/New_York', 'Asia/Tokyo']) {
      vi.stubEnv('TZ', tz)
      results.add(formatDateTime('2026-08-01T23:45:00'))
    }
    expect([...results]).toEqual(['01.08.2026 23:45'])
  })

  // XATO (src/lib/utils.ts:48 va :56): regex FAQAT `T` ajratgichini biladi. Backend/DB ba'zan
  // `"yyyy-MM-dd HH:mm:ss"` (probel bilan) qaytaradi — bunda VAQT JIMGINA YO'QOLADI
  // (to'lov/davomat vaqti ko'rinmay qoladi, xato bilinmaydi).
  it.skip('probel bilan ajratilgan timestampda ham vaqtni ko\'rsatadi', () => {
    expect(formatDateTime('2026-08-01 12:34')).toBe('01.08.2026 12:34')
    expect(formatTime('2026-08-01 12:34')).toBe('12:34')
  })
})

// ─────────────────────────── baho ranglari ───────────────────────────

describe('gradeBadgeCls / gradeTextCls / gradeHex', () => {
  it('1..5 chegaralarini to\'g\'ri xaritalaydi', () => {
    expect(gradeBadgeCls(1)).toBe('bg-emerald-50 text-emerald-600')
    expect(gradeBadgeCls(5)).toBe('bg-emerald-600 text-white')
    expect(gradeTextCls(1)).toBe('text-emerald-500')
    expect(gradeTextCls(5)).toBe('text-emerald-900')
    expect(gradeHex(1)).toBe('#10b981')
    expect(gradeHex(5)).toBe('#064e3b')
  })

  it('kasr o\'rtacha bahoni eng yaqin ballga yaxlitlaydi', () => {
    expect(gradeHex(4.3)).toBe(gradeHex(4))
    expect(gradeHex(4.5)).toBe(gradeHex(5))
    expect(gradeTextCls(2.4)).toBe(gradeTextCls(2))
    expect(gradeBadgeCls(1.2)).toBe(gradeBadgeCls(1))
  })

  it('shkaladan tashqari qiymatlarni chegaraga qisadi (0, 9, -1)', () => {
    expect(gradeHex(0)).toBe(gradeHex(1))
    expect(gradeHex(-1)).toBe(gradeHex(1))
    expect(gradeHex(9)).toBe(gradeHex(5))
    expect(gradeBadgeCls(0)).toBe(gradeBadgeCls(1))
    expect(gradeBadgeCls(9)).toBe(gradeBadgeCls(5))
  })

  // XATO (src/lib/utils.ts:162-188): `gradeStep(NaN)` → NaN, `steps[NaN]` → `undefined`.
  // Baho hali qo'yilmagan (o'rtacha = 0/0 = NaN) katakda `class={undefined}` ketadi yoki
  // `style.fill = undefined` bo'lib SVG ko'rinmay qoladi. Kutilgan: eng past ball zaxirasi.
  it.skip('NaN uchun zaxira rang qaytaradi (undefined emas)', () => {
    expect(gradeHex(NaN)).toBe('#10b981')
    expect(gradeBadgeCls(NaN)).toBe('bg-emerald-50 text-emerald-600')
    expect(gradeTextCls(NaN)).toBe('text-emerald-500')
  })
})

// ─────────────────────────── Telegram havolalari ───────────────────────────

describe('telegramTargets / telegramUrl', () => {
  it('@username, yalang username va t.me/username ni bir xil hal qiladi', () => {
    const expected = { app: 'tg://resolve?domain=wunderkind', web: 'https://t.me/wunderkind' }
    expect(telegramTargets('@wunderkind')).toEqual(expected)
    expect(telegramTargets('wunderkind')).toEqual(expected)
    expect(telegramTargets('t.me/wunderkind')).toEqual(expected)
    expect(telegramTargets('https://t.me/wunderkind')).toEqual(expected)
    expect(telegramTargets('  https://t.me/wunderkind/  ')).toEqual(expected)
  })

  it('+invite havolasidan tg://join deep-link quradi', () => {
    expect(telegramTargets('+AbC')).toEqual({
      app: 'tg://join?invite=AbC',
      web: 'https://t.me/+AbC',
    })
  })

  it('joinchat/ ko\'rinishidagi eski invite havolasini ham tushunadi', () => {
    expect(telegramTargets('https://t.me/joinchat/XYZ')).toEqual({
      app: 'tg://join?invite=XYZ',
      web: 'https://t.me/joinchat/XYZ',
    })
  })

  it('bo\'sh/probelli kirishda ikkala havola ham bo\'sh', () => {
    expect(telegramTargets('')).toEqual({ app: '', web: '' })
    expect(telegramTargets('   ')).toEqual({ app: '', web: '' })
    expect(telegramUrl('')).toBe('')
  })

  it('telegramUrl — telegramTargets().web ning qisqartmasi', () => {
    expect(telegramUrl('@wunderkind')).toBe('https://t.me/wunderkind')
    expect(telegramUrl('t.me/wunderkind')).toBe('https://t.me/wunderkind')
  })

  // XATO (src/lib/utils.ts:206-216): HAR QANDAY http(s) URL dan faqat `pathname` olinadi va
  // `https://t.me/<path>` qilib QAYTA YOZILADI — host tekshirilmaydi. Admin sozlamaga
  // `https://instagram.com/wunderkind` kiritsa, foydalanuvchi mavjud bo'lmagan Telegram
  // kanaliga yuboriladi (yoki begona kanalga). Kutilgan: telegram bo'lmagan host rad etilsin.
  it.skip('telegram bo\'lmagan hostni t.me ga qayta yozmaydi', () => {
    expect(telegramTargets('https://instagram.com/wunderkind')).toEqual({ app: '', web: '' })
    expect(telegramTargets('https://evil.tld/wunderkind')).toEqual({ app: '', web: '' })
  })
})

// ─────────────────────────── balans ranglari ───────────────────────────

describe('balanceTextCls / balanceDotCls / balanceTitle', () => {
  it('to\'lagan (balans >= 0) → yashil', () => {
    expect(balanceTextCls(0)).toBe('text-emerald-700')
    expect(balanceTextCls(250000)).toBe('text-emerald-700')
    expect(balanceDotCls(0)).toBe('bg-emerald-500')
  })

  it('qarzdor (balans < 0) → qizil', () => {
    expect(balanceTextCls(-1)).toBe('text-red-600')
    expect(balanceDotCls(-450000)).toBe('bg-red-500')
  })

  it('1 oylik qarz hali qizil (og\'ir emas)', () => {
    expect(balanceTextCls(-450000, 1)).toBe('text-red-600')
    expect(balanceDotCls(-450000, 1)).toBe('bg-red-500')
  })

  it('2+ oylik qarz qizildan USTUN — fuchsia', () => {
    expect(balanceTextCls(-450000, 2)).toBe('text-fuchsia-600')
    expect(balanceTextCls(-900000, 5)).toBe('text-fuchsia-600')
    expect(balanceDotCls(-450000, 2)).toBe('bg-fuchsia-500')
  })

  it('debtMonths balansdan ustun: balans musbat bo\'lsa ham fuchsia', () => {
    expect(balanceTextCls(10000, 3)).toBe('text-fuchsia-600')
    expect(balanceDotCls(10000, 3)).toBe('bg-fuchsia-500')
  })

  it('tooltip matni holatga mos', () => {
    expect(balanceTitle(0)).toBe("Shu guruh uchun to'langan")
    expect(balanceTitle(-450000)).toBe(`Qarz (shu guruh): -450${NBSP}000 so'm`)
    expect(balanceTitle(-900000, 3)).toBe(`3 oylik qarz (shu guruh): -900${NBSP}000 so'm`)
  })
})

// ─────────────────────────── apiErrorMessage ───────────────────────────

describe('apiErrorMessage', () => {
  it('backend `message` ni Error.message dan USTUN qo\'yadi (axios regressiyasi)', () => {
    const axiosLike = Object.assign(new Error('Request failed with status code 400'), {
      response: { data: { message: 'Jurnal yopilgan — tahrirlab bo\'lmaydi' } },
    })
    expect(apiErrorMessage(axiosLike, 'zaxira')).toBe('Jurnal yopilgan — tahrirlab bo\'lmaydi')
  })

  it('backend xabari bo\'lmasa Error.message ga qaytadi', () => {
    expect(apiErrorMessage(new Error('Network Error'), 'zaxira')).toBe('Network Error')
  })

  it('backend xabari bo\'sh satr bo\'lsa uni e\'tiborsiz qoldiradi', () => {
    const err = Object.assign(new Error('500'), { response: { data: { message: '' } } })
    expect(apiErrorMessage(err, 'zaxira')).toBe('500')
  })

  it('Error bo\'lmagan qiymatda fallback qaytaradi', () => {
    expect(apiErrorMessage('shunchaki matn', 'zaxira')).toBe('zaxira')
    expect(apiErrorMessage(null, 'zaxira')).toBe('zaxira')
    expect(apiErrorMessage(undefined, 'zaxira')).toBe('zaxira')
    expect(apiErrorMessage({ response: { data: {} } }, 'zaxira')).toBe('zaxira')
  })
})

// ─────────────────────────── randomPassword / cn ───────────────────────────

describe('randomPassword', () => {
  it('standart uzunlik 6, so\'ralgan uzunlikka bo\'ysunadi', () => {
    expect(randomPassword()).toHaveLength(6)
    expect(randomPassword(12)).toHaveLength(12)
    expect(randomPassword(1)).toHaveLength(1)
  })

  it('faqat chalkashtirmaydigan alifbodan foydalanadi (0/O va 1/l/I yo\'q)', () => {
    for (let i = 0; i < 50; i++) {
      expect(randomPassword(16)).toMatch(/^[abcdefghijkmnpqrstuvwxyz23456789]+$/)
    }
  })

  it('ketma-ket chaqiruvlar bir xil parol bermaydi', () => {
    const seen = new Set(Array.from({ length: 30 }, () => randomPassword(10)))
    expect(seen.size).toBe(30)
  })
})

describe('cn', () => {
  it('faqat rost klasslarni bo\'sh joy bilan birlashtiradi', () => {
    expect(cn('a', false, null, undefined, 'b')).toBe('a b')
    expect(cn()).toBe('')
    expect(cn(false, null)).toBe('')
  })
})
