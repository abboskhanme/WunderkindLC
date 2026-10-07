import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { LucideIcon } from 'lucide-react'
import { Briefcase, Building2, ListChecks, UserCheck, Users, UserX } from 'lucide-react'
import type { CareerStage, CareerStats, Vacancy } from '@/api/services/career'
import { getCareerStats, getStages, getVacancies } from '@/api/services/career'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { cn } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { VacancyListTab } from './VacancyListTab'
import { ApplicationsTab } from './ApplicationsTab'
import { CareerAboutTab } from './CareerAboutTab'
import { CareerFunnel } from './CareerFunnel'

type Tab = 'vacancies' | 'applications' | 'about'

/**
 * BOSHQARUV → VAKANSIYALAR (Wunderkind Career).
 *
 * Bo'lim ALOHIDA Telegram bot (`.env: CAREER_BOT_TOKEN`) va uning Mini App'iga
 * (`/vakansiya` — statik HTML/Bootstrap sahifa) xizmat qiladi:
 *  • Vakansiyalar — faol e'lonlar yaratish/arxivlash (ilovada shular ko'rinadi);
 *  • Arizalar — nomzodlar arizalarini bosqichma-bosqich yuritish (bosqich o'zgarganda
 *    nomzodga botda avtomatik xabar ketadi);
 *  • Biz haqimizda — ilovaning birinchi ekrani (matn, manzil, ijtimoiy tarmoqlar).
 *
 * Ruxsat kaliti: `vacancies.list` (yalang `vacancies` merosi bilan ochadi). Ikkinchi sahifa —
 * «Moslashuv» (`vacancies.onboarding`): ariza "Ishga qabul qilindi" bo'lganda yangi xodimning
 * 30 kunlik cheklisti o'zi boshlanadi.
 */
export function VacanciesPage() {
  const { can } = usePerm()
  const canOnboarding = can('vacancies.onboarding', 'view')
  const [tab, setTab] = useState<Tab>('vacancies')
  const [stages, setStages] = useState<CareerStage[]>([])
  const [vacancies, setVacancies] = useState<Vacancy[]>([])
  const [stats, setStats] = useState<CareerStats | null>(null)
  const [vacancyFilter, setVacancyFilter] = useState('')

  // Jamlanma — ikkilamchi ma'lumot (sahifaning asosiy ro'yxatlari o'z xatosini ko'rsatadi),
  // shuning uchun xato jim: kartalar shunchaki chizilmaydi.
  const refreshStats = useCallback(() => {
    getCareerStats().then(setStats).catch(() => setStats(null))
  }, [])

  useEffect(() => {
    getStages().then(setStages).catch(() => setStages([]))
    getVacancies().then(setVacancies).catch(() => setVacancies([]))
    refreshStats()
  }, [refreshStats])

  // Vakansiya kartasidagi "N ta ariza" — filtrni qo'yib "Arizalar" tabiga o'tadi.
  const openApplications = (vacancyId: string) => {
    setVacancyFilter(vacancyId)
    setTab('applications')
  }

  return (
    <div>
      <PageHeader
        title="Vakansiyalar"
        sub="Ishga qabul boti va nomzodlar ilovasi (Wunderkind Career)"
        actions={
          <div className="flex flex-wrap items-center gap-2">
          {canOnboarding && (
            <Link
              to="/admin/boshqaruv/moslashuv"
              className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg border border-brand-600/50 bg-white px-3 py-1 text-xs font-medium text-brand-600 hover:bg-brand-50"
            >
              <ListChecks className="h-4 w-4" /> Moslashuv (30 kun)
            </Link>
          )}
          <div className="tabs">
            <TabButton active={tab === 'vacancies'} onClick={() => setTab('vacancies')} icon={Briefcase}>
              Vakansiyalar
            </TabButton>
            <TabButton
              active={tab === 'applications'}
              onClick={() => setTab('applications')}
              icon={Users}
            >
              <span className="inline-flex items-center gap-1.5">
                Arizalar
                {stats && stats.byStatus.new > 0 && (
                  <span className="rounded-full bg-red-500 px-1.5 py-px text-[11px] font-bold text-white">
                    {stats.byStatus.new}
                  </span>
                )}
              </span>
            </TabButton>
            <TabButton active={tab === 'about'} onClick={() => setTab('about')} icon={Building2}>
              Biz haqimizda
            </TabButton>
          </div>
          </div>
        }
      />

      {stats && (
        <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Jami ariza" value={stats.total} icon={Users} />
          <StatCard
            label="Jarayonda"
            value={stats.active}
            icon={Briefcase}
            iconBg="bg-sky-50"
            iconColor="text-sky-600"
          />
          <StatCard
            label="Ishga qabul qilingan"
            value={stats.hired}
            icon={UserCheck}
            iconBg="bg-emerald-50"
            iconColor="text-emerald-600"
          />
          <StatCard
            label="Rad etilgan"
            value={stats.rejected}
            icon={UserX}
            iconBg="bg-red-50"
            iconColor="text-red-600"
          />
        </div>
      )}

      {tab === 'applications' && stats?.funnel && <CareerFunnel steps={stats.funnel} />}

      {tab === 'vacancies' ? (
        <VacancyListTab
          canCreate={can('vacancies.list', 'create')}
          canEdit={can('vacancies.list', 'edit')}
          canDelete={can('vacancies.list', 'delete')}
          onOpenApplications={openApplications}
        />
      ) : tab === 'applications' ? (
        <ApplicationsTab
          stages={stages}
          vacancies={vacancies}
          canEdit={can('vacancies.list', 'edit')}
          canDelete={can('vacancies.list', 'delete')}
          vacancyFilter={vacancyFilter}
          onVacancyFilterChange={setVacancyFilter}
          counts={stats?.byStatus}
          onChanged={refreshStats}
        />
      ) : (
        <CareerAboutTab canEdit={can('vacancies.list', 'edit')} />
      )}
    </div>
  )
}

interface TabButtonProps {
  active: boolean
  onClick: () => void
  icon: LucideIcon
  children: React.ReactNode
}

function TabButton({ active, onClick, icon: Icon, children }: TabButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn('tab inline-flex items-center gap-1.5', active && 'active')}
    >
      <Icon className="h-4 w-4" />
      {children}
    </button>
  )
}
