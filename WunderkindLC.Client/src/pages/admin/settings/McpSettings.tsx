import { useEffect, useState } from 'react'
import { AlertTriangle, CheckCircle2, Copy, PlugZap, ShieldCheck, XCircle } from 'lucide-react'
import {
  getMcpAdmins,
  getMcpAudit,
  getMcpConnections,
  getMcpStatus,
  revokeMcpConnection,
  setMcpAdminAccess,
  type McpAdminAccess,
  type McpAuditPage,
  type McpConnection,
  type McpStatus,
} from '@/api/services/mcp'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Switch } from '@/components/ui/list/Switch'
import { apiErrorMessage, cn } from '@/lib/utils'
import { mcpTime as fmt } from '@/lib/mcp'

/**
 * AI ULANISHLAR (read-only MCP) — faqat superadmin. Rahbariyat o'z AI ilovasini (Claude,
 * ChatGPT) tizimga ulab, ma'lumotni FAQAT O'QIYDI: vositalar hech narsani o'zgartirmaydi va
 * hech kimga xabar yubormaydi. Bu yerda: holat, ulanish manzili, administratorlarga AI ruxsati,
 * ulangan ilovalar (bekor qilish, 24 soatlik hajm belgisi) va har bir so'rov jurnali.
 * Server — `McpAdminController`, qoidalar — `docs/SPEC-mcp.md`. Har kim O'Z ulanishlarini
 * «Akkaunt sozlamalari» dagi `MyMcpConnections` blokida boshqaradi.
 */
export function McpSettings() {
  const [status, setStatus] = useState<McpStatus | null>(null)
  const [connections, setConnections] = useState<McpConnection[]>([])
  const [audit, setAudit] = useState<McpAuditPage | null>(null)
  const [outcome, setOutcome] = useState('')
  const [auditPage, setAuditPage] = useState(1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [copied, setCopied] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getMcpStatus(), getMcpConnections()])
      .then(([s, c]) => {
        if (cancelled) return
        setStatus(s)
        setConnections(c)
        setError('')
      })
      .catch((e) => !cancelled && setError(apiErrorMessage(e, "AI ulanish ma'lumotlarini yuklab bo'lmadi")))
      .finally(() => !cancelled && setLoading(false))
    return () => {
      cancelled = true
    }
  }, [tick])

  useEffect(() => {
    let cancelled = false
    getMcpAudit({ outcome: outcome || undefined, page: auditPage, pageSize: 50 })
      .then((a) => !cancelled && setAudit(a))
      .catch((e) => !cancelled && setError(apiErrorMessage(e, "So'rovlar jurnalini yuklab bo'lmadi")))
    return () => {
      cancelled = true
    }
  }, [outcome, auditPage, tick])

  const copy = async () => {
    if (!status) return
    try {
      await navigator.clipboard.writeText(status.endpoint)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard may be blocked (non-https dev, permissions) — the address stays visible to copy by hand.
    }
  }

  const revoke = async (c: McpConnection) => {
    if (!window.confirm(`«${c.clientName}» (${c.userName}) ulanishi bekor qilinsinmi? AI ilova darhol ma'lumot ololmay qoladi.`))
      return
    setBusy(c.id)
    try {
      await revokeMcpConnection(c.id)
      setTick((x) => x + 1)
    } catch (e) {
      alert(apiErrorMessage(e, "Ulanishni bekor qilib bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  if (loading) return <Loader label="Yuklanmoqda..." />

  return (
    <div className="space-y-4">
      <Card
        title={
          <span className="flex flex-wrap items-center gap-2">
            <PlugZap className="h-4 w-4 text-brand-600" /> AI ulanish (MCP)
            {status?.enabled ? (
              <Badge tone="green">
                <CheckCircle2 className="h-3.5 w-3.5" /> Yoqilgan
              </Badge>
            ) : (
              <Badge tone="default">
                <XCircle className="h-3.5 w-3.5" /> O'chiq
              </Badge>
            )}
          </span>
        }
      >
        {error && <p className="mb-3 text-sm text-red-600">{error}</p>}
        <p className="mb-3 text-sm text-slate-500">
          Rahbariyat o'z AI ilovasini (Claude, ChatGPT va boshqa MCP mijozlari) tizimga ulab, markaz ma'lumotlarini
          savol-javob orqali ko'ra oladi. Ulanish <b>FAQAT O'QISH</b> uchun: AI hech narsani o'zgartira, o'chira yoki
          qo'sha olmaydi va hech kimga xabar yubormaydi. Har kim faqat o'ziga ruxsat berilgan bo'limlarni ko'radi.
        </p>

        {status?.enabled ? (
          <>
            <label className="mb-1 block text-sm font-medium text-slate-700">Ulanish manzili</label>
            <div className="flex max-w-xl items-center gap-2">
              <code className="min-w-0 flex-1 truncate rounded-lg border border-line bg-paper2 px-3 py-2 text-xs">
                {status.endpoint}
              </code>
              <Button variant="secondary" onClick={copy} type="button">
                <Copy className="h-4 w-4" /> {copied ? 'Nusxalandi' : 'Nusxa'}
              </Button>
            </div>
            <ol className="mt-3 list-decimal space-y-1 pl-5 text-sm text-slate-600">
              <li>
                <b>Claude:</b> Settings → Connectors → «Add custom connector» → shu manzilni qo'ying.
              </li>
              <li>
                <b>ChatGPT:</b> Settings → Connectors (Developer mode) → yangi MCP server → shu manzil.
              </li>
              <li>
                Ochilgan oynada tizim login va parolingizni kiriting va «Ruxsat berish» ni bosing. Parol AI ilovaga
                berilmaydi.
              </li>
            </ol>
            <p className="mt-3 flex items-start gap-2 text-xs text-slate-500">
              <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-emerald-600" />
              Ulana oladiganlar: superadmin, siz pastda <b>yoqqan</b> administrator va «Rollar» da{' '}
              <b>«AI ulanish (MCP, faqat o'qish)»</b> ruxsati berilgan xodim. O'qituvchi, o'quvchi va ota-ona ulana
              olmaydi. Har bir yangi ulanish va 24 soatda odatdan ko'p so'rov haqida sizga Telegram orqali xabar
              keladi. Vositalar soni: {status.toolCount} guruh.
            </p>
          </>
        ) : (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <p className="font-semibold">AI ulanish serverda yoqilmagan.</p>
            {status?.disabledReason && <p className="mt-1 font-mono text-xs">{status.disabledReason}</p>}
            <p className="mt-2">
              Yoqish uchun serverdagi <code>.env</code> fayliga <code>MCP_DB_RO_PASSWORD</code> qo'shiladi (16+ belgi:
              harf, raqam, <code>-</code> va <code>_</code>), keyin ilova qayta ishga tushiriladi. Batafsil:{' '}
              <code>docs/MCP.md</code>.
            </p>
          </div>
        )}
      </Card>

      <AdminAccessCard onChanged={() => setTick((x) => x + 1)} />

      <Card title="Ulangan AI ilovalar">
        {connections.length === 0 ? (
          <p className="text-sm text-slate-500">Hali hech kim ulanmagan.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead>
                <tr className="border-b border-line text-left text-xs text-slate-500">
                  <th className="py-2 pr-3">Ilova</th>
                  <th className="py-2 pr-3">Foydalanuvchi</th>
                  <th className="py-2 pr-3">Ulangan</th>
                  <th className="py-2 pr-3">Oxirgi foydalanish</th>
                  <th className="py-2 pr-3">So'rovlar</th>
                  <th className="py-2 pr-3">Holat</th>
                  <th className="py-2" />
                </tr>
              </thead>
              <tbody>
                {connections.map((c) => (
                  <tr key={c.id} className="border-b border-line/60 align-top">
                    <td className="py-2 pr-3">
                      <div className="font-semibold text-ink">{c.clientName}</div>
                      <div className="text-xs text-slate-400">{c.redirectHosts.join(', ')}</div>
                    </td>
                    <td className="py-2 pr-3">
                      {c.userName}
                      {!c.userAllowed && <div className="text-xs text-red-600">ruxsati olib qo'yilgan</div>}
                    </td>
                    <td className="py-2 pr-3 whitespace-nowrap">{fmt(c.createdAt)}</td>
                    <td className="py-2 pr-3 whitespace-nowrap">{c.lastUsedAt ? fmt(c.lastUsedAt) : '—'}</td>
                    <td className="py-2 pr-3">
                      {c.calls}
                      <div className="text-xs text-slate-400" title="Oxirgi 24 soat: so'rovlar / o'qilgan qatorlar">
                        24 soat: {c.calls24h} / {c.rows24h} qator
                      </div>
                      {c.volumeAlert && (
                        <div
                          className="mt-0.5 inline-flex items-center gap-1 text-xs font-semibold text-red-600"
                          title="24 soatda so'rovlar yoki o'qilgan qatorlar chegaradan oshgan — so'rovlar jurnalini tekshiring"
                        >
                          <AlertTriangle className="h-3.5 w-3.5" /> Ko'p so'rov
                        </div>
                      )}
                    </td>
                    <td className="py-2 pr-3">
                      {c.active ? (
                        <Badge tone="green">Faol</Badge>
                      ) : c.revokedAt ? (
                        <Badge tone="red">Bekor qilingan</Badge>
                      ) : (
                        <Badge tone="default">Muddati o'tgan</Badge>
                      )}
                    </td>
                    <td className="py-2 text-right">
                      {!c.revokedAt && (
                        <Button variant="danger" type="button" disabled={busy === c.id} onClick={() => revoke(c)}>
                          Bekor qilish
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Card
        title="So'rovlar jurnali"
        actions={
          <select
            value={outcome}
            onChange={(e) => {
              setOutcome(e.target.value)
              setAuditPage(1)
            }}
            className="rounded-lg border border-line bg-white px-2 py-1 text-sm"
          >
            <option value="">Hammasi</option>
            <option value="ok">Muvaffaqiyatli</option>
            <option value="denied">Ruxsat yo'q</option>
            <option value="error">Xato</option>
          </select>
        }
      >
        {!audit || audit.rows.length === 0 ? (
          <p className="text-sm text-slate-500">Yozuv yo'q.</p>
        ) : (
          <>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] text-sm">
                <thead>
                  <tr className="border-b border-line text-left text-xs text-slate-500">
                    <th className="py-2 pr-3">Vaqt</th>
                    <th className="py-2 pr-3">Foydalanuvchi</th>
                    <th className="py-2 pr-3">Ilova</th>
                    <th className="py-2 pr-3">Vosita</th>
                    <th className="py-2 pr-3">Parametrlar</th>
                    <th className="py-2 pr-3">Natija</th>
                  </tr>
                </thead>
                <tbody>
                  {audit.rows.map((r) => (
                    <tr key={r.id} className="border-b border-line/60 align-top">
                      <td className="py-1.5 pr-3 whitespace-nowrap">{fmt(r.at)}</td>
                      <td className="py-1.5 pr-3">{r.userName}</td>
                      <td className="py-1.5 pr-3">{r.clientName}</td>
                      <td className="py-1.5 pr-3 font-mono text-xs">{r.tool}</td>
                      <td className="max-w-[260px] truncate py-1.5 pr-3 font-mono text-xs text-slate-500" title={r.arguments}>
                        {r.arguments || '—'}
                      </td>
                      <td className="py-1.5 pr-3 whitespace-nowrap">
                        <span
                          className={cn(
                            'text-xs font-semibold',
                            r.outcome === 'ok' ? 'text-emerald-600' : r.outcome === 'denied' ? 'text-amber-600' : 'text-red-600',
                          )}
                        >
                          {r.outcome === 'ok' ? `${r.rowCount ?? 0} qator` : r.outcome === 'denied' ? "ruxsat yo'q" : 'xato'}
                        </span>
                        <span className="ml-1 text-xs text-slate-400">{r.durationMs} ms</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="mt-3 flex items-center justify-between text-sm text-slate-500">
              <span>
                Jami: {audit.total} · {audit.page}-sahifa
              </span>
              <div className="flex gap-2">
                <Button variant="secondary" type="button" disabled={audit.page <= 1} onClick={() => setAuditPage((p) => p - 1)}>
                  Oldingi
                </Button>
                <Button
                  variant="secondary"
                  type="button"
                  disabled={audit.page * audit.pageSize >= audit.total}
                  onClick={() => setAuditPage((p) => p + 1)}
                >
                  Keyingi
                </Button>
              </div>
            </div>
          </>
        )}
      </Card>
    </div>
  )
}

/**
 * ADMINISTRATORLAR — har bir admin akkauntiga AI ulanish superadmin tomonidan ALOHIDA yoqiladi
 * (server: `McpAccess.IsAllowed`, `PUT api/admin/mcp/admins/{id}/access`). O'chirilsa o'sha adminning
 * ulanishlari darhol bekor qilinadi. Optimistik EMAS: kalit faqat server javobidan keyin o'zgaradi.
 */
function AdminAccessCard({ onChanged }: { onChanged: () => void }) {
  const [admins, setAdmins] = useState<McpAdminAccess[] | null>(null)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [busy, setBusy] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    getMcpAdmins()
      .then((a) => {
        if (cancelled) return
        setAdmins(a)
        setError('')
      })
      .catch((e) => !cancelled && setError(apiErrorMessage(e, "Administratorlar ro'yxatini yuklab bo'lmadi")))
    return () => {
      cancelled = true
    }
  }, [tick])

  const toggle = async (a: McpAdminAccess, next: boolean) => {
    if (busy) return
    if (!next && a.activeConnections > 0
        && !window.confirm(`${a.fullName}: AI ruxsati o'chirilsa, ${a.activeConnections} ta ulanishi darhol bekor qilinadi. Davom etilsinmi?`))
      return
    setBusy(a.id)
    setError('')
    try {
      await setMcpAdminAccess(a.id, next)
      setTick((x) => x + 1)
      onChanged()
    } catch (e) {
      setError(apiErrorMessage(e, "AI ruxsatini o'zgartirib bo'lmadi"))
    } finally {
      setBusy(null)
    }
  }

  return (
    <Card
      title="Administratorlar uchun AI ruxsati"
      sub="Administrator AI ilovani faqat siz shu yerda yoqqandan keyin ulay oladi. Xodimlarga ruxsat Boshqaruv → Rollar orqali beriladi, superadmin esa har doim ulana oladi."
    >
      {error && <p className="mb-3 text-sm text-red-600">{error}</p>}
      {!admins ? (
        !error && <p className="text-sm text-slate-500">Yuklanmoqda...</p>
      ) : admins.length === 0 ? (
        <p className="text-sm text-slate-500">Administrator akkauntlari yo'q.</p>
      ) : (
        <ul className="divide-y divide-line/60">
          {admins.map((a) => (
            <li key={a.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
              <div className="min-w-0">
                <div className="font-semibold text-ink">{a.fullName}</div>
                <div className="text-xs text-slate-400">
                  {a.enabled ? `Faol ulanishlar: ${a.activeConnections}` : 'AI ulanish yopiq'}
                </div>
              </div>
              <Switch
                checked={a.enabled}
                onChange={(next) => toggle(a, next)}
                label={busy === a.id ? 'Saqlanmoqda...' : a.enabled ? 'Yoqilgan' : "O'chiq"}
              />
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
