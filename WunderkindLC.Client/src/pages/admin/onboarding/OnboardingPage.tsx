import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, Briefcase, CheckCircle2, ClipboardList, ListChecks, Plus, Search, UserPlus, Users } from 'lucide-react'
import type {
  EmployeeOnboarding, OnboardingFilter, OnboardingPerson, OnboardingSummary,
} from '@/api/services/onboarding'
import { getOnboardingPeople, getOnboardingSummary, getOnboardings } from '@/api/services/onboarding'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatDate } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { OnboardingDetailModal } from './OnboardingDetailModal'
import { OnboardingFormModal } from './OnboardingFormModal'
import { OnboardingStepsModal } from './OnboardingStepsModal'
import { dueText, filters, progressBarClass, statusLabels, statusTones, todayIso } from './onboardingModel'

const PERM = 'vacancies.onboarding'

/**
 * BOSHQARUV → MOSLASHUV (30 kunlik onboarding) — yangi xodimlarning adaptatsiya cheklisti va
 * MONITORINGI: kim necha foiz bajardi, qaysi qadam kechikdi, navbatdagi qadam qachon.
 *
 * - Ariza «Ishga qabul qilindi» bosqichiga o'tganda moslashuv AVTOMATIK boshlanadi (server);
 *   vakansiyasiz qabul qilingan xodim uchun — «Yangi xodim moslashuvi».
 * - Cheklist qadamlar KATALOGIDAN nusxa olinadi (standart 11 qadam, «Qadamlar katalogi»).
 *
 * Ruxsat: `vacancies.onboarding` (`.claude/rules/onboarding.md`).
 */
export function OnboardingPage() {
  const { can } = usePerm()
  const canCreate = can(PERM, 'create')
  const canEdit = can(PERM, 'edit')
  const canDelete = can(PERM, 'delete')
  const canSeeVacancies = can('vacancies.list', 'view')

  const [items, setItems] = useState<EmployeeOnboarding[]>([])
  const [summary, setSummary] = useState<OnboardingSummary | null>(null)
  const [people, setPeople] = useState<OnboardingPerson[]>([])
  const [filter, setFilter] = useState<OnboardingFilter>('active')
  const [q, setQ] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  /** Qayta yuklash so'rovi — effekt ichida to'g'ridan-to'g'ri setState o'rniga (topshiriqlar.md §6). */
  const [tick, setTick] = useState(0)

  const [openId, setOpenId] = useState<string | null>(null)
  const [detailKey, setDetailKey] = useState(0)
  const [form, setForm] = useState<{ initial: EmployeeOnboarding | null; key: number } | null>(null)
  const [stepsOpen, setStepsOpen] = useState(false)
  const today = todayIso()

  useEffect(() => {
    let alive = true
    const timer = window.setTimeout(() => {
      getOnboardings({ status: filter, q })
        .then((list) => {
          if (!alive) return
          setItems(list)
          setError('')
        })
        .catch((err) => {
          if (alive) setError(apiErrorMessage(err, "Moslashuvlarni yuklab bo'lmadi"))
        })
        .finally(() => {
          if (alive) setLoading(false)
        })
      // Jamlanma — ikkilamchi (asosiy ro'yxat xatosi yuqorida ko'rsatiladi): xato jim, kartalar chizilmaydi.
      getOnboardingSummary()
        .then((s) => {
          if (alive) setSummary(s)
        })
        .catch(() => {
          if (alive) setSummary(null)
        })
    }, q ? 300 : 0)
    return () => {
      alive = false
      window.clearTimeout(timer)
    }
  }, [filter, q, tick])

  useEffect(() => {
    // Mentor/akkaunt tanlagichi uchun — ikkilamchi: bo'lmasa forma ro'yxatsiz ishlayveradi.
    getOnboardingPeople().then(setPeople).catch(() => setPeople([]))
  }, [])

  const reload = () => setTick((t) => t + 1)

  const openDetail = (id: string) => {
    setOpenId(id)
    setDetailKey((k) => k + 1)
  }

  return (
    <div>
      <PageHeader
        title="Moslashuv (30 kunlik onboarding)"
        sub="Yangi xodimning birinchi oyi: cheklist, muddatlar va nazorat"
        actions={
          <>
            {canSeeVacancies && (
              <Link
                to="/admin/boshqaruv/vacancies"
                className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg px-3 py-1 text-xs font-medium text-brand-600 hover:bg-brand-50"
              >
                <Briefcase className="h-4 w-4" /> Vakansiyalar
              </Link>
            )}
            <Button variant="secondary" onClick={() => setStepsOpen(true)}>
              <ListChecks className="h-4 w-4" /> Qadamlar katalogi
            </Button>
            {canCreate && (
              <Button onClick={() => setForm({ initial: null, key: Date.now() })}>
                <Plus className="h-4 w-4" /> Yangi xodim moslashuvi
              </Button>
            )}
          </>
        }
      />

      {summary && (
        <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Jarayonda" value={summary.active} icon={Users} iconBg="bg-sky-50" iconColor="text-sky-600" />
          <StatCard
            label="Kechikkan qadami bor"
            value={summary.withOverdue}
            icon={AlertTriangle}
            iconBg="bg-red-50"
            iconColor="text-red-600"
            hint={summary.overdueTasks > 0 ? `${summary.overdueTasks} ta qadam kechikkan` : undefined}
          />
          <StatCard
            label="Yakunlangan"
            value={summary.completed}
            icon={CheckCircle2}
            iconBg="bg-emerald-50"
            iconColor="text-emerald-600"
          />
          <StatCard label="To'xtatilgan" value={summary.cancelled} icon={ClipboardList} />
        </div>
      )}

      <div className="toolbar">
        <div className="left">
          {filters.map((f) => (
            <button
              key={f.key || 'all'}
              type="button"
              className={cn('filter-chip', filter === f.key && 'active')}
              onClick={() => {
                setLoading(true)
                setFilter(f.key)
              }}
            >
              {f.label}
            </button>
          ))}
        </div>
        <div className="right">
          <div className="search-inline">
            <Search className="h-4 w-4 text-slate-400" />
            <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Ism, lavozim, mentor..." />
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
              <UserPlus className="h-6 w-6" />
            </div>
            <h4>Moslashuvlar yo'q</h4>
            <p>
              Ariza «Ishga qabul qilindi» bosqichiga o'tganda yangi xodimning 30 kunlik cheklisti shu
              yerda o'zi paydo bo'ladi.
            </p>
          </div>
        </Card>
      ) : (
        <div className="space-y-2">
          {items.map((o) => (
            <button
              key={o.id}
              type="button"
              onClick={() => openDetail(o.id)}
              className="block w-full rounded-xl border border-slate-200 bg-white p-4 text-left shadow-[var(--shadow-1)] transition-colors hover:border-brand-200 hover:bg-brand-50/30"
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <div className="min-w-0">
                  <h3 className="text-[15px] font-bold text-slate-800">{o.fullName}</h3>
                  <p className="text-xs text-slate-500">
                    {o.position || '—'}
                    {o.source === 'hired' && o.applicationNumber > 0 && <> · ariza #{o.applicationNumber}</>}
                    {o.mentorName && <> · mentor: {o.mentorName}</>}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  {o.overdueCount > 0 && <Badge tone="red">{o.overdueCount} ta kechikkan</Badge>}
                  <Badge tone={statusTones[o.status]}>{statusLabels[o.status]}</Badge>
                </div>
              </div>

              <div className="mt-3 flex items-center gap-3">
                <div className="h-2 flex-1 overflow-hidden rounded-full bg-slate-100">
                  <div
                    className={cn('h-full rounded-full', progressBarClass(o.progressPct, o.overdueCount))}
                    style={{ width: `${o.progressPct}%` }}
                  />
                </div>
                <span className="w-24 text-right text-xs font-semibold text-slate-600">
                  {o.doneCount}/{o.total} · {o.progressPct}%
                </span>
              </div>

              <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500">
                <span>Boshlangan: {formatDate(o.startDate)}</span>
                {o.status === 'active' && o.dayNumber > 0 && <span>{o.dayNumber}-kun / 30</span>}
                {o.status === 'active' && o.nextTitle && (
                  <span className={cn(o.overdueCount > 0 && 'text-red-600')}>
                    Navbatdagi: {o.nextTitle}
                    {o.nextDueDate && ` (${dueText({ done: false, dueDate: o.nextDueDate }, today)})`}
                  </span>
                )}
              </div>
            </button>
          ))}
        </div>
      )}

      {openId && (
        <OnboardingDetailModal
          key={detailKey}
          onboardingId={openId}
          canEdit={canEdit}
          canDelete={canDelete}
          onClose={() => setOpenId(null)}
          onChanged={reload}
          onDeleted={reload}
          onEdit={(o) => setForm({ initial: o, key: Date.now() })}
        />
      )}

      {form && (
        <OnboardingFormModal
          key={form.key}
          initial={form.initial}
          people={people}
          onClose={() => setForm(null)}
          onSaved={(o) => {
            reload()
            if (form.initial) {
              // Tahrirlangan moslashuv tafsiloti yangi qiymatlar bilan qayta ochiladi.
              setDetailKey((k) => k + 1)
            } else {
              openDetail(o.id)
            }
          }}
        />
      )}

      {stepsOpen && (
        <OnboardingStepsModal
          canCreate={canCreate}
          canEdit={canEdit}
          canDelete={canDelete}
          onClose={() => setStepsOpen(false)}
        />
      )}
    </div>
  )
}
