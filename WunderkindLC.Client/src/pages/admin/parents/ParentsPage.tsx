import { Fragment, useEffect, useMemo, useState } from 'react'
import { Search, Smartphone, CheckCircle2, Circle, ChevronDown, X } from 'lucide-react'
import type { ParentRow } from '@/types'
import { getParents } from '@/api/services/parents'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { cn, formatDateTime } from '@/lib/utils'

type ActivationFilter = 'all' | 'activated' | 'inactive'

/** Hozirgi vaqtdan farqi (masalan "2 kun oldin"). */
function timeAgo(iso: string | null): string {
  if (!iso) return ''
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const diff = Date.now() - d.getTime()
  const m = Math.floor(diff / 60000)
  if (m < 1) return 'hozir'
  if (m < 60) return `${m} daqiqa oldin`
  const h = Math.floor(m / 60)
  if (h < 24) return `${h} soat oldin`
  const days = Math.floor(h / 24)
  if (days < 30) return `${days} kun oldin`
  const months = Math.floor(days / 30)
  if (months < 12) return `${months} oy oldin`
  const years = Math.floor(months / 12)
  return `${years} yil oldin`
}

/**
 * Admin "Ilova → Ota-onalar" sahifasi. Telefon bo'yicha guruhlangan ota-onalar:
 * ilova aktivlashtirilganmi, oxirgi marta qachon kirgan, farzandlari ro'yxati.
 */
export function ParentsPage() {
  const [rows, setRows] = useState<ParentRow[]>([])
  const [loading, setLoading] = useState(true)
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<ActivationFilter>('all')
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  useEffect(() => {
    getParents()
      .then(setRows)
      .finally(() => setLoading(false))
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    return rows.filter((r) => {
      const matchSearch =
        !q ||
        r.fullName.toLowerCase().includes(q) ||
        r.phone.toLowerCase().includes(q) ||
        r.children.some((c) => c.fullName.toLowerCase().includes(q))
      const matchActivation =
        filter === 'all' ||
        (filter === 'activated' ? r.isActivated : !r.isActivated)
      return matchSearch && matchActivation
    })
  }, [rows, search, filter])

  // Statistika: jami / aktivlashtirilgan / aktivlashtirilmagan
  const stats = useMemo(() => {
    const activated = rows.filter((r) => r.isActivated).length
    return { total: rows.length, activated, inactive: rows.length - activated }
  }, [rows])

  const toggleExpand = (key: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(key)) next.delete(key)
      else next.add(key)
      return next
    })

  // Filtrlar standart holatdan farq qiladimi — "Tozalash" tugmasi shunda ko'rinadi.
  const filtersActive = search !== '' || filter !== 'all'
  const clearFilters = () => {
    setSearch('')
    setFilter('all')
  }

  return (
    <div>
      <PageHeader
        title="Ota-onalar"
        sub="Ilova foydalanuvchilari (ota-onalar) — telefon raqami bo'yicha guruhlangan"
      />

      {/* Statistik kartochkalar */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard
          label="Jami ota-onalar"
          value={stats.total}
          icon={Smartphone}
          iconBg="bg-slate-100"
          iconColor="text-slate-600"
        />
        <StatCard
          label="Ilovani aktivlashtirgan"
          value={stats.activated}
          icon={CheckCircle2}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
        <StatCard
          label="Hali aktivlashtirmagan"
          value={stats.inactive}
          icon={Circle}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
        />
      </div>

      <Card tight>
        {/* Filtrlar */}
        <div className="flex flex-wrap items-center gap-3 border-b border-slate-100 p-4">
          <div className="search-inline flex-1">
            <Search className="h-4 w-4 text-slate-400" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Ota-ona, telefon yoki farzand nomi..."
            />
          </div>
          <div className="tabs">
            {(['all', 'activated', 'inactive'] as ActivationFilter[]).map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setFilter(f)}
                className={cn('tab', f === filter && 'active')}
              >
                {f === 'all' ? 'Hammasi' : f === 'activated' ? 'Aktiv' : 'Aktiv emas'}
              </button>
            ))}
          </div>
          {filtersActive && (
            <button
              type="button"
              onClick={clearFilters}
              className="inline-flex items-center gap-1.5 rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-500 transition-colors hover:bg-slate-50 hover:text-slate-700"
              title="Barcha filtrlarni tozalash"
            >
              <X className="h-4 w-4" /> Filtrni tozalash
            </button>
          )}
        </div>

        {/* Jadval */}
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : filtered.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">
            {rows.length === 0 ? "Ota-onalar topilmadi (o'quvchilar hali kiritilmagan)" : "Filtrga mos natija yo'q"}
          </p>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th className="w-8"></th>
                  <th>Ota-ona F.I.SH</th>
                  <th>Telefon</th>
                  <th>Farzandlar</th>
                  <th>Holat</th>
                  <th>Qurilma</th>
                  <th>Aktivlashtirilgan</th>
                  <th>Oxirgi kirish</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((r, i) => {
                  const key = `${r.phone}-${i}`
                  const isOpen = expanded.has(key)
                  return (
                    <Fragment key={key}>
                      <tr
                        className="cursor-pointer"
                        onClick={() => toggleExpand(key)}
                      >
                        <td className="text-slate-400">
                          <ChevronDown
                            className={cn(
                              'h-4 w-4 transition-transform',
                              isOpen ? 'rotate-180' : '',
                            )}
                          />
                        </td>
                        <td className="font-medium text-slate-800">
                          {r.fullName || <span className="text-slate-400">—</span>}
                        </td>
                        <td className="font-mono text-slate-600">{r.phone || '—'}</td>
                        <td>
                          <Badge tone="default">
                            <span className="font-mono">{r.childrenCount}</span>{' '}
                            farzand
                          </Badge>
                        </td>
                        <td>
                          {r.isActivated ? (
                            <Badge tone="green">
                              <CheckCircle2 className="h-3.5 w-3.5" /> Aktiv
                            </Badge>
                          ) : (
                            <Badge tone="amber">
                              <Circle className="h-3.5 w-3.5" /> Kirmagan
                            </Badge>
                          )}
                        </td>
                        <td className="text-slate-600">
                          {r.deviceName ? (
                            <span className="inline-flex items-center gap-1">
                              <Smartphone className="h-3.5 w-3.5 text-slate-400" />
                              {r.deviceName}
                              {r.platform && (
                                <span className="text-[11px] text-slate-400">({r.platform})</span>
                              )}
                            </span>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="font-mono text-slate-600">
                          {formatDateTime(r.activatedAt)}
                        </td>
                        <td>
                          <div className="font-mono text-slate-700">{formatDateTime(r.lastSeenAt)}</div>
                          {r.lastSeenAt && (
                            <div className="text-[11px] text-slate-400">{timeAgo(r.lastSeenAt)}</div>
                          )}
                        </td>
                      </tr>
                      {isOpen && (
                        <tr className="bg-slate-50/40">
                          <td colSpan={8} className="px-4 py-3">
                            <div className="rounded-lg border border-slate-200 bg-white">
                              <table className="table">
                                <thead>
                                  <tr>
                                    <th>Farzand</th>
                                    <th>Guruh</th>
                                    <th>Birinchi kirish</th>
                                    <th>Oxirgi kirish</th>
                                    <th>Qurilma</th>
                                    <th>App ID</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  {r.children.map((c) => (
                                    <tr key={c.studentId}>
                                      <td className="font-medium text-slate-700">{c.fullName}</td>
                                      <td className="text-slate-600">{c.className}</td>
                                      <td className="font-mono text-slate-600">{formatDateTime(c.firstLoginAt)}</td>
                                      <td className="font-mono text-slate-600">{formatDateTime(c.lastLoginAt)}</td>
                                      <td className="text-slate-600">
                                        {c.deviceName || '—'}
                                        {c.platform ? ` (${c.platform})` : ''}
                                      </td>
                                      <td className="text-slate-500">
                                        <code className="font-mono text-xs">{c.appId || '—'}</code>
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                              </table>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
