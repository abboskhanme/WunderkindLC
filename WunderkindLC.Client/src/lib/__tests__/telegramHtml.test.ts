import { describe, expect, it } from 'vitest'
import { renderToStaticMarkup } from 'react-dom/server'
import { createElement } from 'react'
import { decodeEntities, isAllowedHref, parseTelegramHtml, renderTelegramHtml, telegramPlain } from '../telegramHtml'

/** Komponentni HTML satriga — React o'zi escape qiladi, ya'ni natijada xom teg bo'lsa XSS bo'lardi. */
const html = (src: string) => renderToStaticMarkup(createElement('div', null, renderTelegramHtml(src)))

describe('telegramHtml — oq ro\'yxat renderer', () => {
  it('ruxsat etilgan teglar elementga aylanadi', () => {
    const out = html('<b>Salom</b> <i>dunyo</i> <u>u</u> <s>s</s> <code>c</code>')
    expect(out).toContain('<b class="font-semibold">Salom</b>')
    expect(out).toContain('<i class="italic">dunyo</i>')
    expect(out).toContain('<u class="underline">u</u>')
    expect(out).toContain('<s class="line-through">s</s>')
    expect(out).toContain('<code')
  })

  it('strong/em sinonimlari ham ishlaydi', () => {
    const nodes = parseTelegramHtml('<strong>a</strong><em>b</em>')
    expect(nodes).toEqual([
      { type: 'el', tag: 'b', children: [{ type: 'text', text: 'a' }] },
      { type: 'el', tag: 'i', children: [{ type: 'text', text: 'b' }] },
    ])
  })

  it("script va boshqa teglar MATN bo'lib qoladi (XSS yo'q)", () => {
    const out = html('<script>alert(1)</script><img src=x onerror=alert(1)>')
    expect(out).not.toContain('<script>')
    expect(out).not.toContain('<img')
    expect(out).toContain('&lt;script&gt;')
  })

  it('escape qilingan qiymat matn sifatida ko\'rinadi (Ali &lt;b&gt;)', () => {
    expect(telegramPlain('Hurmatli Ali &lt;b&gt;')).toBe('Hurmatli Ali <b>')
    expect(html('Ali &lt;b&gt;')).not.toContain('<b>')
  })

  it('havola faqat http(s) va tg sxemasi bilan', () => {
    expect(html('<a href="https://wunderkind.uz">sayt</a>')).toContain('href="https://wunderkind.uz"')
    const bad = html('<a href="javascript:alert(1)">x</a>')
    // Haqiqiy <a> elementi YO'Q — teg oddiy (escape qilingan) matn bo'lib chiqadi.
    expect(bad).not.toMatch(/<a[\s>]/)
    expect(bad).not.toContain('href="javascript')
    expect(bad).toContain('&lt;a href=')
    expect(isAllowedHref('tg://resolve?domain=x')).toBe(true)
    expect(isAllowedHref('javascript:alert(1)')).toBe(false)
    expect(isAllowedHref('/relative')).toBe(false)
  })

  it('yopilmagan teg matn oxirida yopiladi, mos kelmagan yopilish matn', () => {
    expect(parseTelegramHtml('<b>qalin')).toEqual([{ type: 'el', tag: 'b', children: [{ type: 'text', text: 'qalin' }] }])
    expect(telegramPlain('matn</b>')).toBe('matn</b>')
  })

  it('entity ochiladi', () => {
    expect(decodeEntities('Q&amp;A &#39;x&#x27; &quot;')).toBe(`Q&A 'x' "`)
  })
})
