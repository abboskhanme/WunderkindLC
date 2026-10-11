/**
 * AI ULANISH (read-only MCP) — sahifalar umumiy ishlatadigan sof yordamchilar
 * (`McpSettings` — superadmin, `MyMcpConnections` — har kim o'zinikini).
 */

/** Server UTC vaqtini markaz vaqtida (Asia/Tashkent) "DD.MM.YYYY HH:mm" ko'rinishida — brauzer mintaqasidan qat'i nazar. */
export function mcpTime(iso: string): string {
  const d = new Date(/[zZ]|[+-]\d{2}:\d{2}$/.test(iso) ? iso : iso + 'Z')
  if (Number.isNaN(d.getTime())) return iso
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Asia/Tashkent', day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
    }).formatToParts(d).map((p) => [p.type, p.value]),
  )
  return `${parts.day}.${parts.month}.${parts.year} ${parts.hour}:${parts.minute}`
}

export type McpConnectionState = 'active' | 'revoked' | 'expired'

/** Ulanish holati: bekor qilingan > faol > muddati o'tgan (yoki ruxsati olingan). */
export function mcpConnectionState(c: { active: boolean; revokedAt: string | null }): McpConnectionState {
  if (c.revokedAt) return 'revoked'
  return c.active ? 'active' : 'expired'
}

/** Faol (bekor qilinmagan) ulanishlar soni — «Hammasini bekor qilish» tugmasi shunga qarab chiqadi. */
export function liveMcpConnections(list: { revokedAt: string | null }[]): number {
  return list.filter((c) => !c.revokedAt).length
}
