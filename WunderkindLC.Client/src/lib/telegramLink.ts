import { api } from '@/api/client'

// «📱 Panel» Mini App → links the Telegram chat to the logged-in account (2026-10-12, owner: one bot for everyone,
// no phone sharing in the bot). The server checks Telegram's signature and links only the caller's own account(s)
// — `POST /api/telegram/link`, rules: `.claude/rules/messaging.md` §0.5.

const doneKey = (userId: string) => `tg-linked:${userId}`
const inFlight = new Set<string>()

/** Signed Mini App launch data; '' in a normal browser (the SDK loads there too, with empty initData). */
export function telegramInitData(): string {
  return window.Telegram?.WebApp?.initData ?? ''
}

type Post = (body: { initData: string }) => Promise<unknown>
const defaultPost: Post = (body) => api.post('/telegram/link', body)

/**
 * Once per session and account. A SECONDARY action (error-visibility.md §4): the panel works without it, the bot
 * simply does not know the chat yet — so a failure is swallowed and retried in the next session.
 */
export async function linkTelegramChat(userId: string, post: Post = defaultPost): Promise<void> {
  const initData = telegramInitData()
  if (!initData || !userId || inFlight.has(userId)) return
  try {
    if (sessionStorage.getItem(doneKey(userId))) return
  } catch {
    // storage blocked — just link (the server is idempotent)
  }
  inFlight.add(userId)
  try {
    await post({ initData })
    try {
      sessionStorage.setItem(doneKey(userId), '1')
    } catch {
      // storage blocked — the next mount links again (idempotent)
    }
  } catch {
    // secondary action — see above
  } finally {
    inFlight.delete(userId)
  }
}

/**
 * Opened from the bot's REPLY-KEYBOARD «📱 Panel» button: inside Telegram, but Telegram never gives initData there
 * (core.telegram.org/bots/webapps — the launch data "is empty if the Mini App was launched from a keyboard button"),
 * so the automatic link above cannot run. Such launches can `sendData` instead (2026-10-13).
 */
export function isKeyboardLaunch(): boolean {
  const w = window.Telegram?.WebApp
  return !!w && typeof w.sendData === 'function' && !w.initData && !!w.platform && w.platform !== 'unknown'
}

export async function getTelegramLinkStatus(): Promise<{ configured: boolean; linked: boolean }> {
  const { data } = await api.get<{ configured: boolean; linked: boolean }>('/telegram/link-status')
  return data
}

/** One-time token → `sendData` → the bot links THIS chat to the logged-in account. The Mini App closes. */
export async function sendBotLinkRequest(): Promise<void> {
  const { data } = await api.post<{ data: string }>('/telegram/link-token')
  window.Telegram?.WebApp?.sendData?.(data.data)
}
