import { api } from '../client'

/**
 * AI ULANISH (read-only MCP) — Sozlamalar → «AI ulanishlar» (faqat superadmin).
 * Server: `McpAdminController` (`api/admin/mcp`), batafsil `docs/SPEC-mcp.md`.
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
