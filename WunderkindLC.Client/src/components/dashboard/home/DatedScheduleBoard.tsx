import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  IconAlertTriangle,
  IconBuilding,
  IconChevronLeft,
  IconChevronRight,
  IconLayoutGrid,
  IconLayoutRows,
  IconMaximize,
  IconMinimize,
  IconSettings,
  IconUser,
} from '@tabler/icons-react'
import type { DatedSchedule } from '@/api/services/schedule'
import { Loader } from '@/components/ui/Loader'
import {
  DAY_FROM_MIN,
  DAY_TO_MIN,
  datedColumns,
  filterDatedLessons,
  rangeTitle,
  stepDate,
  toGridLesson,
  weekDates,
  type DatedParams,
  type DatedView,
} from '@/lib/scheduleDated'
import {
  FIELD_SETTINGS_KEY,
  gridRange,
  parseFieldSettings,
  timeSlots,
  type FieldSettings,
  type GroupBy,
  type ViewMode,
} from '@/lib/scheduleGrid'
import { cn } from '@/lib/utils'
import { TintIconButton, ToggleGroup } from './HomeControls'
import { LessonTooltip, ScheduleGridView, ScheduleListView, type HoverInfo } from './ScheduleViews'
import { ScheduleSettingsDrawer } from './ScheduleSettingsDrawer'
import { WeekBoard } from './WeekBoard'

import { DateInput } from '@/components/ui/DateInput'
function readFields(): FieldSettings {
  try {
    return parseFieldSettings(localStorage.getItem(FIELD_SETTINGS_KEY))
  } catch {
    return parseFieldSettings(null)
  }
}

/**
 * SANALI «Dars jadvali» kartasi (CRM Phase 5): «Kun» / «Hafta», sana navigatsiyasi ‹ «Bugun» ›,
 * xona/o'qituvchi kesimi, kun ko'rinishida to'r/ro'yxat. Xona ustunlari QAVAT bo'yicha guruhlangan.
 * Eski hafta-kuni tugmalari (shablon jadval) o'rniga — haqiqiy sanadagi darslar (ko'chirilgan dars yangi
 * kunida, o'rinbosar nomi bilan).
 *
 * Holat (sana, ko'rinish, kesim, filtrlar) MANZILDA — sahifa boshqaradi, bu komponent faqat chizadi.
 */
export function DatedScheduleBoard({
  data,
  loading,
  error,
  params,
  onChange,
  today,
  canOpenGroup,
}: {
  data: DatedSchedule | null
  loading: boolean
  error: string | null
  params: DatedParams
  onChange: (patch: Partial<DatedParams>) => void
  today: string
  canOpenGroup: boolean
}) {
  const [fields, setFields] = useState<FieldSettings>(readFields)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [full, setFull] = useState(false)
  const [hover, setHover] = useState<HoverInfo | null>(null)

  const { teacher, group, room, course, floor, status } = params
  const filters = useMemo(
    () => ({ teacher, group, room, course, floor, status }),
    [teacher, group, room, course, floor, status],
  )
  const lessons = useMemo(() => filterDatedLessons(data?.lessons ?? [], filters), [data, filters])
  const dates = useMemo(() => weekDates(params.date), [params.date])

  const dayModel = useMemo(() => {
    if (!data || params.view !== 'day') return null
    const dayLessons = lessons.filter((l) => l.date === params.date).map(toGridLesson)
    const columns = datedColumns(params.groupBy, data.rooms, data.teachers, lessons, filters)
    const range = gridRange(dayLessons, {
      day: 0, fromMin: DAY_FROM_MIN, toMin: DAY_TO_MIN,
      teacherId: '', groupId: '', roomId: '', courseId: '', status: '',
    })
    return { dayLessons, columns, range, slots: timeSlots(range.from, range.to) }
  }, [data, lessons, params.view, params.date, params.groupBy, filters])

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
  const maxHeight = full ? '100%' : '70vh'

  return (
    <section
      className={cn(
        'flex flex-col gap-3 bg-white p-3',
        full ? 'fixed inset-0 z-[60] overflow-hidden' : 'rounded-xl border border-[#DBE0E6] shadow-[0_1px_2px_rgba(0,0,0,0.05)]',
      )}
    >
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          {/* «Kun» / «Hafta» */}
          <div className="inline-flex gap-1 rounded-xl border border-[#DBE0E6] bg-white p-1">
            {(['day', 'week'] as DatedView[]).map((v) => (
              <button
                key={v}
                type="button"
                aria-pressed={params.view === v}
                onClick={() => onChange({ view: v })}
                className={cn(
                  'h-8 rounded-lg px-4 text-[14px] transition-colors',
                  params.view === v ? 'bg-[#3D68FF] font-medium text-white' : 'bg-[#F0F2F2] text-black hover:bg-[#E6EAEA]',
                )}
              >
                {v === 'day' ? 'Kun' : 'Hafta'}
              </button>
            ))}
          </div>
          {/* ‹ Bugun › */}
          <div className="inline-flex items-center gap-1">
            <TintIconButton
              icon={IconChevronLeft}
              title={params.view === 'week' ? 'Oldingi hafta' : 'Oldingi kun'}
              onClick={() => onChange({ date: stepDate(params.view, params.date, -1) })}
            />
            <button
              type="button"
              onClick={() => onChange({ date: today })}
              disabled={params.date === today}
              className="h-8 rounded-lg border border-[rgba(61,104,255,0.20)] bg-[rgba(61,104,255,0.10)] px-3 text-[13px] font-medium text-[#3D68FF] transition-colors hover:bg-[rgba(61,104,255,0.18)] disabled:opacity-50"
            >
              Bugun
            </button>
            <TintIconButton
              icon={IconChevronRight}
              title={params.view === 'week' ? 'Keyingi hafta' : 'Keyingi kun'}
              onClick={() => onChange({ date: stepDate(params.view, params.date, 1) })}
            />
          </div>
          <span className="text-[14px] font-semibold text-black">{rangeTitle(params.view, params.date)}</span>
          <DateInput
            aria-label="Sana tanlash"
            value={params.date}
            onChange={(e) => e.target.value && onChange({ date: e.target.value })}
            className="h-8 rounded-lg border border-[#DBE0E6] bg-white px-2 text-[13px] text-black outline-none focus:border-[#3D68FF]"
          />
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <ToggleGroup<GroupBy>
            value={params.groupBy}
            onChange={(groupBy) => onChange({ groupBy })}
            options={[
              { value: 'room', icon: IconBuilding, title: "Xona bo'yicha" },
              { value: 'teacher', icon: IconUser, title: "O'qituvchi bo'yicha" },
            ]}
          />
          {params.view === 'day' && (
            <ToggleGroup<ViewMode>
              value={params.layout}
              onChange={(layout) => onChange({ layout })}
              options={[
                { value: 'grid', icon: IconLayoutGrid, title: "Jadval ko'rinishi" },
                { value: 'list', icon: IconLayoutRows, title: "Ro'yxat ko'rinishi" },
              ]}
            />
          )}
          {params.view === 'day' && (
            <TintIconButton icon={IconSettings} title="Jadval ko'rinishi sozlamalari" onClick={() => setDrawerOpen(true)} />
          )}
          <TintIconButton
            icon={full ? IconMinimize : IconMaximize}
            title={full ? 'Kichik holatga qaytish' : "Katta xolatda ko'rish"}
            active={full}
            onClick={() => setFull((v) => !v)}
          />
        </div>
      </div>

      {data && data.skippedGroups > 0 && (
        <p className="flex items-center gap-1.5 text-[12px] text-amber-700">
          <IconAlertTriangle size={16} stroke={2} className="shrink-0" />
          {data.skippedGroups} ta guruhning dars vaqti yoki kunlari noto'g'ri kiritilgan — jadvalda ko'rinmaydi
          (guruh formasidan tuzating).
        </p>
      )}

      <div className={cn(full && 'min-h-0 flex-1')}>
        {loading && !data ? (
          <Loader label="Jadval yuklanmoqda..." />
        ) : error && !data ? (
          <p className="py-6 text-center text-[13px] text-red-600">Jadvalni yuklab bo'lmadi: {error}</p>
        ) : !data ? null : params.view === 'week' ? (
          <WeekBoard
            lessons={lessons}
            rooms={data.rooms}
            teachers={data.teachers}
            dates={dates}
            groupBy={params.groupBy}
            params={filters}
            today={today}
            canOpenGroup={canOpenGroup}
            maxHeight={maxHeight}
          />
        ) : dayModel && dayModel.columns.length === 0 ? (
          <p className="py-10 text-center text-[13px] text-[#6B7280]">
            {params.groupBy === 'room' ? "Xonalar hali qo'shilmagan" : "O'qituvchilar yo'q"} — ko'rsatiladigan ustun yo'q.
          </p>
        ) : dayModel ? (
          (() => {
            const viewProps = {
              lessons: dayModel.dayLessons,
              columns: dayModel.columns,
              slots: dayModel.slots,
              rangeFrom: dayModel.range.from,
              groupBy: params.groupBy,
              fields,
              canOpenGroup,
              onHover: setHover,
              maxHeight,
            }
            return params.layout === 'list' ? <ScheduleListView {...viewProps} /> : <ScheduleGridView {...viewProps} />
          })()
        ) : null}
      </div>

      {hover && <LessonTooltip info={hover} date={new Date(`${params.date}T00:00:00`)} />}
      {drawerOpen && <ScheduleSettingsDrawer initial={fields} onSave={saveFields} onClose={closeDrawer} />}
    </section>
  )
}
