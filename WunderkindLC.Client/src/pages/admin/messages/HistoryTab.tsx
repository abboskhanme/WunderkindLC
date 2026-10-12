import { useEffect, useMemo, useState } from 'react'
import { History, Paperclip } from 'lucide-react'
import { getAutoStats, getCampaigns, type AutoStatsRow, type CampaignRow } from '@/api/services/messaging'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Input } from '@/components/ui/Input'
import { Loader } from '@/components/ui/Loader'
import { campaignKindLabel, campaignStatusLabel, isCampaignOpen } from '@/lib/telegramDelivery'
import { apiErrorMessage, cn, formatDate, formatDateTime } from '@/lib/utils'
import { CampaignReportModal } from './CampaignReportModal'

const PAGE_SIZE = 30

type Sub = 'campaigns' | 'auto'

/**
 * «Tarix» (CRM upgrade P2.8): Telegram kampaniyalari (sana, tur, kimga, kim yubordi, jami / yuborildi /
 * xato / bloklagan / qabul qildi, holat) — qatorni bosib hisobot ochiladi; «Avto xabarlar» — kun ×
 * hodisa statistikasi. Sonlar outbox qatorlaridan (`MessageDelivery`).
 */
export function HistoryTab({
  openCampaignId,
  onCampaignClosed,
  canCancel,
}: {
  /** Manzildagi `?campaign=` — yangi yuborilgan kampaniya hisoboti ochiq turadi. */
  openCampaignId?: string | null
  onCampaignClosed?: () => void
  canCancel: boolean
}) {
  const [sub, setSub] = useState<Sub>('campaigns')
  const [kind, setKind] = useState('')
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<CampaignRow[] | null>(null)
  const [total, setTotal] = useState(0)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [reportId, setReportId] = useState<string | null>(openCampaignId ?? null)

  const key = JSON.stringify({ kind, page, tick })
  useEffect(() => {
    const k = JSON.parse(key) as { kind: string; page: number }
    let alive = true
    getCampaigns({ kind: k.kind || undefined, page: k.page, pageSize: PAGE_SIZE })
      .then((p) => {
        if (!alive) return
        setItems(p.items)
        setTotal(p.total)
        setError('')
      })
      // Sahifaning ASOSIY ma'lumoti — xato ko'rsatiladi (`error-visibility.md` §4).
      .catch((e) => alive && setError(apiErrorMessage(e, "Tarixni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [key])

  // Yuborilayotgan kampaniya bo'lsa ro'yxat ham yangilanib tursin (har 5 s).
  const anyOpen = (items ?? []).some((c) => isCampaignOpen(c.status))
  useEffect(() => {
    if (!anyOpen) return
    const id = window.setInterval(() => setTick((t) => t + 1), 5000)
    return () => window.clearInterval(id)
  }, [anyOpen])

  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <div className="space-y-4">
      <div className="tabs">
        <button type="button" className={cn('tab', sub === 'campaigns' && 'active')} onClick={() => setSub('campaigns')}>
          Yuborilgan xabarlar
        </button>
        <button type="button" className={cn('tab', sub === 'auto' && 'active')} onClick={() => setSub('auto')}>
          Avto xabarlar
        </button>
      </div>

      {sub === 'auto' ? (
        <AutoStatsPanel />
      ) : (
        <Card
          title="Yuborilgan xabarlar"
          sub="Telegram bot orqali — qatorni bosing: kimga yetdi, kim qabul qildi"
          actions={
            <select
              value={kind}
              onChange={(e) => {
                setKind(e.target.value)
                setPage(1)
              }}
              className="rounded-lg border border-slate-200 px-2 py-1 text-sm"
            >
              <option value="">Barcha turlar</option>
              <option value="broadcast">E'lon</option>
              <option value="archive">Arxiv</option>
              <option value="debt">Qarz eslatmasi</option>
            </select>
          }
          tight
        >
          {error && <p className="px-4 py-3 text-sm font-medium text-red-600">{error}</p>}
          {items === null && !error ? (
            <Loader label="Yuklanmoqda..." />
          ) : (items ?? []).length === 0 ? (
            <div className="py-10 text-center">
              <History className="mx-auto mb-2 h-6 w-6 text-slate-300" />
              <p className="text-sm text-slate-400">Hali xabar yuborilmagan</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-slate-50 text-left text-xs text-slate-500">
                  <tr>
                    <th className="px-3 py-2.5">Sana</th>
                    <th className="px-3 py-2.5">Tur</th>
                    <th className="px-3 py-2.5">Kimga / matn</th>
                    <th className="px-3 py-2.5">Yubordi</th>
                    <th className="px-3 py-2.5 text-center">Jami</th>
                    <th className="px-3 py-2.5 text-center">Yuborildi</th>
                    <th className="px-3 py-2.5 text-center">Xato</th>
                    <th className="px-3 py-2.5 text-center">Bloklagan</th>
                    <th className="px-3 py-2.5 text-center">Qabul qildi</th>
                    <th className="px-3 py-2.5">Holat</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {(items ?? []).map((c) => (
                    <tr key={c.id} onClick={() => setReportId(c.id)} className="cursor-pointer hover:bg-brand-50/40">
                      <td className="whitespace-nowrap px-3 py-2.5 text-xs text-slate-500">{formatDateTime(c.createdAt)}</td>
                      <td className="px-3 py-2.5">
                        <Badge tone={c.kind === 'archive' ? 'default' : c.kind === 'debt' ? 'red' : 'blue'}>
                          {campaignKindLabel(c.kind)}
                        </Badge>
                      </td>
                      <td className="max-w-[320px] px-3 py-2.5">
                        <p className="truncate font-medium text-slate-700">{c.title}</p>
                        <p className="truncate text-xs text-slate-400">
                          {c.hasAttachment && <Paperclip className="mr-1 inline h-3 w-3" />}
                          {c.textPlain || '—'}
                        </p>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5 text-xs text-slate-500">{c.senderName}</td>
                      <td className="px-3 py-2.5 text-center font-mono">{c.total}</td>
                      <td className="px-3 py-2.5 text-center font-mono text-sky-700">{c.sent}</td>
                      <td className="px-3 py-2.5 text-center font-mono text-red-600">{c.failed || '—'}</td>
                      <td className="px-3 py-2.5 text-center font-mono text-red-600">{c.blocked || '—'}</td>
                      <td className="px-3 py-2.5 text-center font-mono text-emerald-700">
                        {c.ackRequired ? c.acked : '—'}
                      </td>
                      <td className="px-3 py-2.5">
                        <Badge tone={isCampaignOpen(c.status) ? 'amber' : c.status === 'cancelled' ? 'default' : 'green'}>
                          {campaignStatusLabel(c.status)}
                        </Badge>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 border-t border-slate-100 px-4 py-3 text-sm">
              <Button variant="secondary" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}>
                Oldingi
              </Button>
              <span className="text-slate-500">
                {page} / {pages}
              </span>
              <Button variant="secondary" onClick={() => setPage((p) => Math.min(pages, p + 1))} disabled={page >= pages}>
                Keyingi
              </Button>
            </div>
          )}
        </Card>
      )}

      {reportId && (
        <CampaignReportModal
          campaignId={reportId}
          canCancel={canCancel}
          onClose={() => {
            setReportId(null)
            onCampaignClosed?.()
            setTick((t) => t + 1)
          }}
          onChanged={() => setTick((t) => t + 1)}
        />
      )}
    </div>
  )
}

/** Bugundan N kun oldin — "yyyy-MM-dd" (lokal). */
function daysAgo(n: number): string {
  const d = new Date()
  d.setDate(d.getDate() - n)
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}

/** «Avto xabarlar» statistikasi: kun × hodisa (ko'pi bilan 92 kun). */
function AutoStatsPanel() {
  const [from, setFrom] = useState(daysAgo(6))
  const [to, setTo] = useState(daysAgo(0))
  const [rows, setRows] = useState<AutoStatsRow[] | null>(null)
  const [error, setError] = useState('')

  const key = `${from}|${to}`
  useEffect(() => {
    const [f, t] = key.split('|')
    let alive = true
    getAutoStats(f, t)
      .then((r) => {
        if (!alive) return
        setRows(r)
        setError('')
      })
      .catch((e) => alive && setError(apiErrorMessage(e, "Statistikani yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [key])

  const totals = useMemo(
    () =>
      (rows ?? []).reduce(
        (a, r) => ({ total: a.total + r.total, sent: a.sent + r.sent, failed: a.failed + r.failed, blocked: a.blocked + r.blocked, acked: a.acked + r.acked }),
        { total: 0, sent: 0, failed: 0, blocked: 0, acked: 0 },
      ),
    [rows],
  )

  return (
    <Card
      title="Avto xabarlar"
      sub="Kun va hodisa bo'yicha — ko'pi bilan 92 kun"
      actions={
        <div className="flex items-end gap-2">
          <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
      }
      tight
    >
      {error && <p className="px-4 py-3 text-sm font-medium text-red-600">{error}</p>}
      {rows === null && !error ? (
        <Loader label="Yuklanmoqda..." />
      ) : (rows ?? []).length === 0 ? (
        <p className="py-10 text-center text-sm text-slate-400">Bu davrda avto xabar yo'q</p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-left text-xs text-slate-500">
              <tr>
                <th className="px-3 py-2.5">Sana</th>
                <th className="px-3 py-2.5">Hodisa</th>
                <th className="px-3 py-2.5 text-center">Jami</th>
                <th className="px-3 py-2.5 text-center">Yuborildi</th>
                <th className="px-3 py-2.5 text-center">Xato</th>
                <th className="px-3 py-2.5 text-center">Bloklagan</th>
                <th className="px-3 py-2.5 text-center">Qabul qildi</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {(rows ?? []).map((r) => (
                <tr key={`${r.date}-${r.trigger}`}>
                  <td className="whitespace-nowrap px-3 py-2 text-xs text-slate-500">{formatDate(r.date)}</td>
                  <td className="px-3 py-2 text-slate-700">{r.label}</td>
                  <td className="px-3 py-2 text-center font-mono">{r.total}</td>
                  <td className="px-3 py-2 text-center font-mono text-sky-700">{r.sent}</td>
                  <td className="px-3 py-2 text-center font-mono text-red-600">{r.failed || '—'}</td>
                  <td className="px-3 py-2 text-center font-mono text-red-600">{r.blocked || '—'}</td>
                  <td className="px-3 py-2 text-center font-mono text-emerald-700">{r.acked || '—'}</td>
                </tr>
              ))}
              <tr className="bg-slate-50 font-semibold">
                <td className="px-3 py-2" colSpan={2}>
                  Jami
                </td>
                <td className="px-3 py-2 text-center font-mono">{totals.total}</td>
                <td className="px-3 py-2 text-center font-mono text-sky-700">{totals.sent}</td>
                <td className="px-3 py-2 text-center font-mono text-red-600">{totals.failed}</td>
                <td className="px-3 py-2 text-center font-mono text-red-600">{totals.blocked}</td>
                <td className="px-3 py-2 text-center font-mono text-emerald-700">{totals.acked}</td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}
