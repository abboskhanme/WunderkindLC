import { api } from '../client'

/**
 * AI ULANISH (read-only MCP) — Sozlamalar → «AI ulanishlar» (faqat superadmin, `McpAdminController`,
 * `api/admin/mcp`) va «Mening AI ulanishlarim» (ulana oladigan har bir foydalanuvchi o'zinikini,
 * `McpMyConnectionsController`, `api/mcp/my`). Batafsil `docs/SPEC-mcp.md`.
 */

export interface McpStatus {
  enabled: boolean
  disabledReason: string | null
  /** AI ilovasiga qo'yiladigan MCP manzili (…/mcp). */
  endpoint: string
  toolCount: number
}

export interface McpConnection {
  id: string
  clientId: string
  clientName: string
  userId: string
  userName: string
  userRole: string | null
  /** Foydalanuvchida hozir ham AI ulanish ruxsati bormi. */
  userAllowed: boolean
  createdAt: string
  lastUsedAt: string | null
  revokedAt: string | null
  revokedBy: string | null
  active: boolean
  calls: number
  /** Kalit qaysi manzilga yuboriladi (claude.ai, chatgpt.com, kompyuterdagi dastur…). */
  redirectHosts: string[]
  /** Oxirgi 24 soatdagi so'rovlar va o'qilgan qatorlar. */
  calls24h: number
  rows24h: number
  /** Hajm chegarasidan oshgan (hozir yoki oxirgi 24 soatda ogohlantirilgan). */
  volumeAlert: boolean
  volumeAlertAt: string | null
}

/** Administrator akkaunti va uning AI ulanish kaliti (faqat superadmin yoqadi). */
export interface McpAdminAccess {
  id: string
  fullName: string
  phone: string
  enabled: boolean
  activeConnections: number
}

/** Foydalanuvchining O'Z ulanishi (`api/mcp/my`). */
export interface McpOwnConnection {
  id: string
  clientName: string
  redirectHosts: string[]
  createdAt: string
  lastUsedAt: string | null
  /** Ishlatilmasa qachon to'xtaydi (bekor qilinmagan bo'lsa). */
  expiresAt: string | null
  revokedAt: string | null
  active: boolean
  calls: number
}

export interface MyMcp {
  /** Shu akkaunt hozir AI ilova ulay oladimi. */
  allowed: boolean
  /** Nega yo'q (ruxsat bo'lmasa). */
  reason: string | null
  /** MCP serverda yoqilganmi. */
  enabled: boolean
  /** AI ilovasiga qo'yiladigan manzil (ruxsat va server yoqilgan bo'lsa). */
  endpoint: string | null
  connections: McpOwnConnection[]
}

export interface McpAuditRow {
  id: number
  at: string
  userId: string
  userName: string
  clientName: string
  tool: string
  arguments: string
  outcome: 'ok' | 'denied' | 'error'
  rowCount: number | null
  durationMs: number
}

export interface McpAuditPage {
  total: number
  page: number
  pageSize: number
  rows: McpAuditRow[]
}

export async function getMcpStatus(): Promise<McpStatus> {
  const { data } = await api.get<McpStatus>('/admin/mcp/status')
  return data
}

export async function getMcpConnections(): Promise<McpConnection[]> {
  const { data } = await api.get<McpConnection[]>('/admin/mcp/connections')
  return data
}

export async function getMcpAudit(params: { outcome?: string; page?: number; pageSize?: number }): Promise<McpAuditPage> {
  const { data } = await api.get<McpAuditPage>('/admin/mcp/audit', { params })
  return data
}

export async function revokeMcpConnection(id: string): Promise<void> {
  await api.post(`/admin/mcp/connections/${id}/revoke`)
}

export async function getMcpAdmins(): Promise<McpAdminAccess[]> {
  const { data } = await api.get<McpAdminAccess[]>('/admin/mcp/admins')
  return data
}

export async function setMcpAdminAccess(userId: string, enabled: boolean): Promise<{ enabled: boolean; revoked: number }> {
  const { data } = await api.put<{ enabled: boolean; revoked: number }>(
    `/admin/mcp/admins/${encodeURIComponent(userId)}/access`,
    { enabled },
  )
  return data
}

export async function getMyMcp(): Promise<MyMcp> {
  const { data } = await api.get<MyMcp>('/mcp/my')
  return data
}

export async function revokeMyMcpConnection(id: string): Promise<void> {
  await api.post(`/mcp/my/connections/${id}/revoke`)
}

export async function revokeAllMyMcpConnections(): Promise<{ revoked: number }> {
  const { data } = await api.post<{ revoked: number }>('/mcp/my/revoke-all')
  return data
}
