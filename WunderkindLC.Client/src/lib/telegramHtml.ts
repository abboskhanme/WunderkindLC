import { createElement, Fragment, type ReactNode } from 'react'

/**
 * TELEGRAM HTML → React (CRM upgrade P2.8). Server preview (`POST messaging/preview`) va saqlangan
 * xabar matni Telegram HTML'da keladi; uni ekranda chizish uchun FAQAT ruxsat etilgan teglar
 * (`b i u s code pre a`) React elementiga aylanadi, qolgan hammasi ODDIY MATN bo'lib qoladi.
 *
 * ⚠️ HECH QACHON `dangerouslySetInnerHTML` emas: matnda o'quvchi ismi, izoh, admin yozgan har narsa
 * bor — XSS bo'lmasin. Havola faqat `http(s)://` va `tg://` (server qoidasi bilan bir xil,
 * `TelegramHtml.IsAllowedUrl`); boshqa sxema (`javascript:`) — matn.
 */

export type TgTag = 'b' | 'i' | 'u' | 's' | 'code' | 'pre' | 'a'

export type TgNode =
  | { type: 'text'; text: string }
  | { type: 'el'; tag: TgTag; href?: string; children: TgNode[] }

const TAGS: Record<string, TgTag> = {
  b: 'b',
  strong: 'b',
  i: 'i',
  em: 'i',
  u: 'u',
  ins: 'u',
  s: 's',
  strike: 's',
  del: 's',
  code: 'code',
  pre: 'pre',
  a: 'a',
}

/** Ruxsat etilgan havola sxemasi. */
export function isAllowedHref(href: string | null | undefined): boolean {
  const v = (href ?? '').trim()
  return /^https?:\/\/[^\s/]+/i.test(v) || /^tg:\/\/\S+/i.test(v)
}

/** HTML entity'larni ochadi (`&lt; &gt; &amp; &quot; &#NNN; &#xHH;`). */
export function decodeEntities(s: string): string {
  return s.replace(/&(lt|gt|amp|quot|apos|#\d{1,7}|#x[0-9a-f]{1,6});/gi, (m, e: string) => {
    const k = e.toLowerCase()
    if (k === 'lt') return '<'
    if (k === 'gt') return '>'
    if (k === 'amp') return '&'
    if (k === 'quot') return '"'
    if (k === 'apos') return "'"
    const code = k.startsWith('#x') ? parseInt(k.slice(2), 16) : parseInt(k.slice(1), 10)
    try {
      return Number.isFinite(code) ? String.fromCodePoint(code) : m
    } catch {
      return m
    }
  })
}

const TAG_RX = /<(\/?)([a-zA-Z][a-zA-Z0-9]*)((?:\s[^<>]*)?)\/?>/g
const HREF_RX = /href\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s"'>]+))/i

/** Telegram HTML → daraxt (sof funksiya). Yopilmagan teg matn oxirida yopiladi. */
export function parseTelegramHtml(html: string | null | undefined): TgNode[] {
  const src = html ?? ''
  const root: { children: TgNode[] } = { children: [] }
  const stack: { tag: TgTag | null; node: { children: TgNode[] } }[] = [{ tag: null, node: root }]
  const top = () => stack[stack.length - 1].node.children
  const pushText = (raw: string) => {
    if (!raw) return
    const text = decodeEntities(raw)
    const list = top()
    const last = list[list.length - 1]
    if (last && last.type === 'text') last.text += text
    else list.push({ type: 'text', text })
  }

  let pos = 0
  TAG_RX.lastIndex = 0
  for (let m = TAG_RX.exec(src); m; m = TAG_RX.exec(src)) {
    pushText(src.slice(pos, m.index))
    pos = m.index + m[0].length
    const closing = m[1] === '/'
    const tag = TAGS[m[2].toLowerCase()]
    if (!tag) {
      pushText(m[0])
      continue
    }
    if (closing) {
      // Faqat ochiq teg bilan mos yopilish; mos kelmasa — matn.
      const idx = [...stack].reverse().findIndex((f) => f.tag === tag)
      if (idx < 0) {
        pushText(m[0])
        continue
      }
      stack.length = stack.length - 1 - idx
      continue
    }
    if (tag === 'a') {
      const hm = HREF_RX.exec(m[3] ?? '')
      const href = decodeEntities((hm?.[1] ?? hm?.[2] ?? hm?.[3] ?? '').trim())
      if (!isAllowedHref(href)) {
        pushText(m[0])
        continue
      }
      const el: TgNode = { type: 'el', tag, href, children: [] }
      top().push(el)
      stack.push({ tag, node: el })
      continue
    }
    const el: TgNode = { type: 'el', tag, children: [] }
    top().push(el)
    stack.push({ tag, node: el })
  }
  pushText(src.slice(pos))
  return root.children
}

/** Faqat matn (teglarsiz) — qisqa ko'rinishlar uchun. */
export function telegramPlain(html: string | null | undefined): string {
  const walk = (nodes: TgNode[]): string =>
    nodes.map((n) => (n.type === 'text' ? n.text : walk(n.children))).join('')
  return walk(parseTelegramHtml(html))
}

const CLASS: Record<TgTag, string | undefined> = {
  b: 'font-semibold',
  i: 'italic',
  u: 'underline',
  s: 'line-through',
  code: 'rounded bg-black/5 px-1 font-mono text-[0.92em]',
  pre: 'my-1 overflow-x-auto rounded bg-black/5 p-2 font-mono text-[0.92em]',
  a: 'text-sky-600 underline',
}

function toReact(nodes: TgNode[], keyPrefix: string): ReactNode[] {
  return nodes.map((n, i) => {
    const key = `${keyPrefix}${i}`
    if (n.type === 'text') return createElement(Fragment, { key }, n.text)
    const children = toReact(n.children, `${key}.`)
    if (n.tag === 'a')
      return createElement('a', { key, href: n.href, target: '_blank', rel: 'noopener noreferrer', className: CLASS.a }, ...children)
    return createElement(n.tag, { key, className: CLASS[n.tag] }, ...children)
  })
}

/** Telegram HTML → React elementlari (oq ro'yxat; konteynerga `whitespace-pre-wrap` bering). */
export function renderTelegramHtml(html: string | null | undefined): ReactNode {
  return createElement(Fragment, null, ...toReact(parseTelegramHtml(html), 'tg'))
}
