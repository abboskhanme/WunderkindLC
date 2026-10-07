import { useEffect, useState } from 'react'
import {
  Archive, ArchiveRestore, Briefcase, MapPin, Pencil, Plus, Trash2, Users, Wallet, Clock,
} from 'lucide-react'
import type { Vacancy, VacancyStatus } from '@/api/services/career'
import {
  archiveVacancy, deleteVacancy, getVacancies, restoreVacancy, setVacancyActive,
} from '@/api/services/career'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { VacancyFormModal } from './VacancyFormModal'
import { employmentLabels, isExpired, salaryText } from './careerLabels'

interface Props {
  canCreate: boolean
  canEdit: boolean
  canDelete: boolean
  /** Vakansiyaga tushgan arizalarni ko'rish uchun "Arizalar" tabiga o'tish. */
  onOpenApplications: (vacancyId: string) => void
}

/** `current` — joriy (yoqilgan + o'chirilgan), `archived` — arxiv, `''` — hammasi. */
type Filter = '' | 'current' | 'archived'

const FILTER_STATUSES: Record<Filter, VacancyStatus[] | undefined> = {
  current: ['active', 'paused'],
  archived: ['archived'],
  '': undefined,
}

/**
 * VAKANSIYALAR ro'yxati — yaratish, tahrirlash, BITTA TUGMA bilan YOQISH/O'CHIRISH (ON/OFF),
 * ARXIVLASH/tiklash.
 * - O'chirilgan (OFF) vakansiya nomzodlar ilovasida (Telegram bot Mini App'i va `/vakansiya`
 *   sahifasi) darhol ko'rinmay qoladi va ariza qabul qilmaydi, lekin ro'yxatda joyida turadi.
 * - Arxivlangan vakansiya ham ko'rinmaydi, unga tushgan arizalar saqlanadi; shuning uchun ariza
 *   tushgan vakansiyani o'chirish o'rniga arxivlash taklif qilinadi.
 */
export function VacancyListTab({ canCreate, canEdit, canDelete, onOpenApplications }: Props) {
  const [items, setItems] = useState<Vacancy[]>([])
  const [loading, setLoading] = useState(true)
  const [filter, setFilter] = useState<Filter>('current')
  const [togglingId, setTogglingId] = useState<string | null>(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState<Vacancy | null>(null)
  const [error, setError] = useState('')

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getVacancies(FILTER_STATUSES[filter])
      .then(setItems)
      .catch((err) => setError(apiErrorMessage(err, "Vakansiyalarni yuklab bo'lmadi")))
      .finally(() => setLoading(false))
  }, [filter])

  const upsert = (v: Vacancy) =>
    setItems((prev) => {
      const exists = prev.some((x) => x.id === v.id)
      const next = exists ? prev.map((x) => (x.id === v.id ? v : x)) : [...prev, v]
      // Filtrga to'g'ri kelmay qolgan yozuv ro'yxatdan chiqib ketsin.
      const allowed = FILTER_STATUSES[filter]
      return next
        .filter((x) => !allowed || allowed.includes(x.status))
        .sort((a, b) => a.order - b.order || b.createdAt.localeCompare(a.createdAt))
    })

  /** ON/OFF — server javobidan keyin yangilanadi (optimistik EMAS: xato bo'lsa holat o'zgarmaydi). */
  const doToggle = async (v: Vacancy) => {
    if (togglingId) return
    setTogglingId(v.id)
    setError('')
    try {
      upsert(await setVacancyActive(v.id, v.status !== 'active'))
    } catch (err) {
      setError(apiErrorMessage(err, "Vakansiya holatini o'zgartirib bo'lmadi"))
    } finally {
      setTogglingId(null)
    }
  }

  const doArchive = async (v: Vacancy) => {
    try {
      upsert(await archiveVacancy(v.id))
    } catch (err) {
      setError(apiErrorMessage(err, "Arxivlab bo'lmadi"))
    }
  }

  const doRestore = async (v: Vacancy) => {
    try {
      upsert(await restoreVacancy(v.id))
    } catch (err) {
      setError(apiErrorMessage(err, "Tiklab bo'lmadi"))
    }
  }

  const doDelete = async (v: Vacancy) => {
    if (!window.confirm(`"${v.title}" vakansiyasi butunlay o'chirilsinmi?`)) return
    try {
      await deleteVacancy(v.id)
      setItems((prev) => prev.filter((x) => x.id !== v.id))
    } catch (err) {
      setError(apiErrorMessage(err, "O'chirib bo'lmadi"))
    }
  }

  return (
    <div>
      <div className="toolbar">
        <div className="left">
          {([
            ['current', 'Joriy'],
            ['archived', 'Arxiv'],
            ['', 'Hammasi'],
          ] as [Filter, string][]).map(([key, label]) => (
            <button
              key={key || 'all'}
              type="button"
              className={cn('filter-chip', filter === key && 'active')}
              onClick={() => setFilter(key)}
            >
              {label}
            </button>
          ))}
        </div>
        {canCreate && (
          <div className="right">
            <Button
              onClick={() => {
                setEditing(null)
                setModalOpen(true)
              }}
            >
              <Plus className="h-4 w-4" /> Yangi vakansiya
            </Button>
          </div>
        )}
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
              <Briefcase className="h-6 w-6" />
            </div>
            <h4>Vakansiyalar yo'q</h4>
            <p>
              {filter === 'archived'
                ? 'Arxivlangan vakansiya yo\'q.'
                : "Yangi vakansiya qo'shing — u darhol nomzodlar ilovasida ko'rinadi."}
            </p>
          </div>
        </Card>
      ) : (
        <div className="grid gap-3 lg:grid-cols-2">
          {items.map((v) => {
            const expired = isExpired(v.deadline)
            return (
              <Card key={v.id} className="flex flex-col gap-2">
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div className="min-w-0">
                    <h3 className="text-[15px] font-bold text-slate-800">{v.title}</h3>
                    {v.department && <p className="text-xs text-slate-400">{v.department}</p>}
                  </div>
                  <div className="flex flex-wrap items-center gap-1.5">
                    {v.status === 'archived' ? (
                      <Badge tone="default">Arxivda</Badge>
                    ) : (
                      <>
                        {expired && <Badge tone="amber">Muddati tugagan</Badge>}
                        <ActiveSwitch
                          on={v.status === 'active'}
                          busy={togglingId === v.id}
                          disabled={!canEdit}
                          onToggle={() => doToggle(v)}
                        />
                      </>
                    )}
                  </div>
                </div>

                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
                  <span className="inline-flex items-center gap-1">
                    <Wallet className="h-3.5 w-3.5" /> {salaryText(v)}
                  </span>
                  <span className="inline-flex items-center gap-1">
                    <Clock className="h-3.5 w-3.5" /> {employmentLabels[v.employmentType]}
                  </span>
                  {v.location && (
                    <span className="inline-flex items-center gap-1">
                      <MapPin className="h-3.5 w-3.5" /> {v.location}
                    </span>
                  )}
                  {v.deadline && (
                    <span className="inline-flex items-center gap-1">
                      📅 {formatDate(v.deadline)} gacha
                    </span>
                  )}
                </div>

                {v.description && (
                  <p className="line-clamp-2 text-sm text-slate-500">{v.description}</p>
                )}

                <div className="mt-1 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-2">
                  <button
                    type="button"
                    className="inline-flex items-center gap-1.5 text-[13px] font-semibold text-brand-600 hover:underline"
                    onClick={() => onOpenApplications(v.id)}
                  >
                    <Users className="h-4 w-4" />
                    {v.applicationCount} ta ariza
                    {v.newCount > 0 && (
                      <span className="rounded-full bg-red-500 px-1.5 py-px text-[11px] font-bold text-white">
                        {v.newCount} yangi
                      </span>
                    )}
                  </button>

                  <div className="flex flex-wrap items-center gap-1.5">
                    {canEdit && (
                      <Button
                        variant="secondary"
                        onClick={() => {
                          setEditing(v)
                          setModalOpen(true)
                        }}
                      >
                        <Pencil className="h-3.5 w-3.5" /> Tahrir
                      </Button>
                    )}
                    {canEdit &&
                      (v.status !== 'archived' ? (
                        <Button variant="secondary" onClick={() => doArchive(v)}>
                          <Archive className="h-3.5 w-3.5" /> Arxivlash
                        </Button>
                      ) : (
                        <Button variant="secondary" onClick={() => doRestore(v)}>
                          <ArchiveRestore className="h-3.5 w-3.5" /> Tiklash
                        </Button>
                      ))}
                    {canDelete && v.applicationCount === 0 && (
                      <Button variant="danger" onClick={() => doDelete(v)}>
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    )}
                  </div>
                </div>
              </Card>
            )
          })}
        </div>
      )}

      <VacancyFormModal
        open={modalOpen}
        initial={editing}
        onClose={() => setModalOpen(false)}
        onSaved={upsert}
      />
    </div>
  )
}

interface ActiveSwitchProps {
  on: boolean
  busy: boolean
  disabled: boolean
  onToggle: () => void
}

/**
 * BITTA TUGMA — vakansiyani yoqish/o'chirish. Ruxsati yo'q xodimga faqat holat ko'rinadi.
 * Holat nomzodlar ilovasidagi ko'rinish bilan bog'liq ekani yonida yozilgan (sarlavha matni).
 */
function ActiveSwitch({ on, busy, disabled, onToggle }: ActiveSwitchProps) {
  const hint = on
    ? "Yoqilgan — nomzodlar ilovasida ko'rinadi va ariza qabul qiladi"
    : "O'chirilgan — nomzodlar ilovasida ko'rinmaydi, ariza qabul qilinmaydi"
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      title={hint}
      disabled={disabled || busy}
      onClick={onToggle}
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2 py-0.5 text-[11px] font-semibold transition-colors disabled:cursor-default',
        on ? 'border-emerald-200 bg-emerald-50 text-emerald-700' : 'border-slate-200 bg-slate-50 text-slate-500',
        !disabled && 'hover:brightness-95',
        busy && 'opacity-60',
      )}
    >
      <span
        className={cn(
          'relative inline-flex h-4 w-7 flex-none items-center rounded-full transition-colors',
          on ? 'bg-emerald-500' : 'bg-slate-300',
        )}
      >
        <span
          className={cn(
            'absolute h-3 w-3 rounded-full bg-white shadow transition-all',
            on ? 'left-[14px]' : 'left-0.5',
          )}
        />
      </span>
      {on ? 'Yoqilgan' : "O'chirilgan"}
    </button>
  )
}
