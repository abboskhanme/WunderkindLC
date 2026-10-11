import { useEffect, useState } from 'react'
import { CheckCircle2, Copy, PlugZap, ShieldCheck, XCircle } from 'lucide-react'
import {
  getMcpAudit,
  getMcpConnections,
  getMcpStatus,
  revokeMcpConnection,
  type McpAuditPage,
  type McpConnection,
  type McpStatus,
} from '@/api/services/mcp'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * AI ULANISHLAR (read-only MCP) — faqat superadmin. Rahbariyat o'z AI ilovasini (Claude,
 * ChatGPT) tizimga ulab, ma'lumotni FAQAT O'QIYDI: vositalar hech narsani o'zgartirmaydi va
 * hech kimga xabar yubormaydi. Bu yerda: holat, ulanish manzili, ulangan ilovalar (bekor qilish)
 * va har bir so'rov jurnali. Server — `McpAdminController`, qoidalar — `docs/SPEC-mcp.md`.
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
              Ulana oladiganlar: superadmin, admin va «Rollar» da <b>«AI ulanish (MCP, faqat o'qish)»</b> ruxsati berilgan
              xodim. O'qituvchi, o'quvchi va ota-ona ulana olmaydi. Vositalar soni: {status.toolCount} guruh.
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
                    <td className="py-2 pr-3">{c.calls}</td>
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

/** Server UTC vaqtini markaz vaqtida (Asia/Tashkent) "DD.MM.YYYY HH:mm" ko'rinishida — brauzer mintaqasidan qat'i nazar. */
function fmt(iso: string): string {
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
