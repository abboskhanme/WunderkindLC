import { useCallback, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { IconChartBar, IconFileExport, IconFilter } from '@tabler/icons-react'
import { getDashboardSummary, type DashboardSummary } from '@/api/services/dashboard'
import { exportScheduleDay, getScheduleGrid, type ScheduleGrid } from '@/api/services/schedule'
import { HomeButton } from '@/components/dashboard/home/HomeControls'
import { HomeFilterRow } from '@/components/dashboard/home/HomeFilterRow'
import { HomeStatCards } from '@/components/dashboard/home/HomeStatCards'
import { ScheduleBoard } from '@/components/dashboard/home/ScheduleBoard'
import { useAsync } from '@/hooks/useAsync'
import { HOME_CARDS } from '@/lib/homeCards'
import { usePerm } from '@/lib/permissions'
import {
  gridFilterOf,
  homeParamsToSearch,
  jsDayToServer,
  parseHomeParams,
  type HomeParams,
} from '@/lib/scheduleGrid'
import { apiErrorMessage } from '@/lib/utils'

const SHOW_STATS_KEY = 'home.showStats'

function readShowStats(): boolean {
  try {
    return localStorage.getItem(SHOW_STATS_KEY) !== '0'
  } catch {
    return true
  }
}

/**
 * BOSH SAHIFA — edutizim'dagi "Dars jadvali" (`docs/edutizim/INVENTORY.md` §3):
 * sarlavha + "Statistika"/"Filtr" tugmalari, 12 ta kartochka, filtr qatori, "Export" va
 * kunlik jadval to'ri (xona/o'qituvchi kesimida, to'r yoki ro'yxat ko'rinishida).
 *
 * - Kun, rejim, ko'rinish, vaqt oralig'i va filtrlar MANZILDA (edutizim kabi:
 *   `?fromHour=08:00&toHour=22:00&day=5&dayName=Juma&groupBy=room`) — havola ulashilsa
 *   o'sha ko'rinish ochiladi. Manzil `replace` bilan yangilanadi: har bosish tarixga yozilsa
 *   "orqaga" tugmasi sahifadan chiqmay, kunlar orasida aylanardi.
 * - Jadval `schedule.timetable` ruxsati bo'lsagina chiziladi; kartochkalar — har biri o'z
 *   bo'limi ruxsati bo'yicha. Ikkalasi ham yo'q xodimga sahifa bo'sh holatni aytadi.
 */
export function AdminDashboard({ scheduleOnly = false }: {
  /**
   * Guruh → «Dars jadvali» (`/admin/jadval`) — edutizimda bosh sahifadagi AYNAN O'SHA jadval,
   * faqat kartochkalarsiz: yuqorida "Export" + "Filtr".
   */
  scheduleOnly?: boolean
} = {}) {
  const { can } = usePerm()
  const canSchedule = can('schedule.timetable', 'view')
  const canOpenGroup = can('classes.list', 'view')
  const anyCard = !scheduleOnly && HOME_CARDS.some((c) => can(c.perm, 'view'))

  const [searchParams, setSearchParams] = useSearchParams()
  const todayJs = new Date().getDay()
  const params = useMemo(() => parseHomeParams(searchParams, todayJs), [searchParams, todayJs])
  const filter = useMemo(() => gridFilterOf(params), [params])
  const update = useCallback(
    (patch: Partial<HomeParams>) => setSearchParams(homeParamsToSearch({ ...params, ...patch }), { replace: true }),
    [params, setSearchParams],
  )

  const summary = useAsync<DashboardSummary | null>(
    () => (anyCard ? getDashboardSummary() : Promise.resolve(null)),
    [anyCard],
  )
  const grid = useAsync<ScheduleGrid | null>(
    () => (canSchedule ? getScheduleGrid() : Promise.resolve(null)),
    [canSchedule],
  )

  const [showStats, setShowStats] = useState(readShowStats)
  const [showFilter, setShowFilter] = useState(
    () => !!(params.teacher || params.group || params.room || params.course || params.status),
  )
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const toggleStats = () => {
    const next = !showStats
    setShowStats(next)
    try {
      localStorage.setItem(SHOW_STATS_KEY, next ? '1' : '0')
    } catch {
      /* saqlanmasa ham tugma shu sahifada ishlaydi */
    }
  }

  const onExport = async () => {
    setExporting(true)
    setExportError(null)
    try {
      await exportScheduleDay({
        day: jsDayToServer(params.day),
        groupBy: params.groupBy,
        fromHour: params.fromHour,
        toHour: params.toHour,
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

  return (
    <div className="space-y-2.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">{canSchedule ? 'Dars jadvali' : 'Bosh sahifa'}</h1>
        <div className="flex items-center gap-4">
          {scheduleOnly && canSchedule && (
            <HomeButton icon={IconFileExport} onClick={onExport} disabled={exporting}>
              {exporting ? 'Yuklanmoqda…' : 'Export'}
            </HomeButton>
          )}
          {anyCard && (
            <HomeButton icon={IconChartBar} active={showStats} onClick={toggleStats}>
              Statistika
            </HomeButton>
          )}
          {canSchedule && (
            <HomeButton icon={IconFilter} active={showFilter} onClick={() => setShowFilter((v) => !v)}>
              Filtr
            </HomeButton>
          )}
        </div>
      </div>

      {anyCard && showStats && (
        <>
          <HomeStatCards data={summary.data} loading={summary.loading} />
          {summary.error && (
            <p className="text-[12px] text-red-600">Ko'rsatkichlarni yuklab bo'lmadi: {summary.error}</p>
          )}
        </>
      )}

      {canSchedule && showFilter && (
        <HomeFilterRow
          grid={grid.data}
          params={params}
          onChange={(key, value) => update({ [key]: value } as Partial<HomeParams>)}
        />
      )}

      {canSchedule && (
        <>
          {!scheduleOnly ? (
            <div className="flex items-center justify-end gap-3">
              {exportError && <span className="text-[12px] text-red-600">{exportError}</span>}
              <HomeButton icon={IconFileExport} onClick={onExport} disabled={exporting}>
                {exporting ? 'Yuklanmoqda…' : 'Export'}
              </HomeButton>
            </div>
          ) : (
            exportError && <p className="text-right text-[12px] text-red-600">{exportError}</p>
          )}

          <ScheduleBoard
            grid={grid.data}
            loading={grid.loading}
            error={grid.error}
            jsDay={params.day}
            onDay={(day) => update({ day })}
            groupBy={params.groupBy}
            onGroupBy={(groupBy) => update({ groupBy })}
            view={params.view}
            onView={(view) => update({ view })}
            filter={filter}
            canOpenGroup={canOpenGroup}
          />
        </>
      )}

      {!canSchedule && !anyCard && (
        <div className="rounded-xl border border-[#DBE0E6] bg-white px-4 py-10 text-center text-[13px] text-[#6B7280]">
          Bosh sahifada sizga ochiq bo'lim yo'q — kerakli bo'limni chap menyudan tanlang.
        </div>
      )}
    </div>
  )
}
