import { useCallback, useEffect, useState } from 'react'
import { RotateCcw, Trash2 } from 'lucide-react'
import type { ArchivedRecord } from '@/types'
import {
  getArchive,
  getArchiveCounts,
  restoreArchive,
  deleteArchive,
} from '@/api/services/archive'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { formatDate, cn } from '@/lib/utils'

/** Arxiv turlari (Uzbek yorliqlar bilan). */
const TABS: { key: string; label: string }[] = [
  { key: 'lead', label: 'Lidlar' },
  { key: 'student', label: 'Talabalar' },
  { key: 'teacher', label: "O'qituvchilar" },
  { key: 'staff', label: 'Xodimlar' },
  { key: 'group', label: 'Guruhlar' },
  { key: 'finance', label: 'Moliya' },
]

export function ArchivePage() {
  const [active, setActive] = useState('lead')
  const [counts, setCounts] = useState<Record<string, number>>({})
  const [rows, setRows] = useState<ArchivedRecord[]>([])
  const [loading, setLoading] = useState(true)
  const [busyId, setBusyId] = useState<string | null>(null)

  const loadCounts = useCallback(async () => {
    try {
      setCounts(await getArchiveCounts())
    } catch {
      /* sanog'ni yuklab bo'lmadi — e'tiborsiz qoldiriladi */
    }
  }, [])

  const loadRows = useCallback(async (type: string) => {
    setLoading(true)
    try {
      setRows(await getArchive(type))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    loadCounts()
  }, [loadCounts])

  useEffect(() => {
    loadRows(active)
  }, [active, loadRows])

  const refresh = async () => {
    await Promise.all([loadRows(active), loadCounts()])
  }

  const handleRestore = async (id: string) => {
    setBusyId(id)
    try {
      await restoreArchive(id)
      await refresh()
    } finally {
      setBusyId(null)
    }
  }

  const handleDelete = async (id: string) => {
    if (!window.confirm("Bu yozuvni butunlay o'chirishni tasdiqlaysizmi? Bu amalni qaytarib bo'lmaydi.")) return
    setBusyId(id)
    try {
      await deleteArchive(id)
      await refresh()
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div>
      <PageHeader
        title="Arxiv"
        sub="O'chirilgan yozuvlar — tiklash yoki butunlay o'chirish mumkin"
      />

      {/* Turlar bo'yicha tablar */}
      <div className="mb-4 flex flex-wrap gap-1.5">
        {TABS.map((t) => {
          const count = counts[t.key] ?? 0
          const isActive = t.key === active
          return (
            <button
              key={t.key}
              type="button"
              onClick={() => setActive(t.key)}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-lg px-3.5 py-2 text-[13px] font-semibold transition-colors',
                isActive
                  ? 'bg-brand-600 text-white'
                  : 'border border-slate-200 bg-white text-slate-700 hover:border-slate-300 hover:bg-slate-50',
              )}
            >
              {t.label}
              <span
                className={cn(
                  'inline-flex min-w-[20px] items-center justify-center rounded-full px-1.5 py-0.5 text-[11px] font-mono font-bold',
                  isActive ? 'bg-white/20 text-white' : 'bg-slate-100 text-slate-500',
                )}
              >
                {count}
              </span>
            </button>
          )
        })}
      </div>

      <Card>
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : rows.length === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">
            Bu bo'limda o'chirilgan yozuv yo'q
          </p>
        ) : (
          <div className="space-y-2">
            {rows.map((r) => (
              <div
                key={r.id}
                className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-slate-100 px-3.5 py-3 transition-colors hover:bg-slate-50"
              >
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-semibold text-slate-800">{r.title}</span>
                    {r.reason && <Badge tone="amber">{r.reason}</Badge>}
                  </div>
                  {r.subtitle && <p className="mt-0.5 text-sm text-slate-400">{r.subtitle}</p>}
                  <p className="mt-1 text-xs text-slate-400">
                    <span className="font-mono">{formatDate(r.deletedAt)}</span>
                    {r.actorName && <span> &middot; {r.actorName}</span>}
                  </p>
                </div>
                <div className="flex items-center gap-2">
                  <Button
                    variant="secondary"
                    onClick={() => handleRestore(r.id)}
                    disabled={busyId === r.id}
                  >
                    <RotateCcw className="h-4 w-4" /> Tiklash
                  </Button>
                  <button
                    type="button"
                    title="Butunlay o'chirish"
                    onClick={() => handleDelete(r.id)}
                    disabled={busyId === r.id}
                    className="rounded-lg p-2 text-slate-400 transition-colors hover:bg-red-50 hover:text-red-600 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}
