import { useEffect, useState } from 'react'
import { Copy, PlugZap } from 'lucide-react'
import { getMyMcp, revokeAllMyMcpConnections, revokeMyMcpConnection, type MyMcp, type McpOwnConnection } from '@/api/services/mcp'
import { useAuth } from '@/context/auth-context'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage } from '@/lib/utils'
import { liveMcpConnections, mcpConnectionState, mcpTime } from '@/lib/mcp'

/** Panel rollari — faqat ular AI ulana oladi (server: `McpAccess.IsAllowed`). */
const PANEL_ROLES = ['superadmin', 'admin', 'staff']

/**
 * «MENING AI ULANISHLARIM» — foydalanuvchi O'Z AI ulanishlarini (Claude, ChatGPT…) ko'radi va bekor
 * qiladi. Akkaunt sozlamalarida turadi (login/parol yonida — sessiyalar qatori). Faqat ulana oladigan
 * yoki ulanishi bor foydalanuvchiga ko'rinadi. Server — `McpMyConnectionsController` (`api/mcp/my`):
 * ro'yxat va bekor qilish FAQAT o'z ulanishlari bo'yicha; bekor qilinganda AI ilova darhol to'xtaydi.
 */
export function MyMcpConnections() {
  const { user } = useAuth()
  const panelUser = !!user && PANEL_ROLES.includes(user.role)
  const [data, setData] = useState<MyMcp | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [busy, setBusy] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!panelUser) return
    let cancelled = false
    getMyMcp()
      .then((d) => {
        if (cancelled) return
        setData(d)
        setError('')
      })
      .catch((e) => !cancelled && setError(apiErrorMessage(e, "AI ulanishlaringizni yuklab bo'lmadi")))
    return () => {
      cancelled = true
    }
  }, [panelUser, tick])

  if (!panelUser) return null
  // Ulana olmaydigan va hech qachon ulanmagan foydalanuvchiga blok ko'rsatilmaydi.
  if (!error && (!data || (!data.allowed && data.connections.length === 0))) return null

  const revoke = async (c: McpOwnConnection) => {
    if (!window.confirm(`«${c.clientName}» ulanishi bekor qilinsinmi? AI ilova darhol ma'lumot ololmay qoladi.`)) return
    setBusy(c.id)
    try {
      await revokeMyMcpConnection(c.id)
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Ulanishni bekor qilib bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  const revokeAll = async () => {
    if (!window.confirm("Barcha AI ulanishlaringiz bekor qilinsinmi? Har bir AI ilovada qayta ulanish kerak bo'ladi.")) return
    setBusy('all')
    try {
      await revokeAllMyMcpConnections()
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Ulanishlarni bekor qilib bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  const copy = async () => {
    if (!data?.endpoint) return
    try {
      await navigator.clipboard.writeText(data.endpoint)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard may be blocked — the address stays visible to copy by hand.
    }
  }

  const live = data ? liveMcpConnections(data.connections) : 0

  return (
    <Card
      className="mt-4"
      title={
        <span className="flex items-center gap-2">
          <PlugZap className="h-4 w-4 text-brand-600" /> Mening AI ulanishlarim
        </span>
      }
      sub="Claude, ChatGPT kabi AI ilovalaringiz markaz ma'lumotlarini FAQAT O'QIYDI. Tanimagan yoki endi ishlatmaydigan ulanishni shu yerda bekor qiling."
      actions={
        live > 1 ? (
          <Button variant="danger" type="button" disabled={busy !== null} onClick={revokeAll}>
            Hammasini bekor qilish
          </Button>
        ) : undefined
      }
    >
      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {data && !data.allowed && (
        <p className="mb-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
          Hozir AI ulanish ruxsatingiz yo'q — ulanishlaringiz ishlamaydi. {data.reason}
        </p>
      )}
      {data?.allowed && data.endpoint && (
        <div className="mb-3">
          <label className="mb-1 block text-sm font-medium text-slate-700">AI ilovasiga qo'yiladigan manzil</label>
          <div className="flex max-w-xl items-center gap-2">
            <code className="min-w-0 flex-1 truncate rounded-lg border border-line bg-paper2 px-3 py-2 text-xs">
              {data.endpoint}
            </code>
            <Button variant="secondary" onClick={copy} type="button">
              <Copy className="h-4 w-4" /> {copied ? 'Nusxalandi' : 'Nusxa'}
            </Button>
          </div>
        </div>
      )}
      {data?.allowed && !data.enabled && (
        <p className="mb-3 text-sm text-slate-500">AI ulanish serverda hozircha yoqilmagan.</p>
      )}
      {data && data.connections.length === 0 ? (
        <p className="text-sm text-slate-500">Hali birorta AI ilova ulanmagan.</p>
      ) : (
        data && (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[620px] text-sm">
              <thead>
                <tr className="border-b border-line text-left text-xs text-slate-500">
                  <th className="py-2 pr-3">Ilova</th>
                  <th className="py-2 pr-3">Ulangan</th>
                  <th className="py-2 pr-3">Oxirgi foydalanish</th>
                  <th className="py-2 pr-3">Amal qiladi</th>
                  <th className="py-2 pr-3">So'rovlar</th>
                  <th className="py-2 pr-3">Holat</th>
                  <th className="py-2" />
                </tr>
              </thead>
              <tbody>
                {data.connections.map((c) => {
                  const state = mcpConnectionState(c)
                  return (
                    <tr key={c.id} className="border-b border-line/60 align-top">
                      <td className="py-2 pr-3">
                        <div className="font-semibold text-ink">{c.clientName}</div>
                        <div className="text-xs text-slate-400">{c.redirectHosts.join(', ')}</div>
                      </td>
                      <td className="whitespace-nowrap py-2 pr-3">{mcpTime(c.createdAt)}</td>
                      <td className="whitespace-nowrap py-2 pr-3">{c.lastUsedAt ? mcpTime(c.lastUsedAt) : '—'}</td>
                      <td className="whitespace-nowrap py-2 pr-3">{c.expiresAt ? mcpTime(c.expiresAt) : '—'}</td>
                      <td className="py-2 pr-3">{c.calls}</td>
                      <td className="py-2 pr-3">
                        {state === 'active' ? (
                          <Badge tone="green">Faol</Badge>
                        ) : state === 'revoked' ? (
                          <Badge tone="red">Bekor qilingan</Badge>
                        ) : (
                          <Badge tone="default">Ishlamayapti</Badge>
                        )}
                      </td>
                      <td className="py-2 text-right">
                        {!c.revokedAt && (
                          <Button variant="danger" type="button" disabled={busy !== null} onClick={() => revoke(c)}>
                            Bekor qilish
                          </Button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )
      )}
    </Card>
  )
}
