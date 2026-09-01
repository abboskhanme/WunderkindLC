import { useMemo, useState } from 'react'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors,
  type DragEndEvent, type DragStartEvent,
} from '@dnd-kit/core'
import type { ContactRequestItem } from '@/api/services/contacts'
import { bucketOf, isTodo, type BoardColumn } from '@/lib/contactDue'
import { ContactColumn } from './ContactColumn'
import { ContactCardContent, type ContactCardActions } from './ContactCard'

/** Sudrab tashlangandagi taklif — sahifa shu bilan "Bog'lanildi" oynasini ochadi. */
export interface DropIntent {
  request: ContactRequestItem
  nextStatus: 'callback' | 'done' | 'failed'
  /** Faqat `callback` uchun ("yyyy-MM-dd"). */
  dueDate?: string
}

/** "yyyy-MM-dd" — `today` dan `days` kun keyin. */
function shiftDay(today: string, days: number): string {
  const t = Date.parse(`${today}T00:00:00Z`)
  if (Number.isNaN(t)) return today
  return new Date(t + days * 86_400_000).toISOString().slice(0, 10)
}

/**
 * KANBAN TAXTASI — navbat ustunlarga bo'lib ko'rsatiladi.
 *
 * <p>⚠️ **Sudrash holatni JIMGINA o'zgartirmaydi.** Serverda kartani ustundan ustunga ko'chiradigan
 * endpoint YO'Q va bu ataylab: har o'tish — HODISA (`ContactAttempt`), ya'ni "kim, qachon, nima
 * dedi" yozilishi shart (`.claude/rules/contacts.md` §2, §7). Shuning uchun karta tashlanganda
 * "Bog'lanildi" oynasi keyingi qadam OLDINDAN TANLANGAN holda ochiladi: operator faqat natijani
 * va javobni yozadi. Ya'ni sudrash — bu "tezkor yo'l", hisobotni buzadigan yashirin o'tish emas.</p>
 */
export function ContactBoard({
  columns,
  items,
  groupBy,
  today,
  actions,
  onCardClick,
  onDrop,
}: {
  columns: BoardColumn[]
  items: ContactRequestItem[]
  groupBy: 'due' | 'status'
  today: string
  actions: ContactCardActions
  onCardClick: (r: ContactRequestItem) => void
  onDrop: (intent: DropIntent) => void
}) {
  const [activeId, setActiveId] = useState<string | null>(null)
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  /** Talab qaysi ustunga tushadi (guruhlash rejimiga qarab). */
  const columnKeyOf = useMemo(
    () => (r: ContactRequestItem) => (groupBy === 'status' ? r.status : bucketOf(r.status, r.dueDate, today)),
    [groupBy, today],
  )

  const grouped = useMemo(() => {
    const map = new Map<string, ContactRequestItem[]>(columns.map((c) => [c.key, []]))
    for (const r of items) {
      const key = columnKeyOf(r)
      // Ustuni yo'q talab (masalan "Ochiqlar" ko'rinishida yopilib qolgani) JIMGINA
      // tashlanmaydi — u shunchaki hozirgi kesimga kirmaydi.
      map.get(key)?.push(r)
    }
    return map
  }, [columns, items, columnKeyOf])

  const activeItem = activeId ? items.find((r) => r.id === activeId) ?? null : null

  const handleDragEnd = (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const r = items.find((x) => x.id === String(active.id))
    if (!r) return
    const col = columns.find((c) => c.key === String(over.id))
    if (!col?.drop) return
    // O'z ustuniga tashlash — hech narsa qilinmaydi (tasodifiy siljish oyna ochmasin).
    if (columnKeyOf(r) === col.key) return

    onDrop({
      request: r,
      nextStatus: col.drop.nextStatus,
      dueDate: col.drop.offsetDays == null ? undefined : shiftDay(today, col.drop.offsetDays),
    })
  }

  return (
    <DndContext
      sensors={sensors}
      onDragStart={(e: DragStartEvent) => setActiveId(String(e.active.id))}
      onDragEnd={handleDragEnd}
      onDragCancel={() => setActiveId(null)}
    >
      <div className="kanban">
        {columns.map((col) => (
          <ContactColumn
            key={col.key}
            column={col}
            items={grouped.get(col.key) ?? []}
            today={today}
            actions={actions}
            onCardClick={onCardClick}
            urgent={groupBy === 'due' && isTodo(col.key as never)}
            dragging={!!activeId}
          />
        ))}
      </div>

      <DragOverlay dropAnimation={null}>
        {activeItem && <ContactCardContent r={activeItem} today={today} dragging />}
      </DragOverlay>
    </DndContext>
  )
}
