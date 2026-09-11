import { useEffect, useMemo, useState } from 'react'
import { ChevronDown, History, Check } from 'lucide-react'
import {
  getBroadcasts,
  getPushMessages,
  getSmsBatches,
  getSmsLogs,
  getPushConfirmations,
  type SmsLog,
  type PushConfirmation,
} from '@/api/services/messages'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDate } from '@/lib/utils'
import { CHANNELS, CHANNEL_ORDER, type ChannelKey } from '@/config/channels'

type Ch = ChannelKey

/** Birlashgan tarix yozuvi (uchala kanaldan). */
interface HistItem {
  key: string
  channel: Ch
  id: string
  title?: string
  text: string
  audience: string
  createdAt: string
  sentCount: number
  recipientCount: number
  /** Push: tasdiqlash uchun */
  targetCount?: number
  confirmedCount?: number
  /** SMS uchun: yuborish manbai ("eskiz" | "local"). */
  provider?: string
}

/** SMS holatini guruhlaydi. */
function statusInfo(status: string): { label: string; tone: 'green' | 'amber' | 'red' } {
  const s = (status || '').toUpperCase()
  if (s === 'DELIVRD' || s === 'DELIVERED' || s === 'YUBORILDI') return { label: 'Yetkazildi', tone: 'green' }
  if (s === 'WAITING' || s === 'NEW' || s === 'ACCEPTED' || s === 'STORED')
    return { label: 'Kutilmoqda', tone: 'amber' }
  return { label: 'Yetkazilmadi', tone: 'red' }
}

/** Birlashgan xabarlar tarixi (Telegram + Push + SMS) — vaqt bo'yicha kamayish tartibida. */
export function HistoryTab() {
  const [items, setItems] = useState<HistItem[]>([])
  const [loading, setLoading] = useState(true)
  const [openKey, setOpenKey] = useState<string | null>(null)
  /** Kanal filtri: null = Hammasi. */
  const [chFilter, setChFilter] = useState<Ch | null>(null)
  /** SMS tanlanganda — manba bo'yicha filtr (Eskiz/Local). */
  const [providerFilter, setProviderFilter] = useState<'all' | 'eskiz' | 'local'>('all')

  useEffect(() => {
    Promise.all([
      getBroadcasts().catch(() => []),
      getPushMessages().catch(() => []),
      getSmsBatches().catch(() => []),
    ])
      .then(([broadcasts, pushes, sms]) => {
        const list: HistItem[] = [
          ...broadcasts.map((b) => ({
            key: `tg-${b.id}`,
            channel: 'telegram' as Ch,
            id: b.id,
            text: b.text,
            audience: b.className,
            createdAt: b.createdAt,
            sentCount: b.sentCount,
            recipientCount: b.recipientCount,
          })),
          ...pushes.map((p) => ({
            key: `push-${p.id}`,
            channel: 'push' as Ch,
            id: p.id,
            title: p.title,
            text: p.body,
            audience: p.audience,
            createdAt: p.createdAt,
            sentCount: p.sentCount,
            recipientCount: p.recipientCount,
            targetCount: p.targetCount,
            confirmedCount: p.confirmedCount,
          })),
          ...sms.map((s) => ({
            key: `sms-${s.id}`,
            channel: 'sms' as Ch,
            id: s.id,
            text: s.message,
            audience: s.audience,
            createdAt: s.createdAt,
            sentCount: s.sentCount,
            recipientCount: s.recipientCount,
            provider: s.provider,
          })),
        ]
        list.sort((a, b) => (a.createdAt < b.createdAt ? 1 : -1))
        setItems(list)
      })
      .finally(() => setLoading(false))
  }, [])

  if (loading) return <Loader label="Yuklanmoqda..." />

  const visible = (chFilter ? items.filter((it) => it.channel === chFilter) : items).filter(
    (it) => it.channel !== 'sms' || providerFilter === 'all' || (it.provider ?? 'eskiz') === providerFilter,
  )

  return (
    <Card title="Yuborilgan xabarlar" sub="SMS, Telegram va Push — birlashgan tarix">
      {/* Kanal filtri */}
      <div className="tabs mb-3 inline-flex">
        <button
          type="button"
          onClick={() => setChFilter(null)}
          className={cn('tab', chFilter === null && 'active')}
        >
          Hammasi
        </button>
        {CHANNEL_ORDER.map((k) => (
          <button
            key={k}
            type="button"
            onClick={() => setChFilter(k)}
            className={cn('tab', chFilter === k && 'active')}
          >
            {CHANNELS[k].label}
          </button>
        ))}
      </div>

      {chFilter === 'sms' && (
        <div className="tabs mb-3 inline-flex">
          <button
            type="button"
            onClick={() => setProviderFilter('all')}
            className={cn('tab', providerFilter === 'all' && 'active')}
          >
            Manba: hammasi
          </button>
          <button
            type="button"
            onClick={() => setProviderFilter('eskiz')}
            className={cn('tab', providerFilter === 'eskiz' && 'active')}
          >
            Eskiz
          </button>
          <button
            type="button"
            onClick={() => setProviderFilter('local')}
            className={cn('tab', providerFilter === 'local' && 'active')}
          >
            Local
          </button>
        </div>
      )}

      {visible.length === 0 ? (
        <div className="py-10 text-center">
          <History className="mx-auto mb-2 h-6 w-6 text-slate-300" />
          <p className="text-sm text-slate-400">
            {chFilter ? `${CHANNELS[chFilter].label} bo'yicha xabar topilmadi` : 'Hali xabar yuborilmagan'}
          </p>
        </div>
      ) : (
        <div className="space-y-2.5">
          {visible.map((it) => (
            <HistoryRow
              key={it.key}
              item={it}
              open={openKey === it.key}
              onToggle={() => setOpenKey((k) => (k === it.key ? null : it.key))}
            />
          ))}
        </div>
      )}
    </Card>
  )
}

function HistoryRow({
  item,
  open,
  onToggle,
}: {
  item: HistItem
  open: boolean
  onToggle: () => void
}) {
  const meta = CHANNELS[item.channel]
  const Icon = meta.icon
  // Kengaytiriladigan detali bormi (SMS loglari yoki Push tasdiqlari)
  const expandable = item.channel === 'sms' || (item.channel === 'push' && (item.targetCount ?? 0) > 0)

  return (
    <div className="rounded-lg border border-slate-100 p-3">
      <div className="mb-1 flex flex-wrap items-center gap-2">
        <Badge tone={meta.tone}>
          <Icon className="h-3 w-3" /> {meta.label}
        </Badge>
        {item.channel === 'sms' && (
          <Badge tone={item.provider === 'local' ? 'blue' : 'default'}>
            {item.provider === 'local' ? 'Local' : 'Eskiz'}
          </Badge>
        )}
        {item.audience && <Badge>{item.audience}</Badge>}
        <span className="ml-auto font-mono text-xs text-slate-400">{formatDate(item.createdAt)}</span>
      </div>
      {item.title && <p className="text-sm font-semibold text-slate-800">{item.title}</p>}
      <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{item.text}</p>
      <div className="mt-1.5 flex items-center justify-between text-xs text-slate-400">
        <span className="font-mono">
          {item.sentCount}/{item.recipientCount} yuborildi
        </span>
        {item.channel === 'push' && (item.targetCount ?? 0) > 0 && (
          <span className="font-mono text-emerald-600">
            {item.confirmedCount}/{item.targetCount} tasdiqladi
          </span>
        )}
      </div>

      {expandable && (
        <button
          type="button"
          onClick={onToggle}
          className="mt-2 flex w-full items-center gap-1.5 rounded-md bg-slate-50 px-2 py-1.5 text-xs font-semibold text-slate-600 transition-colors hover:bg-slate-100"
        >
          {item.channel === 'sms' ? 'Raqamlar va holat' : 'Kim tasdiqlagani'}
          <ChevronDown className={cn('ml-auto h-3.5 w-3.5 transition-transform', open && 'rotate-180')} />
        </button>
      )}

      {open && expandable && <RowDetail item={item} />}
    </div>
  )
}

/** SMS loglari yoki Push tasdiqlari (ochilganda yuklanadi). */
function RowDetail({ item }: { item: HistItem }) {
  const [smsLogs, setSmsLogs] = useState<SmsLog[] | null>(null)
  const [pushConfs, setPushConfs] = useState<PushConfirmation[] | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let alive = true
    setLoading(true)
    if (item.channel === 'sms') {
      getSmsLogs(item.id)
        .then((d) => alive && setSmsLogs(d))
        .catch(() => alive && setSmsLogs([]))
        .finally(() => alive && setLoading(false))
    } else {
      getPushConfirmations(item.id)
        .then((d) => alive && setPushConfs(d))
        .catch(() => alive && setPushConfs([]))
        .finally(() => alive && setLoading(false))
    }
    return () => {
      alive = false
    }
  }, [item.id, item.channel])

  const empty = useMemo(
    () => (item.channel === 'sms' ? (smsLogs?.length ?? 0) === 0 : (pushConfs?.length ?? 0) === 0),
    [item.channel, smsLogs, pushConfs],
  )

  return (
    <div className="mt-2 space-y-1 border-t border-slate-100 pt-2">
      {loading ? (
        <p className="text-xs text-slate-400">Yuklanmoqda...</p>
      ) : empty ? (
        <p className="text-xs text-slate-400">Yozuv yo'q</p>
      ) : item.channel === 'sms' ? (
        smsLogs!.map((l) => {
          const si = statusInfo(l.status)
          return (
            <div key={l.id} className="flex items-center justify-between gap-2 text-xs">
              <span className="min-w-0 truncate text-slate-600">
                {l.recipientName}
                <span className="ml-1 font-mono text-slate-400">{l.phoneNumber}</span>
              </span>
              <Badge tone={si.tone}>{si.label}</Badge>
            </div>
          )
        })
      ) : (
        pushConfs!.map((c, i) => (
          <div key={i} className="flex items-center justify-between gap-2 text-xs">
            <span className="min-w-0 truncate text-slate-600">
              {c.name}
              {c.group ? ` · ${c.group}` : ''}
            </span>
            {c.confirmed ? (
              <span className="inline-flex shrink-0 items-center gap-1 font-medium text-emerald-600">
                <Check className="h-3 w-3" /> Tasdiqladi
              </span>
            ) : (
              <span className="shrink-0 text-slate-400">kutilmoqda</span>
            )}
          </div>
        ))
      )}
    </div>
  )
}
