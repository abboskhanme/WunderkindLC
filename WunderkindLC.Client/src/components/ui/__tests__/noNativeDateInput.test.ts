import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Sana HAR DOIM kun.oy.yil (2026-10-13, foydalanuvchi: "oy birinchi bo'lib qolgan"). Brauzerning `<input type="date">`
 * i sanani brauzer tilida ko'rsatadi (inglizcha Chrome'da "10/15/2026") — shuning uchun to'g'ridan-to'g'ri ISHLATILMAYDI:
 * `DateInput` yoki `type="date"` ni o'zi DateInput'ga yo'naltiradigan umumiy maydonlar (Input, DrawerInput, FilterInput,
 * TextField). Yangi `<input type="date">` yozilsa — shu test qizaradi.
 */
const SRC = join(__dirname, '..', '..', '..')

function tsxFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) return name === 'node_modules' ? [] : tsxFiles(p)
    return p.endsWith('.tsx') ? [p] : []
  })
}

describe('brauzerning sana maydoni to\'g\'ridan-to\'g\'ri ishlatilmaydi', () => {
  it('<input type="date"> faqat DateInput ichida', () => {
    const offenders: string[] = []
    for (const file of tsxFiles(SRC)) {
      const rel = relative(SRC, file)
      if (rel === join('components', 'ui', 'DateInput.tsx')) continue
      const src = readFileSync(file, 'utf8')
      // Element = `<input` … `/>` (attributes may contain "=>" — a `[^>]*` scan would stop inside onChange).
      for (const m of src.matchAll(/<input\b[\s\S]*?\/>/g))
        if (/\btype="date"/.test(m[0])) offenders.push(`${rel}: ${m[0].replace(/\s+/g, ' ').slice(0, 80)}`)
    }
    expect(offenders).toEqual([])
  })
})
