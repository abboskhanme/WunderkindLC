import { useMemo, useState } from 'react'
import {
  DndContext, DragOverlay, PointerSensor, useSensor, useSensors,
  type DragEndEvent, type DragStartEvent,
} from '@dnd-kit/core'
import { Plus } from 'lucide-react'
import type { ContactRequestItem } from '@/api/services/contacts'
import { bucketOf, isTodo, stageKeyOf, type BoardColumn } from '@/lib/contactDue'
import { ContactColumn, type ContactColumnAdmin } from './ContactColumn'
import { ContactCardContent, type ContactCardActions } from './ContactCard'

/** Sudrab tashlangandagi natija — sahifa shuni bajaradi. */
export interface DropIntent {
  request: ContactRequestItem
  /** Maqsad ustun (faqat "Bosqich" rejimida). */
  stageId?: string
  /**
   * HOLAT O'ZGARMAYDI — faqat ustun ko'chadi. Bunda "Bog'lanildi" oynasi OCHILMAYDI: bu
   * bog'lanish emas, taxta ichidagi siljish (server uni izoh sifatida tarixga yozadi).
   */
  sameStatus: boolean
  /** Holat o'zgarganda — taklif qilinadigan keyingi qadam. */
  nextStatus?: 'callback' | 'done' | 'failed'
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
 * <p>⚠️ <b>Sudrash bosqichni JIMGINA o'zgartirmaydi.</b> Serverda holatni ko'chiradigan
 * endpoint YO'Q va bu ataylab: har o'tish — HODISA (`ContactAttempt`), ya'ni "kim, qachon,
 * nima dedi" yozilishi shart (`.claude/rules/contacts.md` §2, §7). Shuning uchun BOSHQA
 * bosqichdagi ustunga tashlanganda "Bog'lanildi" oynasi keyingi qadam OLDINDAN TANLANGAN
 * holda ochiladi.</p>
 *
 * <p>O'SHA bosqichdagi ikkinchi ustunga ko'chirish esa oddiy siljish — u holatni
 * o'zgartirmaydi, shuning uchun oyna so'ralmaydi va darhol saqlanadi.</p>
 */
export function ContactBoard({
  columns,
  items,
  groupBy,
  today,
  actions,
  onCardClick,
  onDrop,
  onAddColumn,
  columnAdmin,
}: {
  columns: BoardColumn[]
  items: ContactRequestItem[]
  groupBy: 'due' | 'status'
  today: string
  actions: ContactCardActions
  onCardClick: (r: ContactRequestItem) => void
  onDrop: (intent: DropIntent) => void
  /** Berilsa — oxirida "Ustun qo'shish" tugmasi chiqadi ("Bosqich" rejimi). */
  onAddColumn?: () => void
  /** Berilsa — har ustun sarlavhasida tahrirlash/o'chirish/ko'chirish tugmalari. */
  columnAdmin?: Omit<ContactColumnAdmin, 'isFirst' | 'isLast'>
}) {
  const [activeId, setActiveId] = useState<string | null>(null)
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }))

  const knownIds = useMemo(() => new Set(columns.map((c) => c.key)), [columns])

  /** Talab qaysi ustunga tushadi (guruhlash rejimiga qarab). */
  const columnKeyOf = useMemo(
    () => (r: ContactRequestItem) =>
      groupBy === 'status' ? stageKeyOf(r, knownIds) : bucketOf(r.status, r.dueDate, today),
    [groupBy, today, knownIds],
  )

  const grouped = useMemo(() => {
    const map = new Map<string, ContactRequestItem[]>(columns.map((c) => [c.key, []]))
    for (const r of items) {
      // Ustuni yo'q talab (masalan "Ochiqlar" ko'rinishidagi yakuniy talab) JIMGINA
      // tashlanmaydi — u shunchaki hozirgi kesimga kirmaydi (sahifa uni sanoqqa ham qo'shmaydi).
      map.get(columnKeyOf(r))?.push(r)
    }
    return map
  }, [columns, items, columnKeyOf])

  const activeItem = activeId ? items.find((r) => r.id === activeId) ?? null : null

  /**
   * Shu ustun AYNI PAYTDA sudralayotgan kartani qabul qiladimi.
   *
   * ⚠️ Qaror kartaga BOG'LIQ: masalan "Bog'lanish kerak" ga bog'langan ustun boshqa
   * bosqichdagi kartani qabul QILMAYDI (serverda `new` ga qaytish taqiqlangan), lekin
   * o'sha bosqichdagi kartani qabul qiladi — u holat o'zgarishi emas, oddiy siljish.
   */
  const acceptsActive = (col: BoardColumn): boolean => {
    if (!actions.canWrite) return false
    if (!activeItem) return !!col.drop || !!col.stage
    if (columnKeyOf(activeItem) === col.key) return false
    if (col.stage) return col.stage.baseStatus === activeItem.status || !!col.drop
    return !!col.drop
  }

  const handleDragEnd = (e: DragEndEvent) => {
    setActiveId(null)
    const { active, over } = e
    if (!over) return
    const r = items.find((x) => x.id === String(active.id))
    if (!r) return
    const col = columns.find((c) => c.key === String(over.id))
    if (!col) return
    // O'z ustuniga tashlash — hech narsa qilinmaydi (tasodifiy siljish oyna ochmasin).
    if (columnKeyOf(r) === col.key) return

    // "Bosqich" rejimi: bazaviy holat BIR XIL bo'lsa — oddiy siljish, oyna so'ralmaydi.
    if (col.stage && col.stage.baseStatus === r.status) {
      onDrop({ request: r, stageId: col.stage.id, sameStatus: true })
      return
    }
    if (!col.drop) return

    onDrop({
      request: r,
      stageId: col.stage?.id,
      sameStatus: false,
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
        {columns.map((col, i) => (
          <ContactColumn
            key={col.key}
            column={col}
            items={grouped.get(col.key) ?? []}
            today={today}
            actions={actions}
            onCardClick={onCardClick}
            accepts={acceptsActive(col)}
            urgent={groupBy === 'due' && isTodo(col.key as never)}
            dragging={!!activeId}
            admin={
              columnAdmin && { ...columnAdmin, isFirst: i === 0, isLast: i === columns.length - 1 }
            }
          />
        ))}

        {onAddColumn && (
          <button
            type="button"
            onClick={onAddColumn}
            className="flex min-h-[200px] w-[300px] shrink-0 flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed border-slate-200 text-sm font-medium text-slate-400 transition-colors hover:border-brand-300 hover:bg-brand-50/40 hover:text-brand-600"
          >
            <Plus className="h-5 w-5" /> Ustun qo'shish
          </button>
        )}
      </div>

      <DragOverlay dropAnimation={null}>
        {activeItem && <ContactCardContent r={activeItem} today={today} dragging />}
      </DragOverlay>
    </DndContext>
  )
}
