import { useMemo, type CSSProperties, type MouseEvent as ReactMouseEvent } from 'react'
import { Link } from 'react-router-dom'
import { IconBook, IconUsers } from '@tabler/icons-react'
import type { ScheduleGridGroup } from '@/api/services/schedule'
import {
  SLOT_MINUTES,
  assignLanes,
  columnOf,
  fieldValue,
  formatIsoDate,
  lessonColor,
  textOn,
  toMinutes,
  type FieldSettings,
  type GridColumn,
  type GroupBy,
} from '@/lib/scheduleGrid'
import { cn } from '@/lib/utils'

/**
 * "Dars jadvali" ko'rinishlari — edutizim o'lchamlarida (`INVENTORY.md` §1/§3):
 * - TO'R: chapda yarim soatlik vaqt ustuni (120px, #F9FAFB, 13px/500 #6B7280), tepada xona/
 *   o'qituvchi sarlavhalari (150px, 14px/600 #374151), darslar — ustun ichida mutlaq bloklar;
 * - RO'YXAT: o'qlar almashgan — vaqt tepada, xona/o'qituvchi chapda, darslar gorizontal chiziq.
 */

const TIME_W = 120
const COL_W = 150
const ROW_H = 30
const HEAD_H = 40

const LIST_OWNER_W = 130
const LIST_SLOT_W = 150
const LIST_ROW_H = 50
const LIST_BAR_H = 44

const BORDER = 'border-[#E5E7EB]'

/** Blok ustiga kelganda ko'rsatiladigan tooltip uchun ma'lumot. */
export interface HoverInfo {
  lesson: ScheduleGridGroup
  rect: DOMRect
}

interface ViewProps {
  lessons: ScheduleGridGroup[]
  columns: GridColumn[]
  slots: { start: number; label: string }[]
  rangeFrom: number
  groupBy: GroupBy
  fields: FieldSettings
  canOpenGroup: boolean
  onHover: (h: HoverInfo | null) => void
  /** Scroll konteyner balandligi (katta holatda — butun bo'sh joy, `100%`). */
  maxHeight: string
}

/** Ustun bo'yicha darslar + yo'laklar (ustma-ust darslar yonma-yon/ustma-ust chiziladi). */
function useByColumn(lessons: ScheduleGridGroup[], groupBy: GroupBy) {
  return useMemo(() => {
    const map = new Map<string, ScheduleGridGroup[]>()
    for (const l of lessons) {
      const k = columnOf(l, groupBy)
      const list = map.get(k)
      if (list) list.push(l)
      else map.set(k, [l])
    }
    const out = new Map<string, ReturnType<typeof assignLanes<ScheduleGridGroup>>>()
    for (const [k, list] of map) out.set(k, assignLanes(list))
    return out
  }, [lessons, groupBy])
}

/* ------------------------------------ TO'R ------------------------------------ */

export function ScheduleGridView(p: ViewProps) {
  const byColumn = useByColumn(p.lessons, p.groupBy)
  const bodyH = p.slots.length * ROW_H

  return (
    <div
      className={cn('overflow-auto rounded-lg border', BORDER)}
      style={{ maxHeight: p.maxHeight }}
      onScroll={() => p.onHover(null)}
    >
      <div className="relative" style={{ width: TIME_W + p.columns.length * COL_W }}>
        {/* Sarlavha qatori — yopishqoq */}
        <div className="sticky top-0 z-20 flex bg-white">
          <div
            className={cn('sticky left-0 z-30 shrink-0 border-b border-r bg-[#F9FAFB]', BORDER)}
            style={{ width: TIME_W, height: HEAD_H }}
          />
          {p.columns.map((c) => (
            <div
              key={c.key || '__none'}
              title={c.name}
              className={cn(
                'flex shrink-0 items-center justify-center truncate border-b border-r px-2 text-[14px] font-semibold text-[#374151]',
                BORDER,
              )}
              style={{ width: COL_W, height: HEAD_H }}
            >
              <span className="truncate">{c.name}</span>
            </div>
          ))}
        </div>

        <div className="flex">
          {/* Vaqt ustuni — yopishqoq */}
          <div className="sticky left-0 z-10 shrink-0 bg-[#F9FAFB]" style={{ width: TIME_W }}>
            {p.slots.map((s) => (
              <div
                key={s.start}
                className={cn(
                  'flex items-center justify-center border-b border-r text-[13px] font-medium text-[#6B7280]',
                  BORDER,
                )}
                style={{ height: ROW_H }}
              >
                {s.label}
              </div>
            ))}
          </div>

          {p.columns.map((c) => (
            <div key={c.key || '__none'} className="relative shrink-0" style={{ width: COL_W, height: bodyH }}>
              {p.slots.map((s) => (
                <div
                  key={s.start}
                  className={cn('flex items-center justify-center border-b border-r text-[12px] text-[#D1D5DB]', BORDER)}
                  style={{ height: ROW_H }}
                >
                  —
                </div>
              ))}
              {(byColumn.get(c.key) ?? []).map(({ item, lane, lanes }) => {
                const s = toMinutes(item.start) ?? p.rangeFrom
                const e = toMinutes(item.end) ?? s
                const inner = COL_W - 8
                return (
                  <LessonBlock
                    key={item.groupId}
                    lesson={item}
                    fields={p.fields}
                    layout="grid"
                    canOpen={p.canOpenGroup}
                    onHover={p.onHover}
                    style={{
                      top: ((s - p.rangeFrom) / SLOT_MINUTES) * ROW_H + 2,
                      height: Math.max(((e - s) / SLOT_MINUTES) * ROW_H - 4, 18),
                      left: 4 + (inner / lanes) * lane,
                      width: inner / lanes - (lanes > 1 ? 2 : 0),
                    }}
                  />
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

/* ------------------------------------ RO'YXAT ------------------------------------ */

export function ScheduleListView(p: ViewProps) {
  const byColumn = useByColumn(p.lessons, p.groupBy)
  const width = LIST_OWNER_W + p.slots.length * LIST_SLOT_W

  return (
    <div
      className={cn('overflow-auto rounded-lg border', BORDER)}
      style={{ maxHeight: p.maxHeight }}
      onScroll={() => p.onHover(null)}
    >
      <div className="relative" style={{ width }}>
        {/* Vaqt sarlavhalari — yopishqoq */}
        <div className="sticky top-0 z-20 flex bg-white">
          <div
            className={cn('sticky left-0 z-30 shrink-0 border-b border-r bg-[#F9FAFB]', BORDER)}
            style={{ width: LIST_OWNER_W, height: HEAD_H }}
          />
          {p.slots.map((s) => (
            <div
              key={s.start}
              className={cn(
                'flex shrink-0 items-center justify-center border-b border-r text-[13px] font-medium text-[#6B7280]',
                BORDER,
              )}
              style={{ width: LIST_SLOT_W, height: HEAD_H }}
            >
              {s.label}
            </div>
          ))}
        </div>

        {p.columns.map((c) => {
          const items = byColumn.get(c.key) ?? []
          const lanes = items.reduce((m, x) => Math.max(m, x.lanes), 1)
          const rowH = Math.max(LIST_ROW_H, lanes * (LIST_BAR_H + 2) + 4)
          return (
            <div key={c.key || '__none'} className="flex">
              <div
                title={c.name}
                className={cn(
                  'sticky left-0 z-10 flex shrink-0 items-center justify-center border-b border-r bg-white px-2 text-[14px] font-semibold text-[#374151]',
                  BORDER,
                )}
                style={{ width: LIST_OWNER_W, height: rowH }}
              >
                <span className="truncate">{c.name}</span>
              </div>
              <div className="relative flex" style={{ height: rowH }}>
                {p.slots.map((s) => (
                  <div
                    key={s.start}
                    className={cn('flex shrink-0 items-center justify-center border-b border-r text-[12px] text-[#D1D5DB]', BORDER)}
                    style={{ width: LIST_SLOT_W, height: rowH }}
                  >
                    —
                  </div>
                ))}
                {items.map(({ item, lane }) => {
                  const s = toMinutes(item.start) ?? p.rangeFrom
                  const e = toMinutes(item.end) ?? s
                  return (
                    <LessonBlock
                      key={item.groupId}
                      lesson={item}
                      fields={p.fields}
                      layout="list"
                      canOpen={p.canOpenGroup}
                      onHover={p.onHover}
                      style={{
                        left: ((s - p.rangeFrom) / SLOT_MINUTES) * LIST_SLOT_W + 2,
                        width: Math.max(((e - s) / SLOT_MINUTES) * LIST_SLOT_W - 4, 24),
                        top: 3 + lane * (LIST_BAR_H + 2),
                        height: LIST_BAR_H,
                      }}
                    />
                  )
                })}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

/* ------------------------------------ DARS BLOKI ------------------------------------ */

const TITLE_CLS = ['text-[11px] font-bold', 'text-[10px] font-bold', 'text-[10px] font-semibold']

function LessonBlock({
  lesson,
  fields,
  layout,
  canOpen,
  onHover,
  style,
}: {
  lesson: ScheduleGridGroup
  fields: FieldSettings
  layout: 'grid' | 'list'
  canOpen: boolean
  onHover: (h: HoverInfo | null) => void
  style: CSSProperties
}) {
  const bg = lessonColor(lesson)
  const fg = textOn(bg)
  const lines = [fields.title1, fields.title2, fields.title3, fields.row1, fields.row2, fields.row3].map((f) =>
    fieldValue(lesson, f),
  )
  const footer = fieldValue(lesson, fields.footer)
  const FooterIcon = footer?.icon === 'users' ? IconUsers : IconBook

  const content =
    layout === 'grid' ? (
      <>
        {lines.map((v, i) =>
          v ? (
            <div key={i} className={cn('truncate leading-[14px]', i < 3 ? TITLE_CLS[i] : 'text-[10px] font-normal')}>
              {v.text}
            </div>
          ) : null,
        )}
        {footer && (
          <div className="mt-0.5 flex items-center gap-1 text-[10px] font-semibold leading-[14px]">
            <FooterIcon size={12} stroke={2} className="shrink-0" />
            <span className="truncate">{footer.text}</span>
          </div>
        )}
      </>
    ) : (
      <>
        <div className="min-w-0 flex-1">
          {lines[0] && <div className="truncate text-[11px] font-bold leading-[15px]">{lines[0].text}</div>}
          <div className="truncate text-[10px] leading-[15px]">
            {lines
              .slice(1)
              .filter((v): v is NonNullable<typeof v> => v !== null && v.text !== '')
              .map((v) => v.text)
              .join('  •  ')}
          </div>
        </div>
        {footer && (
          <div className="flex shrink-0 items-center gap-1 text-[10px] font-semibold">
            <FooterIcon size={12} stroke={2} />
            {footer.text}
          </div>
        )}
      </>
    )

  const className = cn(
    'absolute z-[1] overflow-hidden rounded-lg shadow-[0_4px_12px_rgba(0,0,0,0.15)] transition-[filter] hover:brightness-95',
    layout === 'grid' ? (lesson.minutes <= SLOT_MINUTES ? 'px-2 py-1' : 'p-2') : 'flex items-center gap-2 px-2',
  )
  const common = {
    className,
    style: { ...style, backgroundColor: bg, color: fg },
    onMouseEnter: (e: ReactMouseEvent<HTMLElement>) =>
      onHover({ lesson, rect: e.currentTarget.getBoundingClientRect() }),
    onMouseLeave: () => onHover(null),
  }

  return canOpen ? (
    <Link to={`/admin/classes/${lesson.groupId}`} {...common}>
      {content}
    </Link>
  ) : (
    <div {...common}>{content}</div>
  )
}

/* ------------------------------------ TOOLTIP ------------------------------------ */

const TIP_W = 280

/** Qora tooltip: kalit/qiymat qatorlari (ma'lumoti yo'q qator chiqmaydi). */
export function LessonTooltip({ info, date }: { info: HoverInfo; date: Date }) {
  const l = info.lesson
  const rows: [string, string][] = (
    [
      ['Guruh', l.groupName],
      ['Xona', l.roomName],
      ['Dars vaqti', `${l.start} - ${l.end}`],
      ["O'qituvchi", l.teacherName],
      ['Kurs', l.courseName],
      ['Sana', formatIsoDate(toIso(date))],
      ['Boshlangan vaqti', formatIsoDate(l.startDate)],
      ['Tugash vaqti', formatIsoDate(l.endDate)],
    ] as [string, string][]
  ).filter(([, v]) => v)

  // Blokning o'ng tomonida; sig'masa — chap tomonida. Pastdan chiqib ketmasin.
  const vw = typeof window !== 'undefined' ? window.innerWidth : 1280
  const vh = typeof window !== 'undefined' ? window.innerHeight : 800
  const left = info.rect.right + 8 + TIP_W > vw ? Math.max(8, info.rect.left - 8 - TIP_W) : info.rect.right + 8
  const top = Math.max(8, Math.min(info.rect.top, vh - 24 - rows.length * 20))

  return (
    <div
      role="tooltip"
      className="pointer-events-none fixed z-[70] rounded-lg bg-[#1F2937] px-3 py-2 text-[12px] leading-5 text-white shadow-lg"
      style={{ left, top, width: TIP_W }}
    >
      {rows.map(([k, v]) => (
        <div key={k} className="flex gap-2">
          <span className="shrink-0 text-white/60">{k}:</span>
          <span className="min-w-0 break-words font-medium">{v}</span>
        </div>
      ))}
    </div>
  )
}

function toIso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}
