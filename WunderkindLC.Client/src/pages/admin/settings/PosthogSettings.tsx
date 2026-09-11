import { useEffect, useState } from 'react'
import {
  Activity,
  BarChart3,
  CheckCircle2,
  CreditCard,
  ExternalLink,
  LogIn,
  MessageSquare,
  RefreshCw,
  Send,
  ThumbsUp,
  Users,
  XCircle,
  Zap,
} from 'lucide-react'
import posthog from '@/lib/posthog'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'

/* ─── Tiplar ────────────────────────────────────────────────────── */
interface EventRow {
  event: string
  label: string
  icon: React.ReactNode
  color: string
  count: number
  lastSeen: Date | null
}

interface SessionInfo {
  distinctId: string
  sessionId: string | null
  isIdentified: boolean
}

/* ─── Eventlar ro'yxati (loyihada kuzatiladiganlar) ─────────────── */
const TRACKED_EVENTS: Omit<EventRow, 'count' | 'lastSeen'>[] = [
  {
    event: 'user_logged_in',
    label: 'Kirish (login)',
    icon: <LogIn className="h-4 w-4" />,
    color: 'text-blue-500',
  },
  {
    event: 'student_payment_recorded',
    label: "To'lov qayd etildi",
    icon: <CreditCard className="h-4 w-4" />,
    color: 'text-emerald-500',
  },
  {
    event: 'payment_refunded',
    label: "To'lov qaytarildi",
    icon: <RefreshCw className="h-4 w-4" />,
    color: 'text-amber-500',
  },
  {
    event: 'message_campaign_sent',
    label: 'Xabar kampaniyasi',
    icon: <Send className="h-4 w-4" />,
    color: 'text-violet-500',
  },
  {
    event: 'class_completed_and_transferred',
    label: 'Dars yakunlandi',
    icon: <CheckCircle2 className="h-4 w-4" />,
    color: 'text-teal-500',
  },
  {
    event: 'public_lead_form_submitted',
    label: 'Lid forma yuborildi',
    icon: <Users className="h-4 w-4" />,
    color: 'text-pink-500',
  },
  {
    event: 'student_feedback_submitted',
    label: "O'quvchi shikoyati",
    icon: <ThumbsUp className="h-4 w-4" />,
    color: 'text-orange-500',
  },
]

/* ─── Local storage'dan event hisobini o'qish ───────────────────── */
const LS_KEY = 'ph_event_counts'

function readCounts(): Record<string, { count: number; lastSeen: string }> {
  try {
    return JSON.parse(localStorage.getItem(LS_KEY) ?? '{}')
  } catch {
    return {}
  }
}

function writeCounts(data: Record<string, { count: number; lastSeen: string }>) {
  try {
    localStorage.setItem(LS_KEY, JSON.stringify(data))
  } catch {
    /* ignore */
  }
}

/* ─── PostHog patch: har capture'da mahalliy hisob yangilanadi ──── */
let patchApplied = false
function patchPosthog() {
  if (patchApplied) return
  patchApplied = true
  const original = posthog.capture.bind(posthog)
  posthog.capture = (event: string, props?: object, opts?: object) => {
    const stored = readCounts()
    const prev = stored[event] ?? { count: 0, lastSeen: '' }
    stored[event] = { count: prev.count + 1, lastSeen: new Date().toISOString() }
    writeCounts(stored)
    window.dispatchEvent(new CustomEvent('ph:event', { detail: { event } }))
    return original(event, props, opts)
  }
}

/* ─── Asosiy komponent ──────────────────────────────────────────── */
export function PosthogSettings() {
  const [rows, setRows] = useState<EventRow[]>([])
  const [session, setSession] = useState<SessionInfo | null>(null)
  const [isConfigured, setIsConfigured] = useState(false)
  const [totalEvents, setTotalEvents] = useState(0)
  const [lastRefresh, setLastRefresh] = useState(new Date())

  const host: string = import.meta.env.VITE_POSTHOG_HOST ?? ''
  const key: string = import.meta.env.VITE_POSTHOG_KEY ?? ''

  function loadData() {
    const configured = !!(key && host)
    setIsConfigured(configured)

    if (configured) {
      patchPosthog()

      // Session ma'lumotlari
      const distinctId = posthog.get_distinct_id?.() ?? '—'
      const sessionId =
        (posthog as unknown as { get_session_id?: () => string }).get_session_id?.() ?? null
      const isIdentified = distinctId.length > 0 && !distinctId.startsWith('$')
      setSession({ distinctId, sessionId, isIdentified })

      // Event hisobi
      const stored = readCounts()
      let total = 0
      const built = TRACKED_EVENTS.map((e) => {
        const s = stored[e.event]
        total += s?.count ?? 0
        return {
          ...e,
          count: s?.count ?? 0,
          lastSeen: s?.lastSeen ? new Date(s.lastSeen) : null,
        }
      })
      setRows(built)
      setTotalEvents(total)
    }

    setLastRefresh(new Date())
  }

  useEffect(() => {
    loadData()
    // Real-vaqt yangilanish: har capture'da
    const handler = () => loadData()
    window.addEventListener('ph:event', handler)
    return () => window.removeEventListener('ph:event', handler)
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const projectUrl = host && key ? `${host}/project` : null

  return (
    <div className="space-y-6">
      {/* ── Status card ── */}
      <Card
        title={
          <span className="flex flex-wrap items-center gap-2">
            <BarChart3 className="h-4 w-4 text-[#f54e00]" />
            PostHog Analitika
            {isConfigured ? (
              <Badge tone="green">
                <CheckCircle2 className="h-3.5 w-3.5" /> Faol
              </Badge>
            ) : (
              <Badge tone="default">
                <XCircle className="h-3.5 w-3.5" /> Sozlanmagan
              </Badge>
            )}
          </span>
        }
        actions={
          <div className="flex items-center gap-2">
            <span className="text-xs text-slate-400">
              {lastRefresh.toLocaleTimeString('uz-UZ')}
            </span>
            <Button variant="ghost" onClick={loadData} className="p-1.5">
              <RefreshCw className="h-3.5 w-3.5" />
            </Button>
          </div>
        }
      >
        <p className="mb-4 text-sm text-slate-400">
          PostHog — foydalanuvchi harakatlari (login, to'lov, xabar va boshqalar) ni real vaqtda
          kuzatuvchi analytics platforma. Barcha eventlar brauzerdan to'g'ridan-to'g'ri PostHog
          serverlariga yuboriladi — sizning serveringizga ta'siri yo'q.
        </p>

        {/* Env o'zgaruvchilari */}
        <div className="mb-4 grid gap-3 sm:grid-cols-2">
          <EnvRow
            label="VITE_POSTHOG_KEY"
            value={key}
            masked
            ok={!!key}
          />
          <EnvRow
            label="VITE_POSTHOG_HOST"
            value={host}
            ok={!!host}
          />
        </div>

        {/* Tashqi link */}
        {projectUrl && (
          <a
            href={`${host}/project`}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1.5 rounded-lg bg-[#f54e00] px-4 py-2 text-sm font-medium text-white shadow-sm transition-opacity hover:opacity-90"
          >
            <ExternalLink className="h-4 w-4" />
            PostHog dashboardini ochish
          </a>
        )}

        {!isConfigured && (
          <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-700">
            <p className="mb-2 font-medium">Sozlash uchun:</p>
            <ol className="list-decimal space-y-1 pl-4">
              <li>
                <a
                  href="https://posthog.com"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="underline"
                >
                  posthog.com
                </a>{' '}
                ga kiring → yangi loyiha yarating
              </li>
              <li>Project Settings → Project API Key ni oling</li>
              <li>
                Serverdagi{' '}
                <code className="rounded bg-amber-100 px-1">.env</code> fayliga qo'shing:
              </li>
            </ol>
            <pre className="mt-2 rounded bg-slate-900 p-3 text-xs text-slate-100">
              {`VITE_POSTHOG_KEY=phc_your_key_here\nVITE_POSTHOG_HOST=https://us.i.posthog.com`}
            </pre>
            <p className="mt-2 text-xs text-amber-600">
              Keyin Docker qaytadan build qiling:{' '}
              <code className="rounded bg-amber-100 px-1">docker compose up -d --build app</code>
            </p>
          </div>
        )}
      </Card>

      {/* ── Joriy sessiya ── */}
      {isConfigured && session && (
        <Card
          title={
            <span className="flex items-center gap-2">
              <Activity className="h-4 w-4 text-brand-600" />
              Joriy brauzer sessiyasi
            </span>
          }
        >
          <div className="grid gap-3 sm:grid-cols-2">
            <InfoRow
              label="Distinct ID"
              value={session.distinctId}
              mono
              badge={
                session.isIdentified ? (
                  <Badge tone="green">Identifikatsiya qilingan</Badge>
                ) : (
                  <Badge tone="default">Anonim</Badge>
                )
              }
            />
            {session.sessionId && (
              <InfoRow label="Session ID" value={session.sessionId} mono />
            )}
          </div>
        </Card>
      )}

      {/* ── Event hisobi ── */}
      {isConfigured && (
        <Card
          title={
            <span className="flex items-center gap-2">
              <Zap className="h-4 w-4 text-brand-600" />
              Kuzatilayotgan eventlar
              <Badge tone="green">{totalEvents} ta jami</Badge>
            </span>
          }
        >
          <p className="mb-4 text-xs text-slate-400">
            Quyidagi hisoblar <b>faqat shu brauzer sessiyasida</b> amalga oshirilgan eventlarni
            ko'rsatadi. Barcha foydalanuvchilarning to'liq statistikasi PostHog dashboardida.
          </p>
          <div className="divide-y divide-slate-100">
            {rows.map((row) => (
              <div
                key={row.event}
                className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0"
              >
                <div className="flex items-center gap-3">
                  <span className={row.color}>{row.icon}</span>
                  <div>
                    <p className="text-sm font-medium text-slate-700">{row.label}</p>
                    <p className="font-mono text-xs text-slate-400">{row.event}</p>
                  </div>
                </div>
                <div className="text-right">
                  <p className="text-lg font-bold text-slate-800">{row.count}</p>
                  {row.lastSeen && (
                    <p className="text-xs text-slate-400">
                      {row.lastSeen.toLocaleTimeString('uz-UZ')}
                    </p>
                  )}
                </div>
              </div>
            ))}
          </div>

          {totalEvents === 0 && (
            <div className="mt-2 flex items-center gap-2 rounded-lg bg-slate-50 p-4 text-sm text-slate-500">
              <MessageSquare className="h-4 w-4 shrink-0" />
              Hali bu brauzerda hech qanday event qayd etilmagan. Login qiling, to'lov kiriting yoki
              boshqa amal bajaring — bu yerda ko'rinadi.
            </div>
          )}
        </Card>
      )}

      {/* ── Qanday ishlaydi ── */}
      <Card
        title={
          <span className="flex items-center gap-2">
            <MessageSquare className="h-4 w-4 text-slate-400" />
            Qanday ishlaydi?
          </span>
        }
      >
        <ol className="space-y-2 text-sm text-slate-600">
          <li className="flex gap-2">
            <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-brand-100 text-xs font-bold text-brand-700">
              1
            </span>
            Foydalanuvchi login bo'lganda PostHog uning <b>ID, ism va roli</b> bilan
            identifikatsiya qiladi.
          </li>
          <li className="flex gap-2">
            <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-brand-100 text-xs font-bold text-brand-700">
              2
            </span>
            Har bir muhim harakatda (to'lov, xabar, dars) brauzer PostHog serverlariga{' '}
            <b>kichik JSON so'rov</b> yuboradi — sizning serveringizga ta'siri yo'q.
          </li>
          <li className="flex gap-2">
            <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-brand-100 text-xs font-bold text-brand-700">
              3
            </span>
            Logout yoki token muddati o'tganda PostHog <b>sessiyani tozalaydi</b> — keyingi
            foydalanuvchi oldinginikiga aralashmaydi.
          </li>
          <li className="flex gap-2">
            <span className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-brand-100 text-xs font-bold text-brand-700">
              4
            </span>
            Barcha <b>so'ralmagan JS xatolari</b> ham avtomatik yoziladi (
            <code className="rounded bg-slate-100 px-1 text-xs">capture_unhandled_errors</code>).
          </li>
        </ol>
      </Card>
    </div>
  )
}

/* ─── Yordamchi komponentlar ────────────────────────────────────── */
function EnvRow({
  label,
  value,
  masked = false,
  ok,
}: {
  label: string
  value: string
  masked?: boolean
  ok: boolean
}) {
  const display = masked && value ? `${value.slice(0, 8)}${'•'.repeat(12)}` : value || '—'
  return (
    <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
      <p className="mb-1 font-mono text-xs text-slate-500">{label}</p>
      <div className="flex items-center gap-2">
        {ok ? (
          <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-emerald-500" />
        ) : (
          <XCircle className="h-3.5 w-3.5 shrink-0 text-slate-400" />
        )}
        <span className="truncate font-mono text-xs text-slate-700">{display}</span>
      </div>
    </div>
  )
}

function InfoRow({
  label,
  value,
  mono = false,
  badge,
}: {
  label: string
  value: string
  mono?: boolean
  badge?: React.ReactNode
}) {
  return (
    <div className="rounded-lg border border-slate-200 bg-slate-50 p-3">
      <p className="mb-1 text-xs text-slate-500">{label}</p>
      <div className="flex flex-wrap items-center gap-2">
        <span
          className={`truncate text-sm text-slate-700 ${mono ? 'font-mono text-xs' : 'font-medium'}`}
        >
          {value}
        </span>
        {badge}
      </div>
    </div>
  )
}
