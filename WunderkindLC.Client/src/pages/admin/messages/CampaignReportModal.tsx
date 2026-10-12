import { useEffect, useState } from 'react'
import { Ban, Paperclip, Search } from 'lucide-react'
import {
  cancelCampaign,
  getCampaign,
  getCampaignDeliveries,
  type CampaignDetail,
  type CampaignStudentDeliveries,
} from '@/api/services/messaging'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { renderTelegramHtml } from '@/lib/telegramHtml'
import { relationIcon, relationLabel } from '@/lib/relations'
import {
  DELIVERY_META,
  REPORT_FILTERS,
  campaignKindLabel,
  campaignStatusLabel,
  deliveryView,
  isCampaignOpen,
} from '@/lib/telegramDelivery'
import { apiErrorMessage, cn, formatDateTime, formatTime } from '@/lib/utils'

const PAGE_SIZE = 50
const POLL_MS = 3000

/**
 * Kampaniya HISOBOTI (CRM upgrade P2.8): har o'quvchi bo'yicha qatorlar (kimning chatiga, holat,
 * vaqt, «Qabul qildi»). Holat filtri, qidiruv; navbatda xabar bo'lsa «Bekor qilish». Kampaniya
 * tugaguncha har 3 soniyada yangilanadi.
 */
export function CampaignReportModal({
  campaignId,
  canCancel,
  onClose,
  onChanged,
}: {
  campaignId: string
  canCancel: boolean
  onClose: () => void
  /** Bekor qilingach ro'yxat yangilansin. */
  onChanged?: () => void
}) {
  const [detail, setDetail] = useState<CampaignDetail | null>(null)
  const [rows, setRows] = useState<CampaignStudentDeliveries[]>([])
  const [total, setTotal] = useState(0)
  const [status, setStatus] = useState('')
  const [q, setQ] = useState('')
  const [page, setPage] = useState(1)
  const [error, setError] = useState('')
  const [tick, setTick] = useState(0)
  const [cancelling, setCancelling] = useState(false)

  const key = JSON.stringify({ campaignId, status, q: q.trim(), page, tick })
  useEffect(() => {
    const k = JSON.parse(key) as { campaignId: string; status: string; q: string; page: number }
    let alive = true
    const t = window.setTimeout(() => {
      Promise.all([
        getCampaign(k.campaignId),
        getCampaignDeliveries(k.campaignId, { status: k.status || undefined, q: k.q || undefined, page: k.page, pageSize: PAGE_SIZE }),
      ])
        .then(([d, p]) => {
          if (!alive) return
          setDetail(d)
          setRows(p.items)
          setTotal(p.total)
          setError('')
        })
        .catch((e) => alive && setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")))
    }, 250)
    return () => {
      alive = false
      window.clearTimeout(t)
    }
  }, [key])

  // Tugaguncha har 3 s — yangi holatlar (yuborildi / qabul qildi / bloklagan).
  const open = detail ? isCampaignOpen(detail.status) || detail.pending > 0 : false
  useEffect(() => {
    if (!open) return
    const id = window.setInterval(() => setTick((t) => t + 1), POLL_MS)
    return () => window.clearInterval(id)
  }, [open])

  const cancel = async () => {
    if (!confirm("Navbatdagi (hali yuborilmagan) xabarlar bekor qilinsinmi?")) return
    setCancelling(true)
    try {
      const r = await cancelCampaign(campaignId)
      alert(`${r.cancelled} ta xabar bekor qilindi`)
      setTick((t) => t + 1)
      onChanged?.()
    } catch (e) {
      alert(apiErrorMessage(e, "Bekor qilib bo'lmadi"))
    } finally {
      setCancelling(false)
    }
  }

  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  return (
    <Modal
      open
      onClose={onClose}
      size="xl"
      title={detail ? `${campaignKindLabel(detail.kind)} — ${detail.title}` : 'Hisobot'}
      footer={
        <>
          {canCancel && detail && detail.pending > 0 && (
            <Button variant="danger" onClick={cancel} disabled={cancelling}>
              <Ban className="h-4 w-4" /> {cancelling ? 'Bekor qilinmoqda...' : 'Bekor qilish'}
            </Button>
          )}
          <Button variant="secondary" onClick={onClose}>
            Yopish
          </Button>
        </>
      }
    >
      {error && <p className="mb-3 text-sm font-medium text-red-600">{error}</p>}
      {!detail ? (
        !error && <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-[1fr_auto]">
            <div className="max-h-48 overflow-y-auto whitespace-pre-wrap break-words rounded-xl bg-slate-50 p-3 text-sm text-slate-700">
              {renderTelegramHtml(detail.html)}
              {detail.attachment && (
                <p className="mt-2 inline-flex items-center gap-1 text-xs text-slate-500">
                  <Paperclip className="h-3.5 w-3.5" /> {detail.attachment.fileName}
                </p>
              )}
            </div>
            <div className="grid min-w-[220px] grid-cols-3 gap-2 text-center text-xs">
              <Stat label="Jami" value={detail.total} />
              <Stat label="Yuborildi" value={detail.sent} tone="text-sky-700" />
              <Stat label="Qabul qildi" value={detail.acked} tone="text-emerald-700" />
              <Stat label="Navbatda" value={detail.pending} tone="text-amber-700" />
              <Stat label="Bloklagan" value={detail.blocked} tone="text-red-600" />
              <Stat label="Xato" value={detail.failed} tone="text-red-600" />
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2 text-xs text-slate-500">
            <Badge tone={isCampaignOpen(detail.status) ? 'amber' : 'default'}>{campaignStatusLabel(detail.status)}</Badge>
            <span>{formatDateTime(detail.createdAt)}</span>
            {detail.senderName && <span>· {detail.senderName}</span>}
            {detail.ackRequired && <span>· «✅ Qabul qildim» tugmasi bilan</span>}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {REPORT_FILTERS.map((f) => (
              <button
                key={f.key}
                type="button"
                onClick={() => {
                  setStatus(f.key)
                  setPage(1)
                }}
                className={cn(
                  'rounded-full border px-2.5 py-1 text-xs font-medium',
                  status === f.key ? 'border-brand-400 bg-brand-50 text-brand-700' : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                )}
              >
                {f.label}
              </button>
            ))}
            <div className="relative ml-auto w-full sm:w-60">
              <Search className="pointer-events-none absolute left-2.5 top-2 h-4 w-4 text-slate-400" />
              <input
                value={q}
                onChange={(e) => {
                  setQ(e.target.value)
                  setPage(1)
                }}
                placeholder="Ism bo'yicha..."
                className="w-full rounded-lg border border-slate-200 py-1.5 pl-8 pr-2 text-sm outline-none focus:border-brand-400"
              />
            </div>
          </div>

          <div className="divide-y divide-slate-100 rounded-xl border border-slate-200">
            {rows.length === 0 && <p className="py-8 text-center text-sm text-slate-400">Qator yo'q</p>}
            {rows.map((s) => (
              <div key={s.studentId} className="flex flex-col gap-1.5 px-3 py-2.5 sm:flex-row sm:items-start">
                <div className="min-w-0 sm:w-64">
                  <p className="truncate text-sm font-medium text-slate-700">{s.studentName}</p>
                  {s.groupNames.length > 0 && <p className="truncate text-xs text-slate-400">{s.groupNames.join(', ')}</p>}
                </div>
                <div className="flex flex-1 flex-wrap gap-1.5">
                  {s.rows.map((r) => {
                    const v = DELIVERY_META[deliveryView(r.status, r.ackAt)]
                    return (
                      <span
                        key={r.deliveryId}
                        className={cn('inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs', v.cls)}
                        title={r.error || undefined}
                      >
                        {r.relation && <span aria-hidden>{relationIcon(r.relation)}</span>}
                        {r.relation ? relationLabel(r.relation) : ''}
                        <span className="font-semibold">
                          {v.icon} {v.label}
                        </span>
                        {r.ackAt ? (
                          <span className="opacity-80">{formatTime(r.ackAt)}</span>
                        ) : r.sentAt ? (
                          <span className="opacity-80">{formatTime(r.sentAt)}</span>
                        ) : null}
                      </span>
                    )
                  })}
                </div>
              </div>
            ))}
          </div>

          {pages > 1 && (
            <div className="flex items-center justify-end gap-2 text-sm">
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
        </div>
      )}
    </Modal>
  )
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="rounded-lg border border-slate-200 px-2 py-1.5">
      <p className={cn('text-base font-bold', tone ?? 'text-slate-700')}>{value}</p>
      <p className="text-slate-500">{label}</p>
    </div>
  )
}
