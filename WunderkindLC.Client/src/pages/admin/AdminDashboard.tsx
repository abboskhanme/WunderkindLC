import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { IconChartBar, IconFileExport, IconFilter, IconLayoutDashboard } from '@tabler/icons-react'
import { getDashboardSummary, type DashboardSummary } from '@/api/services/dashboard'
import { getHomeLayout, resetHomeLayout, saveHomeLayout } from '@/api/services/homeLayout'
import { exportScheduleDay, getDatedSchedule, type DatedSchedule } from '@/api/services/schedule'
import { CenterAiAnalysisCard } from '@/components/dashboard/CenterAiAnalysisCard'
import { TodayLessonsMonitor } from '@/components/dashboard/TodayLessonsMonitor'
import { CashboxBalanceStrip } from '@/components/dashboard/home/CashboxBalanceStrip'
import { DatedFilterRow } from '@/components/dashboard/home/DatedFilterRow'
import { DatedScheduleBoard } from '@/components/dashboard/home/DatedScheduleBoard'
import { AbsentNoticesBlock, ContactsTodayBlock, MyTasksBlock, RedListBlock } from '@/components/dashboard/home/HomeBlocks'
import { HomeButton } from '@/components/dashboard/home/HomeControls'
import { HomeStatCards } from '@/components/dashboard/home/HomeStatCards'
import { HomeWidgetsDrawer } from '@/components/dashboard/home/HomeWidgetsDrawer'
import { ScheduleConflictsBanner } from '@/components/dashboard/home/ScheduleConflictsBanner'
import { Loader } from '@/components/ui/Loader'
import { useAuth } from '@/context/auth-context'
import { useAsync } from '@/hooks/useAsync'
import { HOME_CARDS } from '@/lib/homeCards'
import {
  LEGACY_SHOW_STATS_KEY,
  defaultHomeLayout,
  initialHomeLayout,
  visibleBlocks,
  visibleCards,
  type HomeBlockKey,
  type HomeLayout,
  type WidgetAccess,
} from '@/lib/homeWidgets'
import { usePerm } from '@/lib/permissions'
import {
  datedParamsToSearch,
  parseDatedParams,
  rangeOf,
  serverDayOf,
  todayIsoLocal,
  type DatedParams,
} from '@/lib/scheduleDated'
import { DEFAULT_FROM, DEFAULT_TO } from '@/lib/scheduleGrid'
import { apiErrorMessage } from '@/lib/utils'

/** Eski brauzer sozlamasi («Statistika» tugmasi) — server sozlamasi yo'q bo'lsa BIR MARTA boshlang'ich qiymat. */
function readLegacyShowStats(): boolean | null {
  try {
    const v = localStorage.getItem(LEGACY_SHOW_STATS_KEY)
    return v === null ? null : v !== '0'
  } catch {
    return null
  }
}

/**
 * BOSH SAHIFA — edutizim'dagi "Dars jadvali" (`docs/edutizim/INVENTORY.md` §3) + VIDJETLAR (CRM Phase 5):
 * 12 ta kartochka va bloklar (kassalar, sanali jadval, darslar monitoringi, topshiriqlarim, bog'lanish,
 * AI tahlil) foydalanuvchining O'Z sozlamasi bo'yicha (server, `lib/homeWidgets.ts`) — «Vidjetlar» oynasidan
 * yoqiladi/o'chiriladi/tartiblanadi; standart — avvalgi sahifa bilan bir xil (kartochkalar + kassalar + jadval).
 *
 * - Jadval SANALI: «Kun» / «Hafta», ‹ «Bugun» ›, xona (qavat bo'yicha) / o'qituvchi kesimi. Holat MANZILDA
 *   (`?date=&view=&groupBy=` + filtrlar), `replace` bilan — "orqaga" sahifadan chiqadi, kunlar orasida aylanmaydi.
 * - Har kartochka/blok o'z ruxsati bo'yicha; vidjet sozlamasi ma'lumot OCHMAYDI.
 */
export function AdminDashboard({ scheduleOnly = false }: {
  /**
   * Guruh → «Dars jadvali» (`/admin/jadval`) — bosh sahifadagi AYNAN O'SHA sanali jadval, kartochka va
   * vidjetlarsiz: yuqorida "Export" + "Filtr".
   */
  scheduleOnly?: boolean
} = {}) {
  const { can } = usePerm()
  const { user } = useAuth()
  const isSuperAdmin = user?.role === 'superadmin'
  const canSchedule = can('schedule.timetable', 'view')
  const canOpenGroup = can('classes.list', 'view')
  const access: WidgetAccess = { can, isSuperAdmin }
  const anyCardAllowed = !scheduleOnly && HOME_CARDS.some((c) => can(c.perm, 'view'))

  // ---------- Vidjetlar sozlamasi (serverda, foydalanuvchiniki) ----------
  const layoutRes = useAsync(() => (scheduleOnly ? Promise.resolve(null) : getHomeLayout()), [scheduleOnly])
  const [layoutOverride, setLayoutOverride] = useState<HomeLayout | null>(null)
  const layoutReady = scheduleOnly || !layoutRes.loading
  // O'qish xatosi JIM standartga tushadi (sahifa bo'sh qolmasin) — error-visibility.md §4 (ikkilamchi o'qish).
  const layout = layoutOverride ?? initialHomeLayout(layoutRes.data, readLegacyShowStats())
  const [layoutError, setLayoutError] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)

  const cards = scheduleOnly ? [] : visibleCards(layout, access)
  const showCards = !scheduleOnly && layoutReady && layout.cardsVisible && cards.length > 0
  const blocks = scheduleOnly || !layoutReady ? [] : visibleBlocks(layout, access)
  const scheduleShown = scheduleOnly ? canSchedule : blocks.some((b) => b.key === 'schedule')

  const summary = useAsync<DashboardSummary | null>(
    () => (showCards ? getDashboardSummary() : Promise.resolve(null)),
    [showCards],
  )

  // ---------- Sanali jadval (manzil holati) ----------
  const [searchParams, setSearchParams] = useSearchParams()
  const today = todayIsoLocal()
  const params = useMemo(() => parseDatedParams(searchParams, today), [searchParams, today])
  const update = useCallback(
    (patch: Partial<DatedParams>) =>
      setSearchParams(datedParamsToSearch({ ...params, ...patch }, today), { replace: true }),
    [params, setSearchParams, today],
  )
  const range = rangeOf(params.view, params.date)
  const dated = useAsync<DatedSchedule | null>(
    () => (scheduleShown ? getDatedSchedule(range.from, range.to) : Promise.resolve(null)),
    [scheduleShown, range.from, range.to],
  )

  const [showFilter, setShowFilter] = useState(
    () => !!(params.teacher || params.group || params.room || params.course || params.floor || params.status),
  )
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const onExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await exportScheduleDay({
        day: serverDayOf(params.date),
        date: params.date,
        groupBy: params.groupBy,
        fromHour: DEFAULT_FROM,
        toHour: DEFAULT_TO,
        teacherId: params.teacher,
        groupId: params.group,
        roomId: params.room,
        courseId: params.course,
        status: params.status,
      })
    } catch (e) {
      setExportError(apiErrorMessage(e, "Eksport qilib bo'lmadi"))
    } finally {
      setExporting(false)
    }
  }

  /** «Statistika» — kartochkalarni yashirish/ko'rsatish, SERVERGA saqlanadi. Optimistik: xatoda qaytariladi. */
  const toggleStats = async () => {
    const prev = layout
    const next = { ...layout, cardsVisible: !layout.cardsVisible }
    setLayoutOverride(next)
    setLayoutError(null)
    try {
      setLayoutOverride(await saveHomeLayout(next))
    } catch (e) {
      setLayoutOverride(prev)
      setLayoutError(apiErrorMessage(e, "Sozlamani saqlab bo'lmadi"))
    }
  }

  const saveLayout = async (next: HomeLayout) => {
    try {
      setLayoutOverride(await saveHomeLayout(next))
      setDrawerOpen(false)
    } catch (e) {
      throw new Error(apiErrorMessage(e, "Sozlamani saqlab bo'lmadi"), { cause: e })
    }
  }

  const resetLayout = async () => {
    try {
      await resetHomeLayout()
      setLayoutOverride(defaultHomeLayout())
      setDrawerOpen(false)
    } catch (e) {
      throw new Error(apiErrorMessage(e, "Standartga qaytarib bo'lmadi"), { cause: e })
    }
  }

  const scheduleBlock = (
    <div key="schedule" className="space-y-2.5">
      {showFilter && (
        <DatedFilterRow data={dated.data} params={params} onChange={(key, value) => update({ [key]: value } as Partial<DatedParams>)} />
      )}
      {!scheduleOnly && (
        <div className="flex items-center justify-end gap-3">
          {exportError && <span className="text-[12px] text-red-600">{exportError}</span>}
          <HomeButton icon={IconFileExport} onClick={onExport} disabled={exporting}>
            {exporting ? 'Yuklanmoqda…' : 'Export'}
          </HomeButton>
        </div>
      )}
      {scheduleOnly && exportError && <p className="text-right text-[12px] text-red-600">{exportError}</p>}
      <ScheduleConflictsBanner canOpenGroup={canOpenGroup} />
      <DatedScheduleBoard
        data={dated.data}
        loading={dated.loading}
        error={dated.error}
        params={params}
        onChange={update}
        today={today}
        canOpenGroup={canOpenGroup}
      />
    </div>
  )

  const renderBlock = (key: HomeBlockKey) => {
    switch (key) {
      case 'cashboxes':
        return <CashboxBalanceStrip key={key} />
      case 'schedule':
        return scheduleBlock
      case 'lessonsMonitor':
        return <TodayLessonsMonitor key={key} compact />
      case 'myTasks':
        return <MyTasksBlock key={key} />
      case 'absentNotices':
        return <AbsentNoticesBlock key={key} />
      case 'redList':
        return <RedListBlock key={key} />
      case 'contactsToday':
        return <ContactsTodayBlock key={key} />
      case 'aiAnalysis':
        return <CenterAiAnalysisCard key={key} />
      default:
        return null
    }
  }

  const nothing = scheduleOnly ? !canSchedule : layoutReady && !showCards && blocks.length === 0

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">{scheduleShown ? 'Dars jadvali' : 'Bosh sahifa'}</h1>
        <div className="flex flex-wrap items-center gap-4">
          {scheduleOnly && canSchedule && (
            <HomeButton icon={IconFileExport} onClick={onExport} disabled={exporting}>
              {exporting ? 'Yuklanmoqda…' : 'Export'}
            </HomeButton>
          )}
          {anyCardAllowed && layoutReady && (
            <HomeButton icon={IconChartBar} active={layout.cardsVisible} onClick={toggleStats}>
              Statistika
            </HomeButton>
          )}
          {!scheduleOnly && layoutReady && (
            <HomeButton icon={IconLayoutDashboard} onClick={() => setDrawerOpen(true)}>
              Vidjetlar
            </HomeButton>
          )}
          {scheduleShown && (
            <HomeButton icon={IconFilter} active={showFilter} onClick={() => setShowFilter((v) => !v)}>
              Filtr
            </HomeButton>
          )}
        </div>
      </div>
      {layoutError && <p className="text-right text-[12px] text-red-600">{layoutError}</p>}

      {!layoutReady && <Loader label="Yuklanmoqda..." />}

      {showCards && (
        <>
          <HomeStatCards data={summary.data} loading={summary.loading} cards={cards} />
          {summary.error && (
            <p className="text-[12px] text-red-600">Ko'rsatkichlarni yuklab bo'lmadi: {summary.error}</p>
          )}
        </>
      )}

      {scheduleOnly ? canSchedule && scheduleBlock : blocks.map((b) => renderBlock(b.key))}

      {nothing && (
        <div className="rounded-xl border border-[#DBE0E6] bg-white px-4 py-10 text-center text-[13px] text-[#6B7280]">
          {scheduleOnly
            ? "Bosh sahifada sizga ochiq bo'lim yo'q — kerakli bo'limni chap menyudan tanlang."
            : "Bosh sahifada ko'rsatiladigan vidjet yo'q — «Vidjetlar» tugmasi orqali yoqing yoki chap menyudan bo'lim tanlang."}
        </div>
      )}

      {drawerOpen && (
        <HomeWidgetsDrawer
          initial={layout}
          access={access}
          onSave={saveLayout}
          onReset={resetLayout}
          onClose={() => setDrawerOpen(false)}
        />
      )}
    </div>
  )
}
