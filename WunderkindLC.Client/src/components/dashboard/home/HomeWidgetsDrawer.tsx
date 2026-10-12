import { useEffect, useState } from 'react'
import { IconChevronDown, IconChevronUp, IconX } from '@tabler/icons-react'
import { HOME_CARDS } from '@/lib/homeCards'
import {
  allowedBlocks,
  swapItems,
  toggleItem,
  type HomeLayout,
  type HomeLayoutItem,
  type WidgetAccess,
} from '@/lib/homeWidgets'
import { cn } from '@/lib/utils'

/**
 * «Vidjetlar» — bosh sahifa kartochka va bloklarini yoqish/o'chirish va tartiblash (▲/▼). Qoralama
 * ICHKARIDA turadi va faqat «Saqlash» bosilganda serverga ketadi; xato bo'lsa oyna YOPILMAYDI, sabab
 * pastda qizil satrda (error-visibility.md). Foydalanuvchiga ruxsat etilmagan karta/blok ro'yxatda
 * ko'rinmaydi, lekin sozlamada o'z joyida qoladi (keyin ruxsat berilsa o'sha tartibda chiqadi).
 */
export function HomeWidgetsDrawer({
  initial,
  access,
  onSave,
  onReset,
  onClose,
}: {
  initial: HomeLayout
  access: WidgetAccess
  /** Promise — xatoda reject qiladi (oyna ochiq qoladi). */
  onSave: (layout: HomeLayout) => Promise<void>
  onReset: () => Promise<void>
  onClose: () => void
}) {
  const [draft, setDraft] = useState<HomeLayout>(initial)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const cardLabels = new Map(HOME_CARDS.map((c) => [c.key as string, c]))
  const cards = draft.cards.filter((c) => {
    const def = cardLabels.get(c.key)
    return !!def && access.can(def.perm, 'view')
  })
  const blocks = allowedBlocks(draft, access)

  /** Ko'rinadigan (ruxsatli) ro'yxatdagi qo'shni bilan o'rin almashadi (`swapItems`). */
  const move = (list: 'cards' | 'blocks', visible: HomeLayoutItem[], i: number, dir: -1 | 1) => {
    const other = visible[i + dir]
    if (!other) return
    setDraft((d) => ({ ...d, [list]: swapItems(d[list], visible[i].key, other.key) }))
  }

  const run = async (fn: () => Promise<void>) => {
    if (busy) return
    setBusy(true)
    setError(null)
    try {
      await fn()
    } catch (e) {
      setError(e instanceof Error ? e.message : "Saqlab bo'lmadi")
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="fixed inset-0 z-[80]" role="dialog" aria-modal="true" aria-label="Vidjetlar">
      <button type="button" aria-label="Yopish" className="absolute inset-0 bg-black/30" onClick={onClose} />
      <div className="absolute inset-y-0 right-0 flex w-[400px] max-w-full flex-col bg-white shadow-2xl">
        <div className="flex items-start justify-between gap-3 border-b border-[#DBE0E6] px-5 py-4">
          <div>
            <h2 className="text-[16px] font-semibold text-black">Vidjetlar</h2>
            <p className="mt-0.5 text-[13px] text-[#6B7280]">Bosh sahifada nima va qaysi tartibda ko'rinsin</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Yopish"
            className="inline-flex h-8 w-8 items-center justify-center rounded-lg text-[#6B7280] hover:bg-[#F0F2F2]"
          >
            <IconX size={20} stroke={2} />
          </button>
        </div>

        <div className="flex-1 space-y-5 overflow-y-auto px-5 py-4">
          {cards.length > 0 && (
            <section>
              <label className="mb-2 flex items-center justify-between gap-2">
                <span className="text-[14px] font-semibold text-black">Kartochkalar</span>
                <span className="flex items-center gap-1.5 text-[12px] text-[#374151]">
                  <input
                    type="checkbox"
                    checked={draft.cardsVisible}
                    onChange={(e) => setDraft((d) => ({ ...d, cardsVisible: e.target.checked }))}
                    className="h-4 w-4 accent-[#3D68FF]"
                  />
                  Ko'rsatilsin
                </span>
              </label>
              <ItemList
                items={cards}
                label={(k) => cardLabels.get(k)?.label ?? k}
                disabled={!draft.cardsVisible}
                onToggle={(k) => setDraft((d) => ({ ...d, cards: toggleItem(d.cards, k) }))}
                onMove={(i, dir) => move('cards', cards, i, dir)}
              />
            </section>
          )}

          <section>
            <h3 className="mb-2 text-[14px] font-semibold text-black">Bloklar</h3>
            {blocks.length === 0 ? (
              <p className="text-[12px] text-[#6B7280]">Sizga ochiq blok yo'q.</p>
            ) : (
              <ItemList
                items={blocks}
                label={(k) => blocks.find((b) => b.key === k)?.def.label ?? k}
                hint={(k) => blocks.find((b) => b.key === k)?.def.description}
                onToggle={(k) => setDraft((d) => ({ ...d, blocks: toggleItem(d.blocks, k) }))}
                onMove={(i, dir) => move('blocks', blocks, i, dir)}
              />
            )}
          </section>
        </div>

        {error && <p className="px-5 pb-1 text-[13px] text-red-600">{error}</p>}
        <div className="flex items-center justify-between gap-2 border-t border-[#DBE0E6] px-5 py-3">
          <button
            type="button"
            disabled={busy}
            onClick={() => run(onReset)}
            className="inline-flex h-9 items-center rounded-lg border border-[#DBE0E6] px-3 text-[13px] font-medium text-[#374151] hover:bg-[#F0F2F2] disabled:opacity-60"
          >
            Standartga qaytarish
          </button>
          <button
            type="button"
            disabled={busy}
            onClick={() => run(() => onSave(draft))}
            className="inline-flex h-9 items-center rounded-lg bg-[#3D68FF] px-5 text-[14px] font-medium text-white hover:bg-[#2F57E8] disabled:opacity-60"
          >
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </div>
    </div>
  )
}

function ItemList({
  items,
  label,
  hint,
  disabled,
  onToggle,
  onMove,
}: {
  items: HomeLayoutItem[]
  label: (key: string) => string
  hint?: (key: string) => string | undefined
  disabled?: boolean
  onToggle: (key: string) => void
  onMove: (index: number, dir: -1 | 1) => void
}) {
  return (
    <ul className={cn('space-y-1.5', disabled && 'opacity-50')}>
      {items.map((it, i) => (
        <li key={it.key} className="flex items-center gap-2 rounded-lg bg-[#F0F2F2] px-3 py-2">
          <label className="flex min-w-0 flex-1 items-center gap-2">
            <input
              type="checkbox"
              checked={it.visible}
              disabled={disabled}
              onChange={() => onToggle(it.key)}
              className="h-4 w-4 shrink-0 accent-[#3D68FF]"
            />
            <span className="min-w-0">
              <span className="block truncate text-[13px] text-black">{label(it.key)}</span>
              {hint?.(it.key) && <span className="block truncate text-[11px] text-[#6B7280]">{hint(it.key)}</span>}
            </span>
          </label>
          <button
            type="button"
            aria-label="Yuqoriga"
            disabled={disabled || i === 0}
            onClick={() => onMove(i, -1)}
            className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[#3D68FF] hover:bg-white disabled:opacity-30"
          >
            <IconChevronUp size={16} stroke={2} />
          </button>
          <button
            type="button"
            aria-label="Pastga"
            disabled={disabled || i === items.length - 1}
            onClick={() => onMove(i, 1)}
            className="inline-flex h-7 w-7 items-center justify-center rounded-md text-[#3D68FF] hover:bg-white disabled:opacity-30"
          >
            <IconChevronDown size={16} stroke={2} />
          </button>
        </li>
      ))}
    </ul>
  )
}
