import { useEffect, useState } from 'react'
import { FileText, Inbox, Phone, Search } from 'lucide-react'
import type { ApplicationStatus, CareerStage, JobApplication, Vacancy } from '@/api/services/career'
import { getApplications } from '@/api/services/career'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDateTime } from '@/lib/utils'
import { ApplicationDetailModal } from './ApplicationDetailModal'
import { statusIcons, statusLabels, statusOrder, statusTones } from './careerLabels'

interface Props {
  stages: CareerStage[]
  vacancies: Vacancy[]
  canEdit: boolean
  canDelete: boolean
  /** Vakansiyalar tabidan "N ta ariza" bosilganda oldindan qo'yiladigan filtr. */
  vacancyFilter: string
  onVacancyFilterChange: (id: string) => void
}

/**
 * ARIZALAR — nomzodlar ro'yxati. Bosqich chiplari bilan filtrlanadi; qator bosilganda
 * tafsilot modali ochiladi (u yerda bosqich o'zgartiriladi va nomzodga xabar ketadi).
 */
export function ApplicationsTab({
  stages, vacancies, canEdit, canDelete, vacancyFilter, onVacancyFilterChange,
}: Props) {
  const [items, setItems] = useState<JobApplication[]>([])
  const [loading, setLoading] = useState(true)
  const [status, setStatus] = useState<ApplicationStatus | ''>('')
  const [q, setQ] = useState('')
  const [openId, setOpenId] = useState<string | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setLoading(true)
      getApplications({ status, vacancyId: vacancyFilter, q })
        .then(setItems)
        .catch((err) => setError(apiErrorMessage(err, "Arizalarni yuklab bo'lmadi")))
        .finally(() => setLoading(false))
    }, q ? 300 : 0)
    return () => window.clearTimeout(timer)
  }, [status, vacancyFilter, q])

  const patch = (a: JobApplication) =>
    setItems((prev) => prev.map((x) => (x.id === a.id ? { ...x, ...a } : x)))

  const stageLabel = (key: ApplicationStatus) =>
    stages.find((s) => s.key === key)?.label ?? statusLabels[key]

  return (
    <div>
      <div className="toolbar">
        <div className="left">
          <button
            type="button"
            className={cn('filter-chip', status === '' && 'active')}
            onClick={() => setStatus('')}
          >
            Barchasi
          </button>
          {statusOrder.map((key) => (
            <button
              key={key}
              type="button"
              className={cn('filter-chip', status === key && 'active')}
              onClick={() => setStatus(key)}
            >
              {statusIcons[key]} {stageLabel(key)}
            </button>
          ))}
        </div>
        <div className="right">
          <select
            className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none"
            value={vacancyFilter}
            onChange={(e) => onVacancyFilterChange(e.target.value)}
          >
            <option value="">Barcha vakansiyalar</option>
            {vacancies.map((v) => (
              <option key={v.id} value={v.id}>
                {v.title}
              </option>
            ))}
          </select>
          <div className="search-inline">
            <Search className="h-4 w-4 text-slate-400" />
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="F.I.Sh. yoki telefon..."
            />
          </div>
        </div>
      </div>

      {error && (
        <Card className="mb-3 border-red-200 bg-red-50 text-sm font-medium text-red-600">{error}</Card>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : items.length === 0 ? (
        <Card>
          <div className="state">
            <div className="state-icon">
              <Inbox className="h-6 w-6" />
            </div>
            <h4>Arizalar yo'q</h4>
            <p>Tanlangan filtrlar bo'yicha ariza topilmadi.</p>
          </div>
        </Card>
      ) : (
        <div className="space-y-2">
          {items.map((a) => (
            <button
              key={a.id}
              type="button"
              onClick={() => setOpenId(a.id)}
              className="block w-full rounded-xl border border-slate-200 bg-white p-4 text-left shadow-[var(--shadow-1)] transition-colors hover:border-brand-200 hover:bg-brand-50/30"
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-[11px] font-semibold text-slate-400">
                    #{a.number} · {formatDateTime(a.createdAt)}
                  </p>
                  <h3 className="text-[15px] font-bold text-slate-800">{a.fullName}</h3>
                  <p className="text-xs text-slate-500">{a.vacancyTitle}</p>
                </div>
                <Badge tone={statusTones[a.status]}>
                  {statusIcons[a.status]} {stageLabel(a.status)}
                </Badge>
              </div>

              <div className="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-slate-500">
                <span className="inline-flex items-center gap-1">
                  <Phone className="h-3.5 w-3.5" /> {a.phone}
                </span>
                {a.cvUrl && (
                  <span className="inline-flex items-center gap-1 text-brand-600">
                    <FileText className="h-3.5 w-3.5" /> CV biriktirilgan
                  </span>
                )}
                {a.statusNote && <span className="truncate">📝 {a.statusNote}</span>}
              </div>
            </button>
          ))}
        </div>
      )}

      <ApplicationDetailModal
        open={openId != null}
        applicationId={openId}
        stages={stages}
        canEdit={canEdit}
        canDelete={canDelete}
        onClose={() => setOpenId(null)}
        onChanged={patch}
        onDeleted={(id) => setItems((prev) => prev.filter((x) => x.id !== id))}
      />
    </div>
  )
}
