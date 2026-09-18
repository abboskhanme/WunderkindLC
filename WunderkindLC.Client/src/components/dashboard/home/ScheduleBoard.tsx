import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  IconAlertTriangle,
  IconBuilding,
  IconLayoutGrid,
  IconLayoutRows,
  IconMaximize,
  IconMinimize,
  IconSettings,
  IconUser,
} from '@tabler/icons-react'
import type { ScheduleGrid } from '@/api/services/schedule'
import { Loader } from '@/components/ui/Loader'
import {
  DAY_TABS,
  FIELD_SETTINGS_KEY,
  dateOfServerDay,
  gridColumns,
  gridRange,
  lessonsOfDay,
  parseFieldSettings,
  timeSlots,
  type FieldSettings,
  type GridFilter,
  type GroupBy,
  type ViewMode,
} from '@/lib/scheduleGrid'
import { cn } from '@/lib/utils'
import { TintIconButton, ToggleGroup } from './HomeControls'
import { LessonTooltip, ScheduleGridView, ScheduleListView, type HoverInfo } from './ScheduleViews'
import { ScheduleSettingsDrawer } from './ScheduleSettingsDrawer'

function readFields(): FieldSettings {
  try {
    return parseFieldSettings(localStorage.getItem(FIELD_SETTINGS_KEY))
  } catch {
    return parseFieldSettings(null)
  }
}

/**
 * "Dars jadvali" kartasi: kun tugmalari (Yakshanbadan), xona/o'qituvchi va to'r/ro'yxat
 * almashtirgichlari, sozlamalar drawer'i va katta (to'liq ekran) holat.
 *
 * Kun/rejim/ko'rinish MANZILDA (sahifa boshqaradi) — bu komponent faqat chizadi va o'zgarishni
 * yuqoriga beradi. Blok maydonlari sozlamasi — shu brauzerning `localStorage` ida.
 */
export function ScheduleBoard({
  grid,
  loading,
  error,
  jsDay,
  onDay,
  groupBy,
  onGroupBy,
  view,
  onView,
  filter,
  canOpenGroup,
}: {
  grid: ScheduleGrid | null
  loading: boolean
  error: string | null
  jsDay: number
  onDay: (d: number) => void
  groupBy: GroupBy
  onGroupBy: (g: GroupBy) => void
  view: ViewMode
  onView: (v: ViewMode) => void
  filter: GridFilter
  canOpenGroup: boolean
}) {
  const [fields, setFields] = useState<FieldSettings>(readFields)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [full, setFull] = useState(false)
  const [hover, setHover] = useState<HoverInfo | null>(null)

  const model = useMemo(() => {
    if (!grid) return null
    const lessons = lessonsOfDay(grid.groups, filter)
    const columns = gridColumns(groupBy, grid.rooms, grid.teachers, lessons, filter)
    const range = gridRange(lessons, filter)
    return { lessons, columns, range, slots: timeSlots(range.from, range.to) }
  }, [grid, filter, groupBy])

  // Katta holatdan Esc bilan chiqish (drawer ochiq bo'lsa — avval drawer yopiladi).
  useEffect(() => {
    if (!full) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !drawerOpen) setFull(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [full, drawerOpen])

  const saveFields = (s: FieldSettings) => {
    setFields(s)
    setDrawerOpen(false)
    try {
      localStorage.setItem(FIELD_SETTINGS_KEY, JSON.stringify(s))
    } catch {
      /* inkognito/kvota — sozlama shu sessiya davomida baribir ishlaydi */
    }
  }
  const closeDrawer = useCallback(() => setDrawerOpen(false), [])

  const viewProps = model && {
    lessons: model.lessons,
    columns: model.columns,
    slots: model.slots,
    rangeFrom: model.range.from,
    groupBy,
    fields,
    canOpenGroup,
    onHover: setHover,
    maxHeight: full ? '100%' : '70vh',
  }

  return (
    <section
      className={cn(
        'flex flex-col gap-3 bg-white p-3',
        full ? 'fixed inset-0 z-[60] overflow-hidden' : 'rounded-xl border border-[#DBE0E6] shadow-[0_1px_2px_rgba(0,0,0,0.05)]',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        {/* Kun tugmalari — Yakshanbadan */}
        <div className="inline-flex max-w-full gap-1 overflow-x-auto rounded-xl border border-[#DBE0E6] bg-white p-1">
          {DAY_TABS.map((label, idx) => (
            <button
              key={label}
              type="button"
              onClick={() => onDay(idx)}
              aria-pressed={idx === jsDay}
              className={cn(
                'h-8 shrink-0 rounded-lg px-5 text-[14px] transition-colors',
                idx === jsDay ? 'bg-[#3D68FF] font-medium text-white' : 'bg-[#F0F2F2] text-black hover:bg-[#E6EAEA]',
              )}
            >
              {label}
            </button>
          ))}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <ToggleGroup<GroupBy>
            value={groupBy}
            onChange={onGroupBy}
            options={[
              { value: 'room', icon: IconBuilding, title: 'Xona' },
              { value: 'teacher', icon: IconUser, title: "O'qituvchi" },
            ]}
          />
          <ToggleGroup<ViewMode>
            value={view}
            onChange={onView}
            options={[
              { value: 'grid', icon: IconLayoutGrid, title: "Jadval ko'rinishi" },
              { value: 'list', icon: IconLayoutRows, title: "Ro'yxat ko'rinishi" },
            ]}
          />
          <TintIconButton icon={IconSettings} title="Jadval ko'rinishi sozlamalari" onClick={() => setDrawerOpen(true)} />
          <TintIconButton
            icon={full ? IconMinimize : IconMaximize}
            title={full ? "Kichik holatga qaytish" : "Katta xolatda ko'rish"}
            active={full}
            onClick={() => setFull((v) => !v)}
          />
        </div>
      </div>

      {grid && grid.skippedGroups > 0 && (
        <p className="flex items-center gap-1.5 text-[12px] text-amber-700">
          <IconAlertTriangle size={16} stroke={2} className="shrink-0" />
          {grid.skippedGroups} ta guruhning dars vaqti yoki kunlari noto'g'ri kiritilgan — jadvalda ko'rinmaydi
          (guruh formasidan tuzating).
        </p>
      )}

      <div className={cn(full && 'min-h-0 flex-1')}>
        {loading && !grid ? (
          <Loader label="Jadval yuklanmoqda..." />
        ) : error && !grid ? (
          <p className="py-6 text-center text-[13px] text-red-600">Jadvalni yuklab bo'lmadi: {error}</p>
        ) : viewProps && viewProps.columns.length === 0 ? (
          <p className="py-10 text-center text-[13px] text-[#6B7280]">
            {groupBy === 'room' ? "Xonalar hali qo'shilmagan" : "O'qituvchilar yo'q"} — ko'rsatiladigan ustun yo'q.
          </p>
        ) : viewProps ? (
          view === 'list' ? (
            <ScheduleListView {...viewProps} />
          ) : (
            <ScheduleGridView {...viewProps} />
          )
        ) : null}
      </div>

      {hover && <LessonTooltip info={hover} date={dateOfServerDay(filter.day, new Date())} />}
      {drawerOpen && <ScheduleSettingsDrawer initial={fields} onSave={saveFields} onClose={closeDrawer} />}
    </section>
  )
}
