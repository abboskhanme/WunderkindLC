import { api, USE_MOCK } from '../client'

export interface BotThread {
  chatId: number
  name: string
  username: string
  phone: string
  linked: string
  startedAt: string
  lastMessageAt: string | null
  lastText: string
  unread: number
}

export interface BotSupportMsg {
  id: string
  fromUser: boolean
  text: string
  adminName: string
  createdAt: string
}

export async function getBotThreads(): Promise<BotThread[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<BotThread[]>('/admin/messages/support/threads')
  return data
}

export async function getBotMessages(chatId: number): Promise<BotSupportMsg[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<BotSupportMsg[]>(`/admin/messages/support/threads/${chatId}/messages`)
  return data
}

export async function replyBotThread(chatId: number, text: string): Promise<BotSupportMsg> {
  const { data } = await api.post<BotSupportMsg>(`/admin/messages/support/threads/${chatId}/reply`, { text })
  return data
}

export async function getBotSupportUnread(): Promise<number> {
  if (USE_MOCK) return 0
  const { data } = await api.get<{ count: number }>('/admin/messages/support/unread')
  return data.count
}
