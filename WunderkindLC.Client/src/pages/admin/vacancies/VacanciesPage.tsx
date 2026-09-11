import { useCallback, useEffect, useState } from 'react'
import type { LucideIcon } from 'lucide-react'
import { Briefcase, Building2, UserCheck, Users, UserX } from 'lucide-react'
import type { CareerStage, CareerStats, Vacancy } from '@/api/services/career'
import { getCareerStats, getStages, getVacancies } from '@/api/services/career'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { cn } from '@/lib/utils'
import { usePerm } from '@/lib/permissions'
import { VacancyListTab } from './VacancyListTab'
import { ApplicationsTab } from './ApplicationsTab'
import { CareerAboutTab } from './CareerAboutTab'

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
 * Ruxsat kaliti: `vacancies`.
 */
export function VacanciesPage() {
  const { can } = usePerm()
  const [tab, setTab] = useState<Tab>('vacancies')
  const [stages, setStages] = useState<CareerStage[]>([])
  const [vacancies, setVacancies] = useState<Vacancy[]>([])
  const [stats, setStats] = useState<CareerStats | null>(null)
  const [vacancyFilter, setVacancyFilter] = useState('')

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

      {tab === 'vacancies' ? (
        <VacancyListTab
          canCreate={can('vacancies', 'create')}
          canEdit={can('vacancies', 'edit')}
          canDelete={can('vacancies', 'delete')}
          onOpenApplications={openApplications}
        />
      ) : tab === 'applications' ? (
        <ApplicationsTab
          stages={stages}
          vacancies={vacancies}
          canEdit={can('vacancies', 'edit')}
          canDelete={can('vacancies', 'delete')}
          vacancyFilter={vacancyFilter}
          onVacancyFilterChange={setVacancyFilter}
        />
      ) : (
        <CareerAboutTab canEdit={can('vacancies', 'edit')} />
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
