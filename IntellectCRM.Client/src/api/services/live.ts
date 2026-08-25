import * as signalR from '@microsoft/signalr'

export type LiveHandlers = Record<string, (...args: unknown[]) => void>

/**
 * Admin real-time hub'iga (/hubs/live) ulanib, berilgan mavzuga (topic) qo'shiladi.
 * `handlers` — hodisa nomi → callback (masalan { busLocation: fn } yoki { turnstileChanged: fn }).
 * Avtomatik qayta ulanadi va ulanish tiklanganda mavzuga qaytadan qo'shiladi.
 * Komponent unmount bo'lganda qaytgan ulanishni `.stop()` qiling.
 */
export async function connectLiveTopic(topic: string, handlers: LiveHandlers): Promise<signalR.HubConnection> {
  // Auth `at` cookie'si WS handshake'da avtomatik ketadi (backend OnMessageReceived cookie'ni o'qiydi).
  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/live', { withCredentials: true })
    .withAutomaticReconnect()
    .build()

  for (const [event, fn] of Object.entries(handlers)) conn.on(event, fn)
  conn.onreconnected(() => {
    conn.invoke('Join', topic).catch(() => {})
  })

  await conn.start()
  await conn.invoke('Join', topic)
  return conn
}
