import { useEffect, useState } from 'react'
import { ChevronDown, Clock } from 'lucide-react'
import type { AuditLog } from '@/types'
import { getAuditLogs, type AuditFilters } from '@/api/services/audit'
import { Loader } from '@/components/ui/Loader'
import { formatDateTime, cn } from '@/lib/utils'
import { auditActionLabel, snapshotFields } from './auditFields'

interface Props {
  filters: AuditFilters
  /** Bo'sh bo'lganda ko'rsatiladigan matn */
  emptyLabel?: string
  /**
   * Har qatorda BO'LIM yorlig'ini ko'rsatish (umumiy tarix sahifasi uchun). O'quvchi/guruh
   * sahifasidagi tarixda kerak emas — u yerda bo'lim baribir ma'lum.
   */
  sectionLabels?: Record<string, string>
  /** Yuklab bo'lingach chaqiriladi — sahifa "Ko'proq" tugmasini shu songa qarab ko'rsatadi. */
  onLoaded?: (count: number) => void
}

/** before/after snapshotlarini o'qiladigan tafsilotga aylantiradi (yorliq/format — `auditFields.ts`). */
function SnapshotDetail({ before, after }: { before?: string; after?: string }) {
  const fields = snapshotFields(before, after)
  if (fields.length === 0) return null

  return (
    <dl className="mt-2 space-y-1 rounded-lg bg-slate-50 px-3 py-2 text-xs">
      {fields.map((f) => (
        <div key={f.key} className="flex justify-between gap-3">
          <dt className="text-slate-400">{f.label}</dt>
          <dd className="text-right text-slate-600">
            {f.changed ? (
              <>
                <span className="text-slate-400 line-through">{f.before}</span>
                {' → '}
                <span className="font-medium text-slate-700">{f.after}</span>
              </>
            ) : (
              f.after
            )}
          </dd>
        </div>
      ))}
    </dl>
  )
}

export function AuditHistoryList({
  filters,
  emptyLabel = "O'zgarishlar tarixi yo'q",
  sectionLabels,
  onLoaded,
}: Props) {
  const [logs, setLogs] = useState<AuditLog[]>([])
  const [loading, setLoading] = useState(true)
  const [openId, setOpenId] = useState<string | null>(null)

  const key = JSON.stringify(filters)
  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getAuditLogs(filters)
      .then((data) => {
        if (!active) return
        setLogs(data)
        onLoaded?.(data.length)
      })
      .finally(() => {
        if (active) setLoading(false)
      })
    return () => {
      active = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- filtrlarni stabil JSON kalit orqali kuzatamiz
  }, [key])

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (logs.length === 0)
    return (
      <div className="flex items-center gap-2 py-6 text-sm text-slate-400">
        <Clock className="h-4 w-4" /> {emptyLabel}
      </div>
    )

  return (
    <ul className="space-y-2">
      {logs.map((log) => {
        const cfg = auditActionLabel(log.action, log.summary)
        const hasDetail = !!(log.before || log.after)
        const open = openId === log.id
        return (
          <li key={log.id} className="rounded-lg border border-slate-100 px-3 py-2">
            <div className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="flex flex-wrap items-center gap-2">
                  <span className={cn('rounded-md px-2 py-0.5 text-xs font-medium', cfg.cls)}>
                    {cfg.label}
                  </span>
                  {sectionLabels && (
                    <span className="rounded-md bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-500">
                      {sectionLabels[log.section ?? 'other'] ?? log.entityType}
                    </span>
                  )}
                  <span className="text-sm text-slate-700">{log.summary}</span>
                </div>
                <p className="mt-0.5 text-xs text-slate-400">
                  {formatDateTime(log.timestamp)} · {log.actorName || 'Tizim'}
                </p>
              </div>
              {hasDetail && (
                <button
                  type="button"
                  onClick={() => setOpenId(open ? null : log.id)}
                  className="shrink-0 rounded-md p-1 text-slate-400 transition-colors hover:bg-slate-100 hover:text-slate-600"
                  title="Tafsilot"
                >
                  <ChevronDown className={cn('h-4 w-4 transition-transform', open && 'rotate-180')} />
                </button>
              )}
            </div>
            {open && hasDetail && <SnapshotDetail before={log.before} after={log.after} />}
          </li>
        )
      })}
    </ul>
  )
}
